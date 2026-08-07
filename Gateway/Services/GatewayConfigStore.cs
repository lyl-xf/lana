using Lana.Gateway.Data;
using Lana.Gateway.Models;
using Lana.Data.Sqlite;

namespace Lana.Gateway.Services;

/// <summary>
/// 采集服务用的网关配置读取实现。
/// </summary>
public sealed class GatewayConfigStore : IGatewayConfigStore
{
    private readonly DeviceMapper _devices;
    private readonly DeviceVariableMapper _variables;
    private readonly MqttConfigMapper _mqtt;

    public GatewayConfigStore(ISqliteSessionFactory sessionFactory)
    {
        _devices = new DeviceMapper(sessionFactory);
        _variables = new DeviceVariableMapper(sessionFactory);
        _mqtt = new MqttConfigMapper(sessionFactory);
    }

    public GatewayConfigStore(
        DeviceMapper devices,
        DeviceVariableMapper variables,
        MqttConfigMapper mqtt)
    {
        _devices = devices;
        _variables = variables;
        _mqtt = mqtt;
    }

    public Task<MqttConfig?> GetMqttConfigAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return _mqtt.GetAsync();
    }

    public async Task<List<Device>> GetActiveDevicesWithVariablesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var devices = await _devices.GetActiveAsync();
        var list = new List<Device>(devices.Count);

        foreach (var device in devices)
        {
            ct.ThrowIfCancellationRequested();
            var vars = await _variables.GetByDeviceAsync(device.Id);
            device.Variables = vars.ToList();
            list.Add(device);
        }

        return list;
    }
}
