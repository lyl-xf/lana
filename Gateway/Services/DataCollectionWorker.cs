using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using Lana.Gateway.Models;
using Lana.Gateway.Protocol;
using MQTTnet;
using MQTTnet.Client;

namespace Lana.Gateway.Services
{
    /// <summary>
    /// 登录后后台采集与 MQTT 上报/订阅 Worker。
    /// <para>
    /// 周期从 <see cref="IGatewayConfigStore"/> 拉取活跃设备；
    /// <c>PollInterval == 0</c> 的设备跳过轮询（仍可调试读写）。
    /// 会话按连接参数哈希共享（同 IP/串口配置共用一条连接）。
    /// </para>
    /// <para>
    /// 轮询结果写入 <see cref="IDevicePointCache"/> 与 <see cref="IDeviceDataSnapshotStore"/>；
    /// MQTT 指令读写经 <see cref="IDeviceIoScheduler"/> 与 Poll 共用链路队列。
    /// </para>
    /// <para>
    /// 生命周期由 <c>MainViewModel</c> 在登录 Start、登出 Stop/Dispose。
    /// 扩展采集逻辑时优先改 <see cref="DevicePayloadBuilder"/> / MQTT 订阅处理，注意失败退避。
    /// </para>
    /// </summary>
    public class DataCollectionWorker : IAsyncDisposable
    {
        /// <summary>MQTT 遥测 JSON 序列化选项（保留中文不转义）。</summary>
        private static readonly JsonSerializerOptions MqttJsonOptions = new()
        {
            // 上报中文 key/value 保持汉字，避免 \u98CE\u529B
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>连接失败退避初始间隔（秒）：15s 起指数增长。</summary>
        private const double BackoffInitialSeconds = 15;

        /// <summary>连接失败退避上限（秒）：最长 5 分钟。</summary>
        private const double BackoffMaxSeconds = 300;

        /// <summary>网关配置持久化存储（设备、变量、MQTT 配置）。</summary>
        private readonly IGatewayConfigStore _configStore;

        /// <summary>统一 IO 仲裁。</summary>
        private readonly IDeviceIoScheduler _ioScheduler;

        /// <summary>共享实时状态；轮询完成后 UpdateDevice，UI 直接绑定 Groups。</summary>
        private readonly IDeviceDataSnapshotStore? _snapshotStore;

        /// <summary>设备点 live 缓存；Poll commit 写入，MQTT 周期上报读取。</summary>
        private readonly IDevicePointCache? _pointCache;

        /// <summary>上次全局 MQTT 周期遥测调度 UTC（TelemetryPublishInterval &gt; 0 时使用）。</summary>
        private DateTime _lastGlobalTelemetryPublish = DateTime.MinValue;

        /// <summary>各设备上次 MQTT 遥测发布时的缓存 Version（用于跳过未变化 payload）。</summary>
        private readonly Dictionary<long, long> _lastTelemetryPublishedVersion = new();

        /// <summary>MQTT 客户端实例；启用 MQTT 时创建并复用。</summary>
        private IMqttClient? _mqttClient;

        /// <summary>各设备上次完成轮询的 UTC 时间，用于 PollInterval 节流。</summary>
        private readonly Dictionary<long, DateTime> _lastPollTimes = new();

        /// <summary>各设备上次 MQTT 遥测发布的 UTC 时间（TelemetryPublishInterval=0 时按设备限频）。</summary>
        private readonly Dictionary<long, DateTime> _lastTelemetryPublishTimes = new();

        /// <summary>上次从数据库刷新配置缓存的 UTC 时间。</summary>
        private DateTime _lastDbCheck = DateTime.MinValue;

        /// <summary>上次发布设备在线状态到 MQTT 的 UTC 时间。</summary>
        private DateTime _lastOnlineStatusReport = DateTime.MinValue;

        /// <summary>缓存的 MQTT 配置（每 15 秒随设备列表一并刷新）。</summary>
        private MqttConfig? _cachedMqttConfig;

        /// <summary>缓存的活跃设备及变量列表（含 PollInterval、协议参数）。</summary>
        private List<Device> _cachedDevices = new();

        /// <summary>各设备当前在线状态（协议连接是否成功）。</summary>
        private readonly Dictionary<long, bool> _deviceOnlineStatus = new();

        /// <summary>上一轮在线状态快照，用于检测变化后增量上报。</summary>
        private Dictionary<long, bool>? _previousOnlineStatusSnapshot;

        /// <summary>设备连接失败退避状态。</summary>
        private readonly ConcurrentDictionary<long, DeviceFailState> _failStates = new();

        /// <summary>防止同一设备上轮询未完成时又启动下一轮（点位多时会超过 PollInterval）。</summary>
        private readonly ConcurrentDictionary<long, byte> _pollingDevices = new();

        /// <summary>主循环取消令牌源，Stop/Dispose 时触发取消。</summary>
        private CancellationTokenSource? _cts;

        /// <summary>后台主循环任务（ExecuteAsync）。</summary>
        private Task? _loopTask;

        /// <summary>Start/Stop/Dispose 生命周期操作的互斥锁。</summary>
        private readonly object _lifecycleLock = new();

        /// <summary>是否已 Dispose，防止重复释放。</summary>
        private bool _disposed;

        /// <summary>
        /// 单设备连接失败退避状态：失败次数、退避截止时间、线程安全门闩。
        /// </summary>
        private sealed class DeviceFailState
        {
            /// <summary>连续失败次数（上限 12，用于指数退避计算）。</summary>
            public int FailCount;

            /// <summary>退避结束 UTC 时间，在此之前跳过连接/轮询。</summary>
            public DateTime BackoffUntilUtc;

            /// <summary>保护 FailCount / BackoffUntilUtc 的细粒度锁。</summary>
            public readonly object Gate = new();
        }

        /// <summary>
        /// 构造数据采集 Worker。
        /// </summary>
        /// <param name="configStore">网关配置存储。</param>
        /// <param name="ioScheduler">统一 IO 调度器。</param>
        /// <param name="snapshotStore">可为 null；正常登录由 MainViewModel 注入单例 Store。</param>
        /// <param name="pointCache">可为 null；与 snapshotStore 成对注入。</param>
        public DataCollectionWorker(
            IGatewayConfigStore configStore,
            IDeviceIoScheduler ioScheduler,
            IDeviceDataSnapshotStore? snapshotStore = null,
            IDevicePointCache? pointCache = null)
        {
            _configStore = configStore;
            _ioScheduler = ioScheduler;
            _snapshotStore = snapshotStore;
            _pointCache = pointCache;
        }

        /// <summary>
        /// 启动后台采集主循环（幂等：已在运行则直接返回）。
        /// </summary>
        /// <param name="cancellationToken">外部取消令牌，与内部 CTS 链接。</param>
        /// <returns>启动完成（不等待主循环结束）。</returns>
        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            lock (_lifecycleLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                // 已在运行则跳过，避免重复启动多个主循环
                if (_loopTask != null)
                    return Task.CompletedTask;

                _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _ioScheduler.SetConnectionHandlers(new DeviceIoConnectionHandlers
                {
                    MarkOnline = id => _deviceOnlineStatus[id] = true,
                    MarkOffline = id => _deviceOnlineStatus[id] = false,
                    RegisterFailure = RegisterDeviceFailure,
                    ClearFailure = ClearDeviceFailure,
                    IsInBackoff = IsInBackoff,
                });
                _loopTask = ExecuteAsync(_cts.Token);
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// 停止主循环并释放客户端池、MQTT 连接等资源。
        /// </summary>
        /// <param name="cancellationToken">等待主循环结束的超时/取消令牌。</param>
        /// <returns>停止完成。</returns>
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            Task? loopTask;
            CancellationTokenSource? cts;

            // 在锁内清空引用，避免 Stop 与 Start 竞态
            lock (_lifecycleLock)
            {
                loopTask = _loopTask;
                cts = _cts;
                _loopTask = null;
                _cts = null;
            }

            // 通知主循环退出
            if (cts != null)
            {
                try { await cts.CancelAsync(); } catch { /* ignore */ }
            }

            // 等待主循环任务结束
            if (loopTask != null)
            {
                try
                {
                    await loopTask.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException) { /* expected */ }
                catch (Exception) when (cancellationToken.IsCancellationRequested) { /* stop requested */ }
            }

            // 断开并销毁 MQTT 客户端
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
            _ioScheduler.SetConnectionHandlers(null);
        }

        /// <summary>
        /// 异步释放 Worker（标记 disposed 后调用 StopAsync）。
        /// </summary>
        /// <returns>释放完成。</returns>
        public async ValueTask DisposeAsync()
        {
            lock (_lifecycleLock)
            {
                if (_disposed) return;
                _disposed = true;
            }

            await StopAsync();
        }

        /// <summary>
        /// 判断设备是否处于连接失败退避窗口内。
        /// </summary>
        /// <param name="deviceId">设备 ID。</param>
        /// <param name="utcNow">当前 UTC 时间。</param>
        /// <returns>在退避中返回 true，否则 false。</returns>
        private bool IsInBackoff(long deviceId, DateTime utcNow)
        {
            if (!_failStates.TryGetValue(deviceId, out var state)) return false;
            lock (state.Gate)
                return utcNow < state.BackoffUntilUtc;
        }

        /// <summary>
        /// 清除设备失败退避状态（连接成功后调用）。
        /// </summary>
        /// <param name="deviceId">设备 ID。</param>
        private void ClearDeviceFailure(long deviceId)
        {
            _failStates.TryRemove(deviceId, out _);
        }

        /// <summary>
        /// 登记设备连接失败并计算指数退避截止时间。
        /// </summary>
        /// <param name="deviceId">设备 ID。</param>
        /// <param name="utcNow">失败发生时的 UTC 时间。</param>
        /// <param name="reason">失败原因（日志用）。</param>
        private void RegisterDeviceFailure(long deviceId, DateTime utcNow, string? reason)
        {
            var state = _failStates.GetOrAdd(deviceId, _ => new DeviceFailState());
            double seconds;
            lock (state.Gate)
            {
                // 失败次数递增（上限 12），指数退避：15s × 2^(n-1)，封顶 300s
                state.FailCount = Math.Min(state.FailCount + 1, 12);
                seconds = Math.Min(BackoffInitialSeconds * Math.Pow(2, state.FailCount - 1), BackoffMaxSeconds);
                state.BackoffUntilUtc = utcNow.AddSeconds(seconds);
            }

            Console.WriteLine($"[Device {deviceId}] 连接失败，退避 {seconds:0}s：{reason}");
        }

        /// <summary>
        /// 后台主循环：刷新配置 → MQTT 连接/订阅 → 按 PollInterval 调度轮询 → 在线状态上报。
        /// </summary>
        /// <param name="stoppingToken">停止取消令牌。</param>
        /// <returns>循环退出时完成。</returns>
        private async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.UtcNow;

                    // ── 配置刷新：每 15 秒从数据库拉取 MQTT 配置与活跃设备 ──
                    if ((now - _lastDbCheck).TotalSeconds >= 15)
                    {
                        _cachedMqttConfig = await _configStore.GetMqttConfigAsync(stoppingToken);
                        _cachedDevices = await _configStore.GetActiveDevicesWithVariablesAsync(stoppingToken);
                        _lastDbCheck = now;

                        // 清理已从库中删除/停用的设备：释放会话、轮询/遥测/在线/退避状态
                        var activeDeviceIds = _cachedDevices.Select(d => d.Id).ToHashSet();
                        var toRemove = _lastPollTimes.Keys
                            .Union(_deviceOnlineStatus.Keys)
                            .Where(id => !activeDeviceIds.Contains(id))
                            .ToList();
                        foreach (var id in toRemove)
                        {
                            _ioScheduler.ReleaseDevice(id);
                            _lastPollTimes.Remove(id);
                            _lastTelemetryPublishTimes.Remove(id);
                            _lastTelemetryPublishedVersion.Remove(id);
                            _deviceOnlineStatus.Remove(id);
                            _pollingDevices.TryRemove(id, out _);
                            ClearDeviceFailure(id);
                        }
                    }

                    // 无活跃设备：短睡后继续，避免空转占 CPU
                    if (!_cachedDevices.Any())
                    {
                        await Task.Delay(100, stoppingToken);
                        continue;
                    }

                    // ── MQTT 连接/订阅（与轮询开关解耦：开启 MQTT 即可收指令）──
                    if (_cachedMqttConfig is { IsEnabled: true })
                        await EnsureMqttConnectedAsync(_cachedMqttConfig, stoppingToken);
                    else
                        await DisconnectMqttAsync();

                    // 轮询总开关：与 MQTT 连接解耦；无配置行时默认开启（与 UI「MQTT 关时仍可本地轮询」一致）
                    var pollingEnabled = _cachedMqttConfig?.EnablePolling ?? true;

                    if (pollingEnabled)
                    {
                        // ── 轮询调度：遍历活跃设备，满足间隔且未退避/未在途则 fire-and-forget ──
                        foreach (var device in _cachedDevices)
                        {
                            // PollInterval <= 0：不参与自动轮询（仍可调试/MQTT 指令读写）
                            if (device.PollInterval <= 0)
                                continue;

                            if (!device.Variables.Any(DeviceVariablePollRules.ShouldPoll))
                                continue;

                            var interval = device.PollInterval;

                            // 连接失败退避中：跳过本轮调度
                            if (IsInBackoff(device.Id, now))
                                continue;

                            // 距上次完成轮询未达间隔：跳过
                            if (_lastPollTimes.TryGetValue(device.Id, out var lastPollTime)
                                && (now - lastPollTime).TotalMilliseconds < interval)
                                continue;

                            // 该设备已有在途轮询任务：跳过，防止重叠 IO
                            if (!_pollingDevices.TryAdd(device.Id, 0))
                                continue;

                            // 异步派发单设备轮询，不阻塞主循环
                            var polledDevice = device;
                            _ = PollDeviceOnceAsync(polledDevice, now, stoppingToken);
                        }

                        // ── 在线状态增量上报（MQTT 已连接且轮询开启时）──
                        if (_cachedMqttConfig is { IsEnabled: true })
                            await PublishOnlineStatusAsync(_cachedMqttConfig, now, stoppingToken);

                        // ── MQTT 周期遥测：独立调度，只读点缓存（TelemetryPublishInterval &gt; 0）──
                        if (_cachedMqttConfig is { IsEnabled: true, TelemetryPublishInterval: > 0 })
                            await TryPublishTelemetryFromCacheAsync(now, stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    // 主循环异常不退出，记录后继续下一轮
                    Console.WriteLine($"[Worker] Error: {ex.Message}");
                }

                // 主循环节拍：100ms，平衡响应性与 CPU 占用
                await Task.Delay(100, stoppingToken);
            }
        }

