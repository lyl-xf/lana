using Lana.Gateway.Models;

namespace Lana.Gateway.Services;

public interface IGatewayConfigStore
{
    Task<MqttConfig?> GetMqttConfigAsync(CancellationToken ct = default);
    Task<List<Device>> GetActiveDevicesWithVariablesAsync(CancellationToken ct = default);
}
