using Lana.Gateway.Models;
using Lana.Gateway.Protocol;

namespace Lana.Gateway.Services;

/// <summary>
/// 统一设备 IO 仲裁：按物理链路串行，写优先，Poll 可按点打断。
/// </summary>
public interface IDeviceIoScheduler : IAsyncDisposable
{
    /// <summary>注入连接状态回调（Worker 登录后设置）。</summary>
    void SetConnectionHandlers(DeviceIoConnectionHandlers? handlers);

    /// <summary>周期 Poll：读 IncludeInPoll 点并返回 payload。</summary>
    Task<DevicePollResult> PollDeviceAsync(Device device, CancellationToken cancellationToken = default);

    /// <summary>读单点（调试 / 手动操作）。</summary>
    Task<ProtocolResult<object?>> ReadAsync(
        Device device,
        string address,
        DataType dataType,
        CancellationToken cancellationToken = default);

    /// <summary>写单点；成功且提供变量时 Patch 缓存与 UI。</summary>
    Task<ProtocolResult> WriteAsync(
        Device device,
        string address,
        DataType dataType,
        string? value,
        DeviceVariable? patchVariable = null,
        CancellationToken cancellationToken = default);

    /// <summary>执行 MQTT 下行写/读指令 IO。</summary>
    Task<MqttIoCommandResult> ExecuteMqttCommandAsync(
        Device device,
        MqttCommandDto command,
        CancellationToken cancellationToken = default);

    /// <summary>停止全部链路消费者并释放会话。</summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>设备停用/删除时释放链路引用。</summary>
    void ReleaseDevice(long deviceId);
}