        /// <summary>
        /// 单设备异步轮询：读 payload → 写快照 → 按需 MQTT 遥测；完成后更新 <c>_lastPollTimes</c>。
        /// </summary>
        /// <param name="device">目标设备（含变量列表）。</param>
        /// <param name="pollStartedUtc">本轮调度时的 UTC 时间。</param>
        /// <param name="ct">取消令牌（遥测发布用）。</param>
        /// <returns>轮询任务完成。</returns>
        private async Task PollDeviceOnceAsync(Device device, DateTime pollStartedUtc, CancellationToken ct)
        {
            try
            {
                var pollResult = await _ioScheduler.PollDeviceAsync(device, ct).ConfigureAwait(false);
                var payload = pollResult.Payload;

                if (!pollResult.Connected && !string.IsNullOrWhiteSpace(pollResult.Error))
                    Console.WriteLine($"[Device {device.Id}] Poll: {pollResult.Error}");

                if (payload.Count > 0)
                {
                    _pointCache?.CommitPoll(device.Id, device, payload, pollStartedUtc);

                    PublishSnapshot(device, payload, pollStartedUtc);

                    if (_cachedMqttConfig is { IsEnabled: true, TelemetryPublishInterval: <= 0 }
                        && _mqttClient != null
                        && _mqttClient.IsConnected
                        && !string.IsNullOrWhiteSpace(_cachedMqttConfig.PubTopic))
                    {
                        await PublishTelemetryForDeviceFromCacheAsync(device, ct).ConfigureAwait(false);
                    }
                }

                _lastPollTimes[device.Id] = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Device {device.Id}] Poll task error: {ex.Message}");
            }
            finally
            {
                _pollingDevices.TryRemove(device.Id, out _);
            }
        }

