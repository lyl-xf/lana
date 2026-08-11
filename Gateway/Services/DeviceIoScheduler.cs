using System.Threading.Channels;
using Lana.Gateway.Models;
using Lana.Gateway.Protocol;

namespace Lana.Gateway.Services;

/// <summary>
/// 按物理链路（<see cref="DeviceLinkKey"/>）串行 IO：高优 Channel 优先于 Poll，Poll 按点/路径可打断。
/// </summary>
public sealed class DeviceIoScheduler : IDeviceIoScheduler
{
    private readonly ProtocolSessionFactory _sessionFactory;
    private readonly IDevicePointCache? _pointCache;
    private readonly IDeviceDataSnapshotStore? _snapshotStore;
    private readonly object _gate = new();
    private readonly Dictionary<string, LinkQueue> _links = new();
    private readonly Dictionary<long, string> _deviceLinkRefs = new();
    private DeviceIoConnectionHandlers? _handlers;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public DeviceIoScheduler(
        ProtocolSessionFactory sessionFactory,
        IDevicePointCache? pointCache = null,
        IDeviceDataSnapshotStore? snapshotStore = null)
    {
        _sessionFactory = sessionFactory;
        _pointCache = pointCache;
        _snapshotStore = snapshotStore;
        _cts = new CancellationTokenSource();
    }

    /// <inheritdoc />
    public void SetConnectionHandlers(DeviceIoConnectionHandlers? handlers)
        => _handlers = handlers;

