using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using Lana.Gateway.Models;
using Lana.Gateway.Protocol;
using MQTTnet;
using MQTTnet.Client;

namespace Lana.Gateway.Services
{
    public class DataCollectionWorker : IAsyncDisposable
    {
        private static readonly JsonSerializerOptions MqttJsonOptions = new()
        {
            // 上报中文 key/value 保持汉字，避免 \u98CE\u529B
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>连接失败退避：15s 起指数增长，上限 5 分钟。</summary>
        private const double BackoffInitialSeconds = 15;
        private const double BackoffMaxSeconds = 300;

        private readonly IGatewayConfigStore _configStore;
        private readonly ProtocolSessionFactory _sessionFactory;
        private IMqttClient? _mqttClient;
        private readonly Dictionary<long, DateTime> _lastPollTimes = new();
        private DateTime _lastDbCheck = DateTime.MinValue;
        private DateTime _lastOnlineStatusReport = DateTime.MinValue;
        private MqttConfig? _cachedMqttConfig;
        private List<Device> _cachedDevices = new();
        private readonly Dictionary<long, bool> _deviceOnlineStatus = new();
        private Dictionary<long, bool>? _previousOnlineStatusSnapshot;

        // Shared client pool:
        // - deviceId -> configKey
        // - configKey -> client (shared by same protocol/ip/port/serial/plc settings)
        private readonly Dictionary<long, string> _deviceClientRefs = new();
        private readonly Dictionary<string, IDeviceProtocolSession> _sharedSessions = new();
        private readonly Dictionary<string, int> _sharedClientRefCounts = new();
        private readonly object _clientPoolLock = new();

        /// <summary>设备连接失败退避状态（与采集锁分离，MQTT 可快速判断）。</summary>
        private readonly ConcurrentDictionary<long, DeviceFailState> _failStates = new();

        private CancellationTokenSource? _cts;
        private Task? _loopTask;
        private readonly object _lifecycleLock = new();
        private bool _disposed;

        private sealed class DeviceFailState
        {
            public int FailCount;
            public DateTime BackoffUntilUtc;
            public readonly object Gate = new();
        }

        public DataCollectionWorker(IGatewayConfigStore configStore, ProtocolSessionFactory sessionFactory)
        {
            _configStore = configStore;
            _sessionFactory = sessionFactory;
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            lock (_lifecycleLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_loopTask != null)
                    return Task.CompletedTask;

                _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _loopTask = ExecuteAsync(_cts.Token);
                return Task.CompletedTask;
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            Task? loopTask;
            CancellationTokenSource? cts;

            lock (_lifecycleLock)
            {
                loopTask = _loopTask;
                cts = _cts;
                _loopTask = null;
                _cts = null;
            }

            if (cts != null)
            {
                try { await cts.CancelAsync(); } catch { /* ignore */ }
            }

            if (loopTask != null)
            {
                try
                {
                    await loopTask.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException) { /* expected */ }
                catch (Exception) when (cancellationToken.IsCancellationRequested) { /* stop requested */ }
            }

            lock (_clientPoolLock)
            {
                foreach (var session in _sharedSessions.Values)
                {
                    try { session.Close(); } catch { /* ignore */ }
                    try { session.Dispose(); } catch { /* ignore */ }
                }
                _sharedSessions.Clear();
                _sharedClientRefCounts.Clear();
                _deviceClientRefs.Clear();
            }

            if (_mqttClient != null)
            {
                try
                {
                    if (_mqttClient.IsConnected)
                        await _mqttClient.DisconnectAsync();
                }
                catch { /* ignore */ }

                _mqttClient.ApplicationMessageReceivedAsync -= HandleMqttMessageAsync;
                _mqttClient.Dispose();
                _mqttClient = null;
            }

            cts?.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            lock (_lifecycleLock)
            {
                if (_disposed) return;
                _disposed = true;
            }

            await StopAsync();
        }

        private bool IsInBackoff(long deviceId, DateTime utcNow)
        {
            if (!_failStates.TryGetValue(deviceId, out var state)) return false;
            lock (state.Gate)
                return utcNow < state.BackoffUntilUtc;
        }

        private void ClearDeviceFailure(long deviceId)
        {
            _failStates.TryRemove(deviceId, out _);
        }

        private void RegisterDeviceFailure(long deviceId, DateTime utcNow, string? reason)
        {
            var state = _failStates.GetOrAdd(deviceId, _ => new DeviceFailState());
            double seconds;
            lock (state.Gate)
            {
                state.FailCount = Math.Min(state.FailCount + 1, 12);
                seconds = Math.Min(BackoffInitialSeconds * Math.Pow(2, state.FailCount - 1), BackoffMaxSeconds);
                state.BackoffUntilUtc = utcNow.AddSeconds(seconds);
            }

            Console.WriteLine($"[Device {deviceId}] 连接失败，退避 {seconds:0}s：{reason}");
        }

        private string GetDeviceConfigHash(Device device)
        {
            return $"{device.ProtocolType}_{device.Ip}_{device.Port}_{device.PortName}_{device.BaudRate}_{device.DataBits}_{device.StopBits}_{device.Parity}_{device.PlcVersion}_{device.PluginConfigJson}";
        }

        private IDeviceProtocolSession AcquireSessionForDevice(Device device)
        {
            var configKey = GetDeviceConfigHash(device);

            lock (_clientPoolLock)
            {
                if (_deviceClientRefs.TryGetValue(device.Id, out var oldKey) && oldKey != configKey)
                {
                    ReleaseClientByKeyNoLock(oldKey);
                    _deviceClientRefs.Remove(device.Id);
                }

                if (!_deviceClientRefs.TryGetValue(device.Id, out var currentKey))
                {
                    if (!_sharedSessions.ContainsKey(configKey))
                    {
                        _sharedSessions[configKey] = _sessionFactory.CreateSession(device);
                        _sharedClientRefCounts[configKey] = 0;
                    }

                    _sharedClientRefCounts[configKey]++;
                    _deviceClientRefs[device.Id] = configKey;
                    currentKey = configKey;
                }

                return _sharedSessions[currentKey];
            }
        }

        private void ReleaseClientByDeviceId(long deviceId)
        {
            lock (_clientPoolLock)
            {
                if (_deviceClientRefs.TryGetValue(deviceId, out var key))
                {
                    _deviceClientRefs.Remove(deviceId);
                    ReleaseClientByKeyNoLock(key);
                }
            }
        }

        private void ReleaseClientByKeyNoLock(string key)
        {
            if (!_sharedClientRefCounts.TryGetValue(key, out var refCount))
                return;

            refCount--;
            if (refCount > 0)
            {
                _sharedClientRefCounts[key] = refCount;
                return;
            }

            _sharedClientRefCounts.Remove(key);
            if (_sharedSessions.TryGetValue(key, out var session))
            {
                try { session.Close(); } catch { /* ignore */ }
                try { session.Dispose(); } catch { /* ignore */ }
                _sharedSessions.Remove(key);
            }
        }

        private async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.UtcNow;

                    // Refresh configuration every 15 seconds
                    if ((now - _lastDbCheck).TotalSeconds >= 15)
                    {
                        _cachedMqttConfig = await _configStore.GetMqttConfigAsync(stoppingToken);
                        _cachedDevices = await _configStore.GetActiveDevicesWithVariablesAsync(stoppingToken);
                        _lastDbCheck = now;

                        // Cleanup inactive or deleted devices from cache
                        var activeDeviceIds = _cachedDevices.Select(d => d.Id).ToHashSet();
                        var toRemove = _deviceClientRefs.Keys.Where(id => !activeDeviceIds.Contains(id)).ToList();
                        foreach (var id in toRemove)
                        {
                            ReleaseClientByDeviceId(id);
                            _lastPollTimes.Remove(id);
                            _deviceOnlineStatus.Remove(id);
                            ClearDeviceFailure(id);
                        }
                    }

                    if (_cachedMqttConfig is { IsEnabled: true } && _cachedDevices.Any())
                    {
                        await EnsureMqttConnectedAsync(_cachedMqttConfig, stoppingToken);

                        foreach (var device in _cachedDevices)
                        {
                            // PollInterval = 0：关闭该设备数据采集
                            if (device.PollInterval <= 0)
                                continue;

                            var interval = device.PollInterval;

                            // 退避中跳过采集，避免反复连接超时拖死整轮
                            if (IsInBackoff(device.Id, now))
                                continue;

                            if (_lastPollTimes.TryGetValue(device.Id, out var lastPollTime))
                            {
                                if ((now - lastPollTime).TotalMilliseconds < interval)
                                    continue;
                            }

                            _lastPollTimes[device.Id] = now;

                            var session = AcquireSessionForDevice(device);
                            var payload = new Dictionary<string, object>();

                            lock (session)
                            {
                                try
                                {
                                    if (!session.IsConnected)
                                    {
                                        var openResult = session.Open();
                                        if (!openResult.Success)
                                        {
                                            _deviceOnlineStatus[device.Id] = false;
                                            RegisterDeviceFailure(device.Id, now, openResult.Error ?? "连接失败");
                                        }
                                    }

                                    if (session.IsConnected)
                                    {
                                        ClearDeviceFailure(device.Id);
                                        _deviceOnlineStatus[device.Id] = true;
                                        FillDevicePayload(device, session, payload);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"[Device {device.Id}] Error: {ex.Message}");
                                    try { session.Close(); } catch { /* ignore */ }
                                    _deviceOnlineStatus[device.Id] = false;
                                    RegisterDeviceFailure(device.Id, now, ex.Message);
                                }
                            }

                            if (payload.Any() && _mqttClient != null && _mqttClient.IsConnected)
                            {
                                var json = JsonSerializer.Serialize(new
                                {
                                    deviceId = device.Id,
                                    timestamp = DateTime.Now,
                                    data = payload
                                }, MqttJsonOptions);

                                var message = new MqttApplicationMessageBuilder()
                                    .WithTopic(_cachedMqttConfig.PubTopic)
                                    .WithPayload(json)
                                    .Build();

                                await _mqttClient.PublishAsync(message, stoppingToken);
                            }
                        }

                        await PublishOnlineStatusAsync(_cachedMqttConfig, now, stoppingToken);
                    }
                    else
                    {
                        // MQTT 关闭或未配置：断开客户端，停止采集上报
                        await DisconnectMqttAsync();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Worker] Error: {ex.Message}");
                }