        /// <summary>
        /// 将本轮采集结果合并进 <see cref="IDeviceDataSnapshotStore"/>（与 MQTT 是否连接无关）。
        /// </summary>
        /// <param name="device">源设备（用于 ID、名称、变量映射）。</param>
        /// <param name="payload">本轮采集的键值对。</param>
        /// <param name="nowUtc">快照更新时间戳。</param>
        private void PublishSnapshot(Device device, Dictionary<string, object> payload, DateTime nowUtc)
        {
            if (_snapshotStore is null || payload.Count == 0)
                return;

            var entries = DeviceSnapshotBuilder.BuildStatusEntries(device, payload, nowUtc);
            if (entries.Count == 0)
                return;

            _snapshotStore.UpdateDevice(device.Id, device.Name, entries);
        }

        /// <summary>
        /// 主循环：按全局 TelemetryPublishInterval 从缓存批量上报遥测。
        /// </summary>
        private async Task TryPublishTelemetryFromCacheAsync(DateTime nowUtc, CancellationToken ct)
        {
            if (_pointCache is null
                || _cachedMqttConfig is null
                || _mqttClient is null
                || !_mqttClient.IsConnected
                || string.IsNullOrWhiteSpace(_cachedMqttConfig.PubTopic))
                return;

            var interval = _cachedMqttConfig.TelemetryPublishInterval;
            if (interval <= 0)
                return;

            if ((nowUtc - _lastGlobalTelemetryPublish).TotalMilliseconds < interval)
                return;

            _lastGlobalTelemetryPublish = nowUtc;

            foreach (var device in _cachedDevices)
            {
                if (device.PollInterval <= 0)
                    continue;

                if (!device.Variables.Any(DeviceVariablePollRules.ShouldPoll))
                    continue;

                await PublishTelemetryForDeviceFromCacheAsync(device, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 从点缓存读取 IncludeInTelemetry 子集并发布 MQTT 遥测。
        /// </summary>
        private async Task PublishTelemetryForDeviceFromCacheAsync(Device device, CancellationToken ct)
        {
            if (_pointCache is null
                || _cachedMqttConfig is null
                || _mqttClient is null
                || !_mqttClient.IsConnected
                || string.IsNullOrWhiteSpace(_cachedMqttConfig.PubTopic))
                return;

            var version = _pointCache.GetVersion(device.Id);
            if (version <= 0)
                return;

            if (_lastTelemetryPublishedVersion.TryGetValue(device.Id, out var lastVersion)
                && lastVersion == version)
                return;

            var payload = _pointCache.GetTelemetryPayload(device);
            if (payload.Count == 0)
                return;

            await PublishTelemetryAsync(device.Id, payload, ct).ConfigureAwait(false);
            _lastTelemetryPublishedVersion[device.Id] = version;
            _lastTelemetryPublishTimes[device.Id] = DateTime.UtcNow;
        }

        /// <summary>
        /// 将设备遥测 JSON 发布到 MQTT PubTopic。
        /// </summary>
        /// <param name="deviceId">设备 ID。</param>
        /// <param name="payload">遥测数据字典。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>发布完成。</returns>
        private async Task PublishTelemetryAsync(
            long deviceId,
            IReadOnlyDictionary<string, object> payload,
            CancellationToken ct)
        {
            if (_cachedMqttConfig is null || _mqttClient is null || !_mqttClient.IsConnected)
                return;

            var json = JsonSerializer.Serialize(new
            {
                deviceId,
                timestamp = DateTime.Now,
                data = payload
            }, MqttJsonOptions);

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(_cachedMqttConfig.PubTopic)
                .WithPayload(json)
                .Build();

            await _mqttClient.PublishAsync(message, ct);
        }

        /// <summary>
        /// 增量发布设备在线状态到 MQTT OnlineStatusTopic（仅变化设备）。
        /// </summary>
        /// <param name="config">MQTT 配置（含主题与上报间隔）。</param>
        /// <param name="now">当前 UTC 时间。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>发布完成。</returns>
        private async Task PublishOnlineStatusAsync(MqttConfig config, DateTime now, CancellationToken ct)
        {
            if (_mqttClient == null || !_mqttClient.IsConnected || string.IsNullOrWhiteSpace(config.OnlineStatusTopic))
                return;

            // 节流：默认 30s，可配置 OnlineStatusReportInterval
            var interval = config.OnlineStatusReportInterval > 0 ? config.OnlineStatusReportInterval : 30000;
            if ((now - _lastOnlineStatusReport).TotalMilliseconds < interval)
                return;

            _lastOnlineStatusReport = now;

            // 构建当前在线快照
            var currentSnapshot = _cachedDevices.ToDictionary(
                d => d.Id,
                d => _deviceOnlineStatus.TryGetValue(d.Id, out var online) && online);

            // 与上一轮对比，找出状态变化的设备 ID
            var changedDeviceIds = _previousOnlineStatusSnapshot == null
                ? new HashSet<long>()
                : currentSnapshot
                    .Where(kv => !_previousOnlineStatusSnapshot.TryGetValue(kv.Key, out var previousOnline) || previousOnline != kv.Value)
                    .Select(kv => kv.Key)
                    .ToHashSet();

            // 首次上报或仅有变化时组装 JSON（按 SortOrder 排序）
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

        /// <summary>
        /// 确保 MQTT 客户端已创建、连接并订阅 SubTopic。
        /// </summary>
        /// <param name="config">MQTT 配置。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>连接/订阅完成（失败仅日志，不抛异常）。</returns>
        private async Task EnsureMqttConnectedAsync(MqttConfig config, CancellationToken ct)
        {
            if (!config.IsEnabled)
            {
                await DisconnectMqttAsync();
                return;
            }

            // 懒创建客户端并注册指令消息处理器
            if (_mqttClient == null)
            {
                _mqttClient = new MqttFactory().CreateMqttClient();
                _mqttClient.ApplicationMessageReceivedAsync += HandleMqttMessageAsync;
            }

            if (!_mqttClient.IsConnected)
            {
                try
                {
                    // ── MQTT 连接：TCP + ClientId + 可选凭据 ──
                    var optionsBuilder = new MqttClientOptionsBuilder()
                        .WithTcpServer(config.BrokerIp, config.Port)
                        .WithClientId(config.ClientId);

                    if (!string.IsNullOrEmpty(config.Username))
                    {
                        optionsBuilder.WithCredentials(config.Username, config.Password);
                    }

                    var options = optionsBuilder.Build();
                    await _mqttClient.ConnectAsync(options, ct);

                    // ── MQTT 订阅：指令下发主题 ──
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

        /// <summary>
        /// 断开并销毁 MQTT 客户端，注销消息处理器。
        /// </summary>
        /// <returns>断开完成。</returns>
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

        /// <summary>
        /// MQTT 消息接收入口：快速解码 payload 后派发到后台线程执行指令，不阻塞 MQTTnet 收包线程。
        /// </summary>
        /// <param name="e">MQTT 应用消息事件参数。</param>
        /// <returns>立即完成的 Task（实际工作在 Task.Run 中）。</returns>
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

            // 指令分发：独立 Task.Run，与收包线程解耦
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

        /// <summary>
        /// 解析并执行 MQTT 下行指令：write 写点、query/write 读点并回包；退避中快速失败。
        /// </summary>
        /// <param name="payload">JSON 指令字符串。</param>
        /// <returns>指令处理完成。</returns>
        private async Task ExecuteMqttCommandAsync(string payload)
        {
            try
            {
                if (_cachedMqttConfig is not { IsEnabled: true })
                    return;

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var command = JsonSerializer.Deserialize<MqttCommandDto>(payload, options);
                if (command == null || command.DeviceId <= 0)
                    return;

                var device = _cachedDevices.FirstOrDefault(d => d.Id == command.DeviceId);
                if (device == null || !device.IsActive)
                {
                    Console.WriteLine($"[MQTT] Device {command.DeviceId} not found or inactive.");
                    return;
                }

                var ioResult = await _ioScheduler.ExecuteMqttCommandAsync(device, command).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(ioResult.Error) && !ioResult.Connected)
                {
                    await PublishMqttErrorAsync(device.Id, ioResult.Error, ioResult.ReplyTo);
                    return;
                }

                if (!ioResult.Connected)
                {
                    await PublishMqttErrorAsync(device.Id, "设备当前不可达", ioResult.ReplyTo);
                    return;
                }

                if (ioResult.ReplyPayload is { Count: > 0 }
                    && _mqttClient != null
                    && _mqttClient.IsConnected
                    && _cachedMqttConfig != null
                    && !string.IsNullOrEmpty(_cachedMqttConfig.PubTopic))
                {
                    var json = JsonSerializer.Serialize(new
                    {
                        deviceId = device.Id,
                        timestamp = DateTime.Now,
                        data = ioResult.ReplyPayload,
                        replyTo = ioResult.ReplyTo,
                    }, MqttJsonOptions);

                    var message = new MqttApplicationMessageBuilder()
                        .WithTopic(_cachedMqttConfig.PubTopic)
                        .WithPayload(json)
                        .Build();

                    await _mqttClient.PublishAsync(message);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MQTT] Execute command error: {ex.Message}");
            }
        }

        /// <summary>
        /// 向 PubTopic 发布错误回包（success=false）。
        /// </summary>
        /// <param name="deviceId">目标设备 ID。</param>
        /// <param name="error">错误描述。</param>
        /// <param name="replyTo">回包类型标志，默认 2（读）。</param>
        /// <returns>发布完成。</returns>
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
    }
}