    /// <inheritdoc />
    public Task<DevicePollResult> PollDeviceAsync(Device device, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var link = GetOrCreateLink(device);
        return link.EnqueuePollAsync(device, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ProtocolResult<object?>> ReadAsync(
        Device device,
        string address,
        DataType dataType,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var link = GetOrCreateLink(device);
        return link.EnqueueReadAsync(device, address, dataType, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ProtocolResult> WriteAsync(
        Device device,
        string address,
        DataType dataType,
        string? value,
        DeviceVariable? patchVariable = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var link = GetOrCreateLink(device);
        return link.EnqueueWriteAsync(device, address, dataType, value, patchVariable, cancellationToken);
    }

    /// <inheritdoc />
    public Task<MqttIoCommandResult> ExecuteMqttCommandAsync(
        Device device,
        MqttCommandDto command,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var link = GetOrCreateLink(device);
        return link.EnqueueMqttAsync(device, command, cancellationToken);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        List<LinkQueue> links;
        lock (_gate)
        {
            links = _links.Values.ToList();
            _links.Clear();
            _deviceLinkRefs.Clear();
        }

        foreach (var link in links)
            await link.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_cts != null)
        {
            try { await _cts.CancelAsync(); } catch { /* ignore */ }
            _cts.Dispose();
            _cts = null;
        }

        await StopAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void ReleaseDevice(long deviceId)
    {
        lock (_gate)
            ReleaseDeviceRefNoLock(deviceId);
    }

    private LinkQueue GetOrCreateLink(Device device)
    {
        var key = DeviceLinkKey.FromDevice(device);
        lock (_gate)
        {
            if (_deviceLinkRefs.TryGetValue(device.Id, out var oldKey) && oldKey != key)
                ReleaseDeviceRefNoLock(device.Id);

            if (!_links.TryGetValue(key, out var link))
            {
                link = new LinkQueue(
                    device,
                    _sessionFactory,
                    _pointCache,
                    _snapshotStore,
                    () => _handlers,
                    () => _cts?.Token ?? CancellationToken.None);
                _links[key] = link;
            }

            if (!_deviceLinkRefs.ContainsKey(device.Id))
            {
                link.AddDeviceRef();
                _deviceLinkRefs[device.Id] = key;
            }

            return link;
        }
    }

    private void ReleaseDeviceRefNoLock(long deviceId)
    {
        if (!_deviceLinkRefs.TryGetValue(deviceId, out var key))
            return;

        _deviceLinkRefs.Remove(deviceId);
        if (_links.TryGetValue(key, out var link) && link.ReleaseDeviceRef())
            _links.Remove(key);
    }

    /// <summary>单链路队列与消费者。</summary>
    private sealed class LinkQueue
    {
        private readonly ProtocolSessionFactory _sessionFactory;
        private readonly IDevicePointCache? _pointCache;
        private readonly IDeviceDataSnapshotStore? _snapshotStore;
        private readonly Func<DeviceIoConnectionHandlers?> _handlers;
        private readonly Func<CancellationToken> _cancellationToken;
        private readonly Channel<IoWorkItem> _urgentChannel;
        private readonly Channel<PollWorkRequest> _pollChannel;
        private readonly IDeviceProtocolSession _session;
        private readonly Task _consumerTask;
        private int _deviceRefCount;
        private PollWorkRequestState? _activePoll;

        public LinkQueue(
            Device seedDevice,
            ProtocolSessionFactory sessionFactory,
            IDevicePointCache? pointCache,
            IDeviceDataSnapshotStore? snapshotStore,
            Func<DeviceIoConnectionHandlers?> handlers,
            Func<CancellationToken> cancellationToken)
        {
            _sessionFactory = sessionFactory;
            _pointCache = pointCache;
            _snapshotStore = snapshotStore;
            _handlers = handlers;
            _cancellationToken = cancellationToken;
            _urgentChannel = Channel.CreateUnbounded<IoWorkItem>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
            });
            _pollChannel = Channel.CreateUnbounded<PollWorkRequest>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
            });

            _session = sessionFactory.CreateSession(seedDevice);
            _consumerTask = Task.Run(ConsumeAsync);
        }

        public void AddDeviceRef() => Interlocked.Increment(ref _deviceRefCount);

        public bool ReleaseDeviceRef()
        {
            if (Interlocked.Decrement(ref _deviceRefCount) > 0)
                return false;

            _pollChannel.Writer.TryComplete();
            _urgentChannel.Writer.TryComplete();
            return true;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _pollChannel.Writer.TryComplete();
            _urgentChannel.Writer.TryComplete();
            try
            {
                await _consumerTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { /* expected */ }

            try { _session.Close(); } catch { /* ignore */ }
            try { _session.Dispose(); } catch { /* ignore */ }
        }

        public Task<DevicePollResult> EnqueuePollAsync(Device device, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<DevicePollResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => tcs.TrySetCanceled(ct));
            if (!_pollChannel.Writer.TryWrite(new PollWorkRequest(device, tcs)))
                tcs.TrySetCanceled();
            return tcs.Task;
        }

        public Task<ProtocolResult<object?>> EnqueueReadAsync(
            Device device,
            string address,
            DataType dataType,
            CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<ProtocolResult<object?>>(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => tcs.TrySetCanceled(ct));
            _urgentChannel.Writer.TryWrite(new ReadWorkItem(device, address, dataType, tcs));
            return tcs.Task;
        }

        public Task<ProtocolResult> EnqueueWriteAsync(
            Device device,
            string address,
            DataType dataType,
            string? value,
            DeviceVariable? patchVariable,
            CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<ProtocolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => tcs.TrySetCanceled(ct));
            _urgentChannel.Writer.TryWrite(new WriteWorkItem(device, address, dataType, value, patchVariable, tcs, _pointCache, _snapshotStore));
            return tcs.Task;
        }

