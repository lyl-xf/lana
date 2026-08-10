using Lana.Data.Sqlite;
using Lana.Gateway.Data;
using Lana.Gateway.Models;

namespace Lana.Gateway.Services;

/// <summary>
/// 采集服务用的网关配置读取实现。
/// </summary>
public sealed class GatewayConfigStore : IGatewayConfigStore
{
    /// <summary>设备表 Mapper。</summary>
    private readonly DeviceMapper _devices;
    /// <summary>变量表 Mapper。</summary>
    private readonly DeviceVariableMapper _variables;
    /// <summary>MQTT 配置 Mapper。</summary>
    private readonly MqttConfigMapper _mqtt;

    /// <summary>
    /// 通过会话工厂构造（内部创建 Mapper）。
    /// </summary>
    /// <param name="sessionFactory">SQLite 会话工厂。</param>
    public GatewayConfigStore(ISqliteSessionFactory sessionFactory)
    {
        _devices = new DeviceMapper(sessionFactory);
        _variables = new DeviceVariableMapper(sessionFactory);
        _mqtt = new MqttConfigMapper(sessionFactory);
    }

    /// <summary>
    /// 注入已有 Mapper（便于测试）。
    /// </summary>
    /// <param name="devices">设备 Mapper。</param>
    /// <param name="variables">变量 Mapper。</param>
    /// <param name="mqtt">MQTT 配置 Mapper。</param>
    public GatewayConfigStore(
        DeviceMapper devices,
        DeviceVariableMapper variables,
        MqttConfigMapper mqtt)
    {
        _devices = devices;
        _variables = variables;
        _mqtt = mqtt;
    }

    /// <inheritdoc />
    public Task<MqttConfig?> GetMqttConfigAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return _mqtt.GetAsync();
    }

    /// <inheritdoc />
    public async Task<List<Device>> GetActiveDevicesWithVariablesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var devices = await _devices.GetActiveAsync();
        var list = new List<Device>(devices.Count);

        // 逐设备加载物模型变量（N+1 查询，设备数通常较少）
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
