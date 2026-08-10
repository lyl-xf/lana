using Lana.Gateway.Models;
using Lana.Gateway.Protocol;

namespace Lana.Gateway.Services;

/// <summary>
/// 将 IoTClient（Modbus / 西门子 / 三菱 / 欧姆龙）适配为 <see cref="Protocol.IDeviceProtocolSession"/>。
/// 具体客户端由 <see cref="IoTClientFactory"/> 创建，读写委托 <see cref="ProtocolIoTClientOperations"/>。
/// </summary>
public sealed class BuiltInIoTClientSession : IDeviceProtocolSession
{
    /// <summary>设备配置（含协议类型）。</summary>
    private readonly Device _device;
    /// <summary>IoTClient 动态客户端实例。</summary>
    private dynamic? _client;
    /// <summary>是否已 Dispose。</summary>
    private bool _disposed;

    /// <summary>
    /// 构造会话（创建客户端但不 Open）。
    /// </summary>
    /// <param name="device">设备实体。</param>
    public BuiltInIoTClientSession(Device device)
    {
        _device = device;
        _client = IoTClientFactory.CreateClient(device);
    }

    /// <inheritdoc />
    public bool IsConnected => _client is not null && (bool)_client.Connected;

    /// <inheritdoc />
    public ProtocolResult Open()
    {
        if (_client == null) return ProtocolResult.Fail("内部错误：客户端未初始化");
        var r = _client.Open();
        return r.IsSucceed ? ProtocolResult.Ok() : ProtocolResult.Fail(r.Err?.ToString() ?? "连接失败");
    }

    /// <inheritdoc />
    public void Close()
    {
        try { _client?.Close(); } catch { /* ignore */ }
    }

    /// <inheritdoc />
    public ProtocolResult<object?> Read(string address, ProtocolDataType dataType) =>
        ProtocolIoTClientOperations.Read(_client!, _device.ProtocolType, address, (DataType)(int)dataType);

    /// <inheritdoc />
    public ProtocolResult Write(string address, ProtocolDataType dataType, string? value) =>
        ProtocolIoTClientOperations.Write(_client!, _device.ProtocolType, address, (DataType)(int)dataType, value);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
        if (_client is IDisposable d)
        {
            try { d.Dispose(); } catch { /* ignore */ }
        }

        _client = null;
    }
}
