using Lana.Gateway.Models;
using Lana.Gateway.Protocol;

namespace Lana.Gateway.Services;

public sealed class ProtocolSessionFactory
{
    public IDeviceProtocolSession CreateSession(Device device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (device.ProtocolType == ProtocolType.HttpClient)
            return new HttpClientDeviceSession(device);

        return new BuiltInIoTClientSession(device);
    }
}