        public Task<MqttIoCommandResult> EnqueueMqttAsync(
            Device device,
            MqttCommandDto command,
            CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<MqttIoCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => tcs.TrySetCanceled(ct));
            _urgentChannel.Writer.TryWrite(new MqttWorkItem(device, command, tcs, _pointCache, _snapshotStore));
            return tcs.Task;
        }

        private async Task ConsumeAsync()
        {
            var ct = _cancellationToken();
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    while (_urgentChannel.Reader.TryRead(out var urgent))
                    {
                        await urgent.ExecuteAsync(_session, _handlers()).ConfigureAwait(false);
                    }

                    if (_activePoll != null)
                    {
                        if (_activePoll.IsComplete)
                        {
                            _activePoll.Complete(_handlers());
                            _activePoll = null;
                            continue;
                        }

                        if (_urgentChannel.Reader.TryRead(out var preemptUrgent))
                        {
                            await preemptUrgent.ExecuteAsync(_session, _handlers()).ConfigureAwait(false);
                            continue;
                        }

                        _activePoll.Step(_session);
                        continue;
                    }

                    var pollWait = _pollChannel.Reader.WaitToReadAsync(ct).AsTask();
                    var urgentWait = _urgentChannel.Reader.WaitToReadAsync(ct).AsTask();
                    var done = await Task.WhenAny(pollWait, urgentWait).ConfigureAwait(false);

                    if (done == urgentWait && _urgentChannel.Reader.TryRead(out var u))
                    {
                        await u.ExecuteAsync(_session, _handlers()).ConfigureAwait(false);
                        continue;
                    }

                    if (_pollChannel.Reader.TryRead(out var pollReq))
                    {
                        _activePoll = new PollWorkRequestState(pollReq);
                        if (!_activePoll.Prepare(_session, _handlers()))
                        {
                            _activePoll.Complete(_handlers());
                            _activePoll = null;
                        }
                    }
                }
            }
            catch (OperationCanceledException) { /* shutdown */ }
            catch (Exception ex)
            {
                Console.WriteLine($"[IO Scheduler] Consumer error: {ex.Message}");
            }
        }
    }

    private interface IoWorkItem
    {
        Task ExecuteAsync(IDeviceProtocolSession session, DeviceIoConnectionHandlers? handlers);
    }

    private sealed class ReadWorkItem(
        Device device,
        string address,
        DataType dataType,
        TaskCompletionSource<ProtocolResult<object?>> tcs) : IoWorkItem
    {
        public Task ExecuteAsync(IDeviceProtocolSession session, DeviceIoConnectionHandlers? handlers)
        {
            try
            {
                if (!TryEnsureConnected(session, device, handlers, out var error))
                {
                    tcs.TrySetResult(ProtocolResult<object?>.Fail(error));
                    return Task.CompletedTask;
                }

                var result = session.Read(address, (ProtocolDataType)(int)dataType);
                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                HandleSessionError(session, device, handlers, ex);
                tcs.TrySetResult(ProtocolResult<object?>.Fail(ex.Message));
            }

            return Task.CompletedTask;
        }
    }

    private sealed class WriteWorkItem(
        Device device,
        string address,
        DataType dataType,
        string? value,
        DeviceVariable? patchVariable,
        TaskCompletionSource<ProtocolResult> tcs,
        IDevicePointCache? pointCache,
        IDeviceDataSnapshotStore? snapshotStore) : IoWorkItem
    {
        public Task ExecuteAsync(IDeviceProtocolSession session, DeviceIoConnectionHandlers? handlers)
        {
            try
            {
                if (!TryEnsureConnected(session, device, handlers, out var error))
                {
                    tcs.TrySetResult(ProtocolResult.Fail(error));
                    return Task.CompletedTask;
                }

                var result = session.Write(address, (ProtocolDataType)(int)dataType, value);
                if (result.Success && patchVariable != null && !string.IsNullOrWhiteSpace(patchVariable.Alias))
                    ApplyWritePatch(device, patchVariable, value, pointCache, snapshotStore);

                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                HandleSessionError(session, device, handlers, ex);
                tcs.TrySetResult(ProtocolResult.Fail(ex.Message));
            }

            return Task.CompletedTask;
        }
    }

    private sealed class MqttWorkItem(
        Device device,
        MqttCommandDto command,
        TaskCompletionSource<MqttIoCommandResult> tcs,
        IDevicePointCache? pointCache,
        IDeviceDataSnapshotStore? snapshotStore) : IoWorkItem
    {
        public Task ExecuteAsync(IDeviceProtocolSession session, DeviceIoConnectionHandlers? handlers)
        {
            var now = DateTime.UtcNow;
            try
            {
                if (handlers?.IsInBackoff?.Invoke(device.Id, now) == true)
                {
                    tcs.TrySetResult(new MqttIoCommandResult
                    {
                        Connected = false,
                        Error = "设备连接失败退避中，请稍后重试",
                        ReplyTo = ResolveReplyTo(command),
                    });
                    return Task.CompletedTask;
                }

                if (!TryEnsureConnected(session, device, handlers, out var connectError))
                {
                    tcs.TrySetResult(new MqttIoCommandResult
                    {
                        Connected = false,
                        Error = connectError ?? "连接失败",
                        ReplyTo = ResolveReplyTo(command),
                    });
                    return Task.CompletedTask;
                }

                var action = command.Action?.ToLowerInvariant() ?? string.Empty;

                if (action == "write" && command.Writes != null)
                {
                    foreach (var write in command.Writes)
                    {
                        var variable = device.Variables.FirstOrDefault(v => v.Alias == write.Key);
                        if (variable == null || variable.ReadWrite == ReadWriteAccess.ReadOnly)
                            continue;

                        var strVal = write.Value?.ToString();
                        if (strVal == null)
                            continue;

                        var wr = session.Write(
                            variable.Address,
                            (ProtocolDataType)(int)variable.DataType,
                            strVal);
                        if (!wr.Success)
                            Console.WriteLine($"[MQTT] Write failed for {variable.Alias}: {wr.Error}");
                        else
                            ApplyWritePatch(device, variable, strVal, pointCache, snapshotStore);
                    }
                }

                Dictionary<string, object>? replyPayload = null;
                if (action is "query" or "write")
                {
                    replyPayload = new Dictionary<string, object>();
                    DevicePayloadBuilder.FillQueryPayload(device, session, command.Reads, replyPayload);
                }

                tcs.TrySetResult(new MqttIoCommandResult
                {
                    Connected = true,
                    ReplyPayload = replyPayload is { Count: > 0 } ? replyPayload : null,
                    ReplyTo = ResolveReplyTo(command),
                });
            }
            catch (Exception ex)
            {
                HandleSessionError(session, device, handlers, ex);
                tcs.TrySetResult(new MqttIoCommandResult
                {
                    Connected = false,
                    Error = ex.Message,
                    ReplyTo = ResolveReplyTo(command),
                });
            }

            return Task.CompletedTask;
        }
    }

    private sealed class PollWorkRequest(Device device, TaskCompletionSource<DevicePollResult> completion)
    {
        public Device Device { get; } = device;
        public TaskCompletionSource<DevicePollResult> Completion { get; } = completion;
    }

    private sealed class PollWorkRequestState
    {
        private readonly PollWorkRequest _request;
        private readonly Dictionary<string, object> _payload = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<PollStep> _steps;
        private int _index;
        private bool _connected;
        private bool _prepared;
        private string? _error;

        public PollWorkRequestState(PollWorkRequest request)
        {
            _request = request;
            _steps = BuildSteps(request.Device);
        }

        public bool IsComplete => _prepared && _index >= _steps.Count;

        public bool Prepare(IDeviceProtocolSession session, DeviceIoConnectionHandlers? handlers)
        {
            if (_prepared)
                return _connected;

            _prepared = true;
            if (!TryEnsureConnected(session, _request.Device, handlers, out _error))
            {
                _connected = false;
                _index = _steps.Count;
                return false;
            }

            _connected = session.IsConnected;
            return _connected;
        }

        public void Step(IDeviceProtocolSession session)
        {
            if (!_connected || _index >= _steps.Count)
                return;

            _steps[_index].Execute(session, _request.Device, _payload);
            _index++;
        }

        public void Complete(DeviceIoConnectionHandlers? handlers)
        {
            if (_connected)
            {
                handlers?.ClearFailure?.Invoke(_request.Device.Id);
                handlers?.MarkOnline?.Invoke(_request.Device.Id);
            }
            else
            {
                handlers?.MarkOffline?.Invoke(_request.Device.Id);
            }

            _request.Completion.TrySetResult(new DevicePollResult
            {
                Payload = _payload,
                Connected = _connected,
                Error = _connected ? null : (_error ?? "连接失败"),
            });
        }

        private static List<PollStep> BuildSteps(Device device)
        {
            var steps = new List<PollStep>();
            if (device.ProtocolType == ProtocolType.HttpClient)
            {
                foreach (var variable in device.Variables)
                {
                    if (DeviceVariablePollRules.ShouldPollHttpPath(variable))
                        steps.Add(new HttpPollStep(variable));
                }

                return steps;
            }

            foreach (var variable in device.Variables)
            {
                if (DeviceVariablePollRules.ShouldPoll(variable))
                    steps.Add(new ModbusPollStep(variable));
            }

            return steps;
        }

        private interface PollStep
        {
            void Execute(IDeviceProtocolSession session, Device device, Dictionary<string, object> payload);
        }

        private sealed class ModbusPollStep(DeviceVariable variable) : PollStep
        {
            public void Execute(IDeviceProtocolSession session, Device device, Dictionary<string, object> payload)
            {
                try
                {
                    var read = session.Read(variable.Address, (ProtocolDataType)(int)variable.DataType);
                    if (read.Success && read.Value != null)
                        payload[variable.Alias] = read.Value;
                }
                catch
                {
                    /* 单点失败跳过 */
                }
            }
        }

        private sealed class HttpPollStep(DeviceVariable variable) : PollStep
        {
            public void Execute(IDeviceProtocolSession session, Device device, Dictionary<string, object> payload)
            {
                if (session is not HttpClientDeviceSession httpSession)
                    return;

                try
                {
                    var map = httpSession.ReadKeyValueMap(variable.HttpKeyJsonPath, variable.HttpValueJsonPath);
                    if (!map.Success || map.Value == null)
                        return;

                    foreach (var kv in map.Value)
                    {
                        if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value == null) continue;
                        payload[kv.Key] = kv.Value;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Device {device.Id}] HttpClient poll step error: {ex.Message}");
                }
            }
        }
    }

    private static bool TryEnsureConnected(
        IDeviceProtocolSession session,
        Device device,
        DeviceIoConnectionHandlers? handlers,
        out string? error)
    {
        error = null;
        var now = DateTime.UtcNow;

        if (handlers?.IsInBackoff?.Invoke(device.Id, now) == true)
        {
            error = "设备连接失败退避中";
            handlers.MarkOffline?.Invoke(device.Id);
            return false;
        }

        try
        {
            if (!session.IsConnected)
            {
                var open = session.Open();
                if (!open.Success)
                {
                    error = open.Error ?? "连接失败";
                    handlers?.RegisterFailure?.Invoke(device.Id, now, error);
                    handlers?.MarkOffline?.Invoke(device.Id);
                    return false;
                }
            }

            handlers?.ClearFailure?.Invoke(device.Id);
            handlers?.MarkOnline?.Invoke(device.Id);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            HandleSessionError(session, device, handlers, ex);
            return false;
        }
    }

    private static void HandleSessionError(
        IDeviceProtocolSession session,
        Device device,
        DeviceIoConnectionHandlers? handlers,
        Exception ex)
    {
        try { session.Close(); } catch { /* ignore */ }
        handlers?.RegisterFailure?.Invoke(device.Id, DateTime.UtcNow, ex.Message);
        handlers?.MarkOffline?.Invoke(device.Id);
    }

    private static void ApplyWritePatch(
        Device device,
        DeviceVariable variable,
        string? rawValue,
        IDevicePointCache? pointCache,
        IDeviceDataSnapshotStore? snapshotStore)
    {
        if (string.IsNullOrWhiteSpace(variable.Alias) || rawValue == null)
            return;

        var now = DateTime.UtcNow;
        object parsed = rawValue;
        if (variable.DataType is DataType.Bool or DataType.Coil or DataType.Discrete
            && bool.TryParse(rawValue, out var b))
            parsed = b;

        pointCache?.PatchPoint(device.Id, device, variable.Alias, parsed, now);

        var entry = DeviceSnapshotBuilder.BuildPatchEntry(variable, parsed, now);
        if (entry != null)
            snapshotStore?.PatchPoint(device.Id, device.Name, entry);
    }

    private static int ResolveReplyTo(MqttCommandDto command)
    {
        var action = command.Action?.Trim().ToLowerInvariant();
        if (action == "write")
            return command.Reads != null ? 3 : 1;
        return 2;
    }
}