                await Task.Delay(100, stoppingToken);
            }
        }

        /// <summary>
        /// 填充设备上报 data。HttpClient 按物模型 key/value JSON 路径展开；其它协议按 Alias 读点。
        /// </summary>
        private static void FillDevicePayload(Device device, IDeviceProtocolSession session, Dictionary<string, object> payload)
        {
            if (device.ProtocolType == ProtocolType.HttpClient && session is HttpClientDeviceSession httpSession)
            {
                foreach (var variable in device.Variables)
                {
                    try
                    {
                        var map = httpSession.ReadKeyValueMap(variable.HttpKeyJsonPath, variable.HttpValueJsonPath);
                        if (!map.Success || map.Value == null)
                        {
                            Console.WriteLine($"[Device {device.Id}] HttpClient KV map failed ({variable.HttpKeyJsonPath}/{variable.HttpValueJsonPath}): {map.Error}");
                            continue;
                        }

                        foreach (var kv in map.Value)
                        {
                            if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value == null) continue;
                            payload[kv.Key] = kv.Value;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Device {device.Id}] Exception reading HttpClient KV map: {ex.Message}");
                    }
                }
                return;
            }

            foreach (var variable in device.Variables)
            {
                object? val = null;
                try
                {
                    var read = session.Read(variable.Address, (ProtocolDataType)(int)variable.DataType);
                    if (read.Success)
                        val = read.Value;
                    else
                        Console.WriteLine($"[Device {device.Id}] Failed to read {variable.Alias} ({variable.Address}): {read.Error}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Device {device.Id}] Exception reading {variable.Alias} ({variable.Address}): {ex.Message}");
                }

                if (val != null) payload[variable.Alias] = val;
            }
        }

        private async Task PublishOnlineStatusAsync(MqttConfig config, DateTime now, CancellationToken ct)
        {
            if (_mqttClient == null || !_mqttClient.IsConnected || string.IsNullOrWhiteSpace(config.OnlineStatusTopic))
                return;

            var interval = config.OnlineStatusReportInterval > 0 ? config.OnlineStatusReportInterval : 30000;
            if ((now - _lastOnlineStatusReport).TotalMilliseconds < interval)
                return;

            _lastOnlineStatusReport = now;

            var currentSnapshot = _cachedDevices.ToDictionary(
                d => d.Id,
                d => _deviceOnlineStatus.TryGetValue(d.Id, out var online) && online);

            var changedDeviceIds = _previousOnlineStatusSnapshot == null
                ? new HashSet<long>()
                : currentSnapshot
                    .Where(kv => !_previousOnlineStatusSnapshot.TryGetValue(kv.Key, out var previousOnline) || previousOnline != kv.Value)
                    .Select(kv => kv.Key)
                    .ToHashSet();

            var statuses = _cachedDevices
                .OrderBy(d => d.SortOrder)
                .ThenBy(d => d.Id)
                .Where(d => changedDeviceIds.Contains(d.Id))
                .Select(d => new
                {
                    deviceId = d.Id,
                    name = d.Name,
                    isActive = d.IsActive,
                    online = currentSnapshot[d.Id]
                })
                .ToList();

            var json = JsonSerializer.Serialize(new
            {
                timestamp = DateTime.Now,
                devices = statuses
            }, MqttJsonOptions);

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(config.OnlineStatusTopic)
                .WithPayload(json)
                .Build();

            await _mqttClient.PublishAsync(message, ct);
            _previousOnlineStatusSnapshot = currentSnapshot;
        }

        private async Task EnsureMqttConnectedAsync(MqttConfig config, CancellationToken ct)
        {
            if (!config.IsEnabled)
            {
                await DisconnectMqttAsync();
                return;
            }

            if (_mqttClient == null)
            {
                _mqttClient = new MqttFactory().CreateMqttClient();
                _mqttClient.ApplicationMessageReceivedAsync += HandleMqttMessageAsync;
            }

            if (!_mqttClient.IsConnected)
            {
                try
                {
                    var optionsBuilder = new MqttClientOptionsBuilder()
                        .WithTcpServer(config.BrokerIp, config.Port)
                        .WithClientId(config.ClientId);

                    if (!string.IsNullOrEmpty(config.Username))
                    {
                        optionsBuilder.WithCredentials(config.Username, config.Password);
                    }

                    var options = optionsBuilder.Build();
                    await _mqttClient.ConnectAsync(options, ct);

                    if (!string.IsNullOrEmpty(config.SubTopic))
                    {
                        await _mqttClient.SubscribeAsync(config.SubTopic, cancellationToken: ct);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MQTT] Connection failed: {ex.Message}");
                }
            }
        }

        private async Task DisconnectMqttAsync()
        {
            var client = _mqttClient;
            if (client is null)
                return;

            try
            {
                if (client.IsConnected)
                    await client.DisconnectAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MQTT] Disconnect failed: {ex.Message}");
            }
            finally
            {
                try { client.ApplicationMessageReceivedAsync -= HandleMqttMessageAsync; } catch { /* ignore */ }
                try { client.Dispose(); } catch { /* ignore */ }
                _mqttClient = null;
            }
        }

        private async Task HandleMqttMessageAsync(MqttApplicationMessageReceivedEventArgs e)
        {
            // 立即返回，避免离线设备连接超时堵住 MQTTnet 收包线程，导致在线设备指令排队
            string payload;
            try
            {
                payload = e.ApplicationMessage.ConvertPayloadToString();
            }
            catch
            {
                return;
            }

            Console.WriteLine($"[MQTT] Received command: {payload}");

            _ = Task.Run(async () =>
            {
                try
                {
                    await ExecuteMqttCommandAsync(payload);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MQTT] Command dispatch error: {ex.Message}");
                }
            });

            await Task.CompletedTask;
        }

        private async Task ExecuteMqttCommandAsync(string payload)
        {
            try
            {
                if (_cachedMqttConfig is not { IsEnabled: true })
                    return;

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var command = JsonSerializer.Deserialize<MqttCommand>(payload, options);
                if (command == null || command.DeviceId <= 0)
                    return;

                var device = _cachedDevices.FirstOrDefault(d => d.Id == command.DeviceId);
                if (device == null || !device.IsActive)
                {
                    Console.WriteLine($"[MQTT] Device {command.DeviceId} not found or inactive.");
                    return;
                }

                var now = DateTime.UtcNow;
                var session = AcquireSessionForDevice(device);
                MqttApplicationMessage? messageToPublish = null;
                var connected = false;

                // 退避中且未连接：快速失败，绝不在此线程长时间 Open，避免拖死其它设备指令
                // 先快速检查会话状态（已连接的在线设备直接放行）
                lock (session)
                {
                    connected = session.IsConnected;
                }

                if (!connected && IsInBackoff(device.Id, now))
                {
                    Console.WriteLine($"[MQTT] Device {device.Id} 处于连接退避中，跳过本次指令（不影响其它设备）。");
                    await PublishMqttErrorAsync(device.Id, "设备连接失败退避中，请稍后重试", ResolveReplyTo(command));
                    return;
                }

                lock (session)
                {
                    try
                    {
                        if (!session.IsConnected)
                        {
                            var openResult = session.Open();
                            if (!openResult.Success)
                            {
                                Console.WriteLine($"[MQTT] Failed to connect to device {device.Id}: {openResult.Error}");
                                RegisterDeviceFailure(device.Id, DateTime.UtcNow, openResult.Error ?? "连接失败");
                                _deviceOnlineStatus[device.Id] = false;
                            }
                        }

                        connected = session.IsConnected;
                        if (connected)
                        {
                            ClearDeviceFailure(device.Id);
                            _deviceOnlineStatus[device.Id] = true;

                            if (command.Action?.ToLower() == "write" && command.Writes != null)
                            {
                                foreach (var write in command.Writes)
                                {
                                    var variable = device.Variables.FirstOrDefault(v => v.Alias == write.Key);
                                    if (variable != null && variable.ReadWrite != ReadWriteAccess.ReadOnly)
                                    {
                                        try
                                        {
                                            var strVal = write.Value?.ToString();
                                            if (strVal != null)
                                            {
                                                Console.WriteLine($"[MQTT] Writing to {variable.Alias} ({variable.Address}): {strVal}");
                                                var wr = session.Write(variable.Address, (ProtocolDataType)(int)variable.DataType, strVal);
                                                if (!wr.Success)
                                                    Console.WriteLine($"[MQTT] Write failed for {variable.Alias}: {wr.Error}");
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"[MQTT] Write error for {variable.Alias}: {ex.Message}");
                                        }
                                    }
                                    else
                                    {
                                        Console.WriteLine($"[MQTT] Variable {write.Key} not found or is read-only.");
                                    }
                                }
                            }

                            if (command.Action?.ToLower() == "query" || command.Action?.ToLower() == "write")
                            {
                                var resultPayload = new Dictionary<string, object>();
                                var replyTo = ResolveReplyTo(command);

                                if (device.ProtocolType == ProtocolType.HttpClient)
                                {
                                    FillDevicePayload(device, session, resultPayload);
                                }
                                else
                                {
                                    var variablesToQuery = (command.Reads != null && command.Reads.Any())
                                        ? device.Variables.Where(v => command.Reads.Contains(v.Alias))
                                        : device.Variables;

                                    foreach (var variable in variablesToQuery)
                                    {
                                        object? val = null;
                                        try
                                        {
                                            var read = session.Read(variable.Address, (ProtocolDataType)(int)variable.DataType);
                                            if (read.Success)
                                                val = read.Value;
                                            else
                                                Console.WriteLine($"[MQTT] Failed to read {variable.Alias} ({variable.Address}): {read.Error}");
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"[MQTT] Exception reading {variable.Alias} ({variable.Address}): {ex.Message}");
                                        }

                                        if (val != null) resultPayload[variable.Alias] = val;
                                    }
                                }

                                if (resultPayload.Any() && _cachedMqttConfig != null && !string.IsNullOrEmpty(_cachedMqttConfig.PubTopic))
                                {
                                    var json = JsonSerializer.Serialize(new
                                    {
                                        deviceId = device.Id,
                                        timestamp = DateTime.Now,
                                        data = resultPayload,
                                        replyTo
                                    }, MqttJsonOptions);

                                    messageToPublish = new MqttApplicationMessageBuilder()
                                        .WithTopic(_cachedMqttConfig.PubTopic)
                                        .WithPayload(json)
                                        .Build();
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[MQTT] Device {device.Id} error: {ex.Message}");
                        try { session.Close(); } catch { /* ignore */ }
                        RegisterDeviceFailure(device.Id, DateTime.UtcNow, ex.Message);
                        _deviceOnlineStatus[device.Id] = false;
                        connected = false;
                    }
                }

                if (!connected && messageToPublish == null)
                {
                    await PublishMqttErrorAsync(device.Id, "设备当前不可达", ResolveReplyTo(command));
                    return;
                }

                if (messageToPublish != null && _mqttClient != null && _mqttClient.IsConnected)
                    await _mqttClient.PublishAsync(messageToPublish);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MQTT] Execute command error: {ex.Message}");
            }
        }

        /// <summary>
        /// replyTo：1=单写(不传 reads)；2=单读(query)；3=读写一体(write 且带 reads，含空数组)。
        /// 每条指令独立解析、独立回包，并发时各自带上对应标志。
        /// </summary>
        private static int ResolveReplyTo(MqttCommand command)
        {
            var action = command.Action?.Trim().ToLowerInvariant();
            if (action == "write")
                return command.Reads != null ? 3 : 1;
            // query 及其它默认按读
            return 2;
        }

        private async Task PublishMqttErrorAsync(long deviceId, string error, int replyTo = 2)
        {
            try
            {
                if (_mqttClient == null || !_mqttClient.IsConnected || _cachedMqttConfig == null)
                    return;
                if (string.IsNullOrEmpty(_cachedMqttConfig.PubTopic))
                    return;

                var json = JsonSerializer.Serialize(new
                {
                    deviceId,
                    timestamp = DateTime.Now,
                    success = false,
                    error,
                    replyTo
                }, MqttJsonOptions);

                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(_cachedMqttConfig.PubTopic)
                    .WithPayload(json)
                    .Build();

                await _mqttClient.PublishAsync(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MQTT] Publish error reply failed: {ex.Message}");
            }
        }

        private class MqttCommand
        {
            public string? MessageId { get; set; }
            public long DeviceId { get; set; }
            public string Action { get; set; } = string.Empty; // "query" or "write"
            public Dictionary<string, object>? Writes { get; set; }
            /// <summary>null=未传 reads（单写→replyTo=1）；非 null（含 []）=读写一体→replyTo=3。</summary>
            public List<string>? Reads { get; set; }
        }
    }
}
