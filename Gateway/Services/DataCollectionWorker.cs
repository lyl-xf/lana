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
    /// 轮询结果经 <see cref="PublishSnapshot"/> 写入 <see cref="IDeviceDataSnapshotStore"/>，
    /// 供定义页等 UI 绑定展示；MQTT 遥测发布与之解耦，可单独开关。
    /// </para>
    /// <para>
    /// 生命周期由 <c>MainViewModel</c> 在登录 Start、登出 Stop/Dispose。
    /// 扩展采集逻辑时优先改 FillDevicePayload / MQTT 订阅处理，注意失败退避。
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

        /// <summary>按协议类型创建设备协议会话的工厂。</summary>
        private readonly ProtocolSessionFactory _sessionFactory;

        /// <summary>共享实时状态；轮询完成后 UpdateDevice，UI 直接绑定 Groups。</summary>
        private readonly IDeviceDataSnapshotStore? _snapshotStore;

        /// <summary>MQTT 客户端实例；启用 MQTT 时创建并复用。</summary>
        private IMqttClient? _mqttClient;

        /// <summary>各设备上次完成轮询的 UTC 时间，用于 PollInterval 节流。</summary>
        private readonly Dictionary<long, DateTime> _lastPollTimes = new();

        /// <summary>各设备上次 MQTT 遥测发布的 UTC 时间，用于 TelemetryPublishInterval 节流。</summary>
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

        /// <summary>设备 ID → 连接配置哈希键，用于客户端池引用追踪。</summary>
        private readonly Dictionary<long, string> _deviceClientRefs = new();

        /// <summary>连接配置哈希键 → 共享协议会话（同 IP/串口/PLC 参数共用）。</summary>
        private readonly Dictionary<string, IDeviceProtocolSession> _sharedSessions = new();

        /// <summary>连接配置哈希键 → 引用计数，归零时释放会话。</summary>
        private readonly Dictionary<string, int> _sharedClientRefCounts = new();

        /// <summary>客户端池字典操作的互斥锁。</summary>
        private readonly object _clientPoolLock = new();

        /// <summary>设备连接失败退避状态（与采集锁分离，MQTT 可快速判断）。</summary>
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
        /// <param name="sessionFactory">协议会话工厂。</param>
        /// <param name="snapshotStore">可为 null；正常登录由 MainViewModel 注入单例 Store。</param>
        public DataCollectionWorker(
            IGatewayConfigStore configStore,
            ProtocolSessionFactory sessionFactory,
            IDeviceDataSnapshotStore? snapshotStore = null)
        {
            _configStore = configStore;
            _sessionFactory = sessionFactory;
            _snapshotStore = snapshotStore;
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

            // 释放客户端池中所有共享协议会话
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
        /// 根据设备连接参数生成客户端池哈希键（同键共享一条协议会话）。
        /// </summary>
        /// <param name="device">设备实体（含协议、IP、串口、PLC 等参数）。</param>
        /// <returns>唯一配置哈希字符串。</returns>
        private string GetDeviceConfigHash(Device device)
        {
            return $"{device.ProtocolType}_{device.Ip}_{device.Port}_{device.PortName}_{device.BaudRate}_{device.DataBits}_{device.StopBits}_{device.Parity}_{device.PlcVersion}_{device.PluginConfigJson}";
        }

        /// <summary>
        /// 为设备获取或创建共享协议会话（引用计数 +1）。
        /// </summary>
        /// <param name="device">目标设备。</param>
        /// <returns>可复用的 <see cref="IDeviceProtocolSession"/> 实例。</returns>
        private IDeviceProtocolSession AcquireSessionForDevice(Device device)
        {
            var configKey = GetDeviceConfigHash(device);

            lock (_clientPoolLock)
            {
                // 设备连接参数变更：释放旧键引用，后续按新键重新分配
                if (_deviceClientRefs.TryGetValue(device.Id, out var oldKey) && oldKey != configKey)
                {
                    ReleaseClientByKeyNoLock(oldKey);
                    _deviceClientRefs.Remove(device.Id);
                }

                if (!_deviceClientRefs.TryGetValue(device.Id, out var currentKey))
                {
                    // 池中尚无该配置键：工厂创建新会话
                    if (!_sharedSessions.ContainsKey(configKey))
                    {
                        _sharedSessions[configKey] = _sessionFactory.CreateSession(device);
                        _sharedClientRefCounts[configKey] = 0;
                    }

                    // 引用计数 +1，记录设备 → 配置键映射
                    _sharedClientRefCounts[configKey]++;
                    _deviceClientRefs[device.Id] = configKey;
                    currentKey = configKey;
                }

                return _sharedSessions[currentKey];
            }
        }

        /// <summary>
        /// 按设备 ID 释放客户端池引用（引用计数 -1，归零则关闭会话）。
        /// </summary>
        /// <param name="deviceId">设备 ID。</param>
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

        /// <summary>
        /// 按配置键释放共享会话（调用方须已持有 <see cref="_clientPoolLock"/>）。
        /// </summary>
        /// <param name="key">连接配置哈希键。</param>
        private void ReleaseClientByKeyNoLock(string key)
        {
            if (!_sharedClientRefCounts.TryGetValue(key, out var refCount))
                return;

            refCount--;
            // 仍有其它设备引用：仅更新计数
            if (refCount > 0)
            {
                _sharedClientRefCounts[key] = refCount;
                return;
            }

            // 引用归零：关闭并移除会话
            _sharedClientRefCounts.Remove(key);
            if (_sharedSessions.TryGetValue(key, out var session))
            {
                try { session.Close(); } catch { /* ignore */ }
                try { session.Dispose(); } catch { /* ignore */ }
                _sharedSessions.Remove(key);
            }
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
                        var toRemove = _deviceClientRefs.Keys.Where(id => !activeDeviceIds.Contains(id)).ToList();
                        foreach (var id in toRemove)
                        {
                            ReleaseClientByDeviceId(id);
                            _lastPollTimes.Remove(id);
                            _lastTelemetryPublishTimes.Remove(id);
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

                    // 轮询总开关：关闭时跳过设备调度与在线状态上报
                    var pollingEnabled = _cachedMqttConfig is { EnablePolling: true };

                    if (pollingEnabled)
                    {
                        // ── 轮询调度：遍历活跃设备，满足间隔且未退避/未在途则 fire-and-forget ──
                        foreach (var device in _cachedDevices)
                        {
                            // PollInterval <= 0：不参与自动轮询（仍可调试/MQTT 指令读写）
                            if (device.PollInterval <= 0)
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
                // 从客户端池获取/创建共享协议会话
                var session = AcquireSessionForDevice(device);
                Dictionary<string, object> payload;

                // 协议 IO 在同一会话锁内串行，避免并发读写
                lock (session)
                {
                    payload = PollDevicePayload(device, session, pollStartedUtc);
                }

                if (payload.Count > 0)
                {
                    // 写入内存快照 Store，供 UI 绑定（与 MQTT 无关）
                    PublishSnapshot(device, payload, pollStartedUtc);

                    // ── MQTT 遥测：已连接 + 达发布间隔 + 配置了 PubTopic ──
                    if (_cachedMqttConfig is { IsEnabled: true }
                        && _mqttClient != null
                        && _mqttClient.IsConnected
                        && ShouldPublishTelemetryForDevice(device.Id, pollStartedUtc, _cachedMqttConfig.TelemetryPublishInterval)
                        && !string.IsNullOrWhiteSpace(_cachedMqttConfig.PubTopic))
                    {
                        await PublishTelemetryAsync(device.Id, payload, ct).ConfigureAwait(false);
                        _lastTelemetryPublishTimes[device.Id] = DateTime.UtcNow;
                    }
                }

                // 无论是否有 payload，均更新完成时间（避免失败设备被高频重试）
                _lastPollTimes[device.Id] = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Device {device.Id}] Poll task error: {ex.Message}");
            }
            finally
            {
                // 释放在途标记，允许下一轮调度
                _pollingDevices.TryRemove(device.Id, out _);
            }
        }

        /// <summary>
        /// 同步读取单设备所有变量，填充 payload 字典；处理连接、退避与异常。
        /// </summary>
        /// <param name="device">目标设备。</param>
        /// <param name="session">已分配的协议会话。</param>
        /// <param name="now">当前 UTC 时间（退避登记用）。</param>
        /// <returns>Alias/Key → 值的字典；连接失败时可能为空。</returns>
        private Dictionary<string, object> PollDevicePayload(
            Device device,
            IDeviceProtocolSession session,
            DateTime now)
        {
            var payload = new Dictionary<string, object>();
            try
            {
                // ── 协议连接：未连接则 Open ──
                if (!session.IsConnected)
                {
                    var openResult = session.Open();
                    if (!openResult.Success)
                    {
                        _deviceOnlineStatus[device.Id] = false;
                        RegisterDeviceFailure(device.Id, now, openResult.Error ?? "连接失败");
                        return payload;
                    }
                }

                // ── 连接成功：清除退避、标记在线、按协议读点 ──
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
                // 异常时关闭会话，下次轮询重新 Open
                try { session.Close(); } catch { /* ignore */ }
                _deviceOnlineStatus[device.Id] = false;
                RegisterDeviceFailure(device.Id, now, ex.Message);
            }

            return payload;
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

            var entries = BuildSnapshotEntries(device, payload, nowUtc);
            if (entries.Count == 0)
                return;

            _snapshotStore.UpdateDevice(device.Id, device.Name, entries);
        }

        /// <summary>
        /// 判断是否到达该设备的 MQTT 遥测发布间隔。
        /// </summary>
        /// <param name="deviceId">设备 ID。</param>
        /// <param name="nowUtc">当前 UTC 时间。</param>
        /// <param name="intervalMs">配置的遥测发布间隔（毫秒）；≤0 表示每次轮询都发布。</param>
        /// <returns>应发布返回 true。</returns>
        private bool ShouldPublishTelemetryForDevice(long deviceId, DateTime nowUtc, int intervalMs)
        {
            if (intervalMs <= 0)
                return true;

            if (!_lastTelemetryPublishTimes.TryGetValue(deviceId, out var last))
                return true;

            return (nowUtc - last).TotalMilliseconds >= intervalMs;
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
            Dictionary<string, object> payload,
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
        /// 由 payload 构建快照 DTO：先按物模型 Alias 匹配，再追加 payload 中未映射的扩展键。
        /// Label 优先 Description，值经 <see cref="FormatSnapshotValue"/> 格式化。
        /// </summary>
        /// <param name="device">设备（含变量定义）。</param>
        /// <param name="payload">采集结果。</param>
        /// <param name="updatedAtUtc">快照更新时间。</param>
        /// <returns>快照条目列表。</returns>
        private static List<DeviceVariableSnapshotEntry> BuildSnapshotEntries(
            Device device,
            Dictionary<string, object> payload,
            DateTime updatedAtUtc)
        {
            var list = new List<DeviceVariableSnapshotEntry>();
            var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 第一遍：按物模型 Alias 匹配已知变量
            foreach (var variable in device.Variables)
            {
                if (string.IsNullOrWhiteSpace(variable.Alias))
                    continue;
                if (!payload.TryGetValue(variable.Alias, out var val) || val is null)
                    continue;

                usedKeys.Add(variable.Alias);
                list.Add(new DeviceVariableSnapshotEntry
                {
                    VariableId = variable.Id,
                    Label = ResolveSnapshotLabel(variable),
                    ValueText = FormatSnapshotValue(val),
                    UpdatedAtUtc = updatedAtUtc,
                });
            }

            // 第二遍：追加 payload 中未映射的扩展键（如 HttpClient 动态 KV）
            foreach (var kv in payload)
            {
                if (usedKeys.Contains(kv.Key) || kv.Value is null)
                    continue;

                list.Add(new DeviceVariableSnapshotEntry
                {
                    VariableId = 0,
                    Label = kv.Key,
                    ValueText = FormatSnapshotValue(kv.Value),
                    UpdatedAtUtc = updatedAtUtc,
                });
            }

            return list;
        }

        /// <summary>快照展示标签：Description → Alias → Address。</summary>
        /// <param name="variable">设备变量定义。</param>
        /// <returns>UI 展示用标签字符串。</returns>
        private static string ResolveSnapshotLabel(DeviceVariable variable)
        {
            if (!string.IsNullOrWhiteSpace(variable.Description))
                return variable.Description.Trim();
            if (!string.IsNullOrWhiteSpace(variable.Alias))
                return variable.Alias.Trim();
            return variable.Address;
        }

        /// <summary>将协议返回值格式化为 UI / 快照可用的字符串。</summary>
        /// <param name="value">原始协议值。</param>
        /// <returns>格式化后的字符串。</returns>
        private static string FormatSnapshotValue(object value)
            => value switch
            {
                bool b => b ? "true" : "false",
                string s => s,
                _ => value.ToString() ?? string.Empty,
            };

        /// <summary>
        /// 填充设备上报 data。HttpClient 按物模型 key/value JSON 路径展开；其它协议按 Alias 读点。
        /// </summary>
        /// <param name="device">目标设备。</param>
        /// <param name="session">已连接的协议会话。</param>
        /// <param name="payload">待填充的输出字典（Alias/Key → 值）。</param>
        private static void FillDevicePayload(Device device, IDeviceProtocolSession session, Dictionary<string, object> payload)
        {
            // ── HttpClient 协议：按 JSON 路径读取动态 KV 映射 ──
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

            // ── 其它协议：逐变量 Read，成功则写入 Alias ──
            foreach (var variable in device.Variables)
            {
                object? val = null;
                try
                {
                    var read = session.Read(variable.Address, (ProtocolDataType)(int)variable.DataType);
                    if (read.Success)
                        val = read.Value;
                }
                catch
                {
                    /* 单点失败跳过，避免大量 Console 输出拖慢轮询 */
                }

                if (val != null) payload[variable.Alias] = val;
            }
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
                // ── 前置校验：MQTT 必须启用 ──
                if (_cachedMqttConfig is not { IsEnabled: true })
                    return;

                // ── 指令反序列化 ──
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var command = JsonSerializer.Deserialize<MqttCommand>(payload, options);
                if (command == null || command.DeviceId <= 0)
                    return;

                // ── 目标设备查找与活跃性校验 ──
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

                // ── 退避快速路径：未连接且处于退避 → 立即错误回包，不阻塞 Open ──
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

                // ── 协议 IO 区：连接 → 写 → 读 → 组装回包（会话锁内串行）──
                lock (session)
                {
                    try
                    {
                        // 未连接则尝试 Open
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

                            // ── write 分支：按 Alias 写可写变量 ──
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

                            // ── query / write 分支：读点并组装回包 payload ──
                            if (command.Action?.ToLower() == "query" || command.Action?.ToLower() == "write")
                            {
                                var resultPayload = new Dictionary<string, object>();
                                var replyTo = ResolveReplyTo(command);

                                if (device.ProtocolType == ProtocolType.HttpClient)
                                {
                                    // HttpClient：复用 FillDevicePayload 读动态 KV
                                    FillDevicePayload(device, session, resultPayload);
                                }
                                else
                                {
                                    // 其它协议：Reads 指定 Alias 子集，否则读全部变量
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

                                // 有数据且配置了 PubTopic：构建 MQTT 回包消息（稍后锁外发布）
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

                // ── 回包/错误响应（锁外异步发布，避免持锁 await）──
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
        /// <param name="command">已解析的 MQTT 指令。</param>
        /// <returns>replyTo 标志值（1/2/3）。</returns>
        private static int ResolveReplyTo(MqttCommand command)
        {
            var action = command.Action?.Trim().ToLowerInvariant();
            if (action == "write")
                return command.Reads != null ? 3 : 1;
            // query 及其它默认按读
            return 2;
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

        /// <summary>
        /// MQTT 下行指令 JSON 反序列化 DTO（query 读 / write 写）。
        /// </summary>
        private class MqttCommand
        {
            /// <summary>可选消息 ID，用于客户端去重或追踪。</summary>
            public string? MessageId { get; set; }

            /// <summary>目标设备 ID。</summary>
            public long DeviceId { get; set; }

            /// <summary>动作类型：<c>query</c>（读）或 <c>write</c>（写，可附带 reads 做读写一体）。</summary>
            public string Action { get; set; } = string.Empty;

            /// <summary>写指令键值对（Alias → 值）；仅 action=write 时有效。</summary>
            public Dictionary<string, object>? Writes { get; set; }

            /// <summary>
            /// 读指令 Alias 列表；null=未传 reads（单写→replyTo=1）；非 null（含 []）=读写一体→replyTo=3。
            /// </summary>
            public List<string>? Reads { get; set; }
        }
    }
}
