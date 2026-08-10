using Lana.Gateway.Models;
using Lana.Gateway.Protocol;

namespace Lana.Gateway.Services;

/// <summary>
/// 按 <see cref="Device.ProtocolType"/> 创建协议会话。
/// HttpClient → <see cref="HttpClientDeviceSession"/>；其余走 IoTClient 封装
/// <see cref="BuiltInIoTClientSession"/>。
/// </summary>
public sealed class ProtocolSessionFactory
{
    /// <summary>为指定设备创建新会话（调用方负责 Open/Dispose）。</summary>
    public IDeviceProtocolSession CreateSession(Device device)
    {
        ArgumentNullException.ThrowIfNull(device);

        // 自定义协议：在此增加 case / if 分支即可
        if (device.ProtocolType == ProtocolType.HttpClient)
            return new HttpClientDeviceSession(device);

        return new BuiltInIoTClientSession(device);
    }
}
