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
    /// <param name="device">设备实体（含协议与连接参数）。</param>
    /// <returns>未打开的协议会话实例。</returns>
    public IDeviceProtocolSession CreateSession(Device device)
    {
        ArgumentNullException.ThrowIfNull(device);

        // HttpClient 走独立 REST 会话；其余协议统一 IoTClient 适配
        if (device.ProtocolType == ProtocolType.HttpClient)
            return new HttpClientDeviceSession(device);

        return new BuiltInIoTClientSession(device);
    }
}
