using Lana.Gateway.Models;

namespace Lana.Gateway.Services;

/// <summary>
/// 采集 Worker 使用的只读配置源（活跃设备+变量、MQTT）。
/// 与 UI 侧 <see cref="GatewayDeviceService"/> 分离，避免 Worker 依赖完整 CRUD。
/// </summary>
public interface IGatewayConfigStore
{
    Task<MqttConfig?> GetMqttConfigAsync(CancellationToken ct = default);

    /// <summary>返回 IsActive 设备及其变量（含 PollInterval=0 的设备，由 Worker 自行跳过轮询）。</summary>
    Task<List<Device>> GetActiveDevicesWithVariablesAsync(CancellationToken ct = default);
}
