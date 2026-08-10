using System.Text.Json;
using Lana.Data.Sqlite;
using Lana.Gateway.Data;
using Lana.Gateway.Models;
using Lana.Gateway.Protocol;

namespace Lana.Gateway.Services;

/// <summary>
/// 网关设备 / 变量 / MQTT / 调试 / 备份的高层 API（供 UI 与配置管理调用）。
/// <para>
/// UI 即时读写请优先走 <see cref="IDeviceDebugApi"/>（会记历史）；
/// 本类 Debug* 为底层实现，采集 Worker 使用协议会话而非本类。
/// </para>
/// </summary>
public sealed class GatewayDeviceService
{
    /// <summary>备份 JSON 序列化选项。</summary>
    private static readonly JsonSerializerOptions BackupJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>设备表 Mapper。</summary>
    private readonly DeviceMapper _devices;
    /// <summary>变量表 Mapper。</summary>
    private readonly DeviceVariableMapper _variables;
    /// <summary>MQTT 配置 Mapper。</summary>
    private readonly MqttConfigMapper _mqtt;
    /// <summary>协议会话工厂。</summary>
    private readonly ProtocolSessionFactory _protocolSessions;

    /// <summary>
    /// 通过会话工厂构造（内部创建默认 ProtocolSessionFactory）。
    /// </summary>
    /// <param name="sessionFactory">SQLite 会话工厂。</param>
    public GatewayDeviceService(ISqliteSessionFactory sessionFactory)
        : this(sessionFactory, new ProtocolSessionFactory())
    {
    }

    /// <summary>
    /// 注入自定义协议会话工厂（便于测试）。
    /// </summary>
    /// <param name="sessionFactory">SQLite 会话工厂。</param>
    /// <param name="protocolSessionFactory">协议会话工厂。</param>
    public GatewayDeviceService(ISqliteSessionFactory sessionFactory, ProtocolSessionFactory protocolSessionFactory)
    {
        _devices = new DeviceMapper(sessionFactory);
        _variables = new DeviceVariableMapper(sessionFactory);
        _mqtt = new MqttConfigMapper(sessionFactory);
        _protocolSessions = protocolSessionFactory;
    }

    /// <summary>
    /// 列出设备（可选名称过滤）。
    /// </summary>
    /// <param name="name">可选名称关键字。</param>
    /// <returns>设备列表。</returns>
    public async Task<IReadOnlyList<Device>> ListDevicesAsync(string? name = null)
        => await _devices.GetAllAsync(name);

    /// <summary>
    /// 获取单台设备及其变量。
    /// </summary>
    /// <param name="id">设备 Id。</param>
    /// <returns>带 Variables 的设备；不存在时返回 null。</returns>
    public async Task<Device?> GetDeviceAsync(long id)
    {
        var device = await _devices.GetByIdAsync(id);
        if (device is null)
            return null;

        device.Variables = (await _variables.GetByDeviceAsync(id)).ToList();
        return device;
    }

    /// <summary>
    /// 创建设备（Id 必须为正且唯一）。
    /// </summary>
    /// <param name="device">待创建设备。</param>
    /// <returns>异步任务。</returns>
    public async Task CreateDeviceAsync(Device device)
    {
        ArgumentNullException.ThrowIfNull(device);
        ValidateProtocol(device.ProtocolType);

        if (device.Id <= 0)
            throw new ArgumentException("设备 Id 必须为正整数。", nameof(device));

        if (await _devices.ExistsAsync(device.Id))
            throw new InvalidOperationException($"设备 Id {device.Id} 已存在。");

        NormalizeDevice(device);
        await _devices.InsertAsync(device);
    }

    /// <summary>
    /// 更新设备（支持变更主键 Id）。
    /// </summary>
    /// <param name="oldId">原设备 Id。</param>
    /// <param name="device">更新后的设备实体。</param>
    /// <returns>异步任务。</returns>
    public async Task UpdateDeviceAsync(long oldId, Device device)
    {
        ArgumentNullException.ThrowIfNull(device);
        ValidateProtocol(device.ProtocolType);

        if (device.Id <= 0)
            throw new ArgumentException("设备 Id 必须为正整数。", nameof(device));

        if (!await _devices.ExistsAsync(oldId))
            throw new InvalidOperationException($"设备 Id {oldId} 不存在。");

        NormalizeDevice(device);

        if (oldId == device.Id)
        {
            await _devices.UpdateAsync(device);
            return;
        }

        // 主键变更：事务内迁移
        await _devices.UpdateDeviceIdMigrateAsync(oldId, device);
    }

    /// <summary>
    /// 删除设备及其全部变量。
    /// </summary>
    /// <param name="id">设备 Id。</param>
    /// <returns>异步任务。</returns>
    public Task DeleteDeviceAsync(long id)
        => _devices.DeleteAsync(id);

    /// <summary>
    /// 列出设备的全部变量。
    /// </summary>
    /// <param name="deviceId">设备 Id。</param>
    /// <returns>变量列表。</returns>
    public Task<IReadOnlyList<DeviceVariable>> ListVariablesAsync(long deviceId)
        => _variables.GetByDeviceAsync(deviceId);

    /// <summary>
    /// 创建变量。
    /// </summary>
    /// <param name="variable">待创建变量。</param>
    /// <returns>新变量 Id。</returns>
    public async Task<long> CreateVariableAsync(DeviceVariable variable)
    {
        ArgumentNullException.ThrowIfNull(variable);

        if (!await _devices.ExistsAsync(variable.DeviceId))
            throw new InvalidOperationException($"设备 Id {variable.DeviceId} 不存在。");

        NormalizeVariable(variable);
        var id = await _variables.InsertAsync(variable);
        variable.Id = id;
        return id;
    }

    /// <summary>
    /// 更新变量。
    /// </summary>
    /// <param name="variable">待更新变量。</param>
    /// <returns>异步任务。</returns>
    public async Task UpdateVariableAsync(DeviceVariable variable)
    {
        ArgumentNullException.ThrowIfNull(variable);

        if (variable.Id <= 0)
            throw new ArgumentException("变量 Id 无效。", nameof(variable));

        if (!await _devices.ExistsAsync(variable.DeviceId))
            throw new InvalidOperationException($"设备 Id {variable.DeviceId} 不存在。");

        NormalizeVariable(variable);
        var affected = await _variables.UpdateAsync(variable);
        if (affected == 0)
            throw new InvalidOperationException($"变量 Id {variable.Id} 不存在。");
    }

    /// <summary>
    /// 删除变量。
    /// </summary>
    /// <param name="id">变量 Id。</param>
    /// <returns>异步任务。</returns>
    public Task DeleteVariableAsync(long id)
        => _variables.DeleteAsync(id);

    /// <summary>
    /// 读取 MQTT 配置。
    /// </summary>
    /// <returns>MQTT 配置；无记录时返回 null。</returns>
    public Task<MqttConfig?> GetMqttAsync()
        => _mqtt.GetAsync();

    /// <summary>
    /// 保存 MQTT 配置（Insert 或 Update）。
    /// </summary>
    /// <param name="config">MQTT 配置。</param>
    /// <returns>异步任务。</returns>
    public Task SaveMqttAsync(MqttConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        NormalizeMqtt(config);
        return _mqtt.UpsertAsync(config);
    }

    /// <summary>
    /// 调试读：打开临时会话读单点。
    /// </summary>
    /// <param name="deviceId">设备 Id。</param>
    /// <param name="address">协议地址。</param>
    /// <param name="dataType">数据类型。</param>
    /// <returns>读结果。</returns>
    public async Task<DebugReadResult> DebugReadAsync(long deviceId, string address, DataType dataType)
    {
        var device = await GetDeviceAsync(deviceId);
        if (device is null)
            return new DebugReadResult { Success = false, Error = $"设备 Id {deviceId} 不存在。" };

        if (string.IsNullOrWhiteSpace(address))
            return new DebugReadResult { Success = false, Error = "地址不能为空。" };

        using var session = _protocolSessions.CreateSession(device);
        var open = session.Open();
        if (!open.Success)
            return new DebugReadResult { Success = false, Error = open.Error };

        try
        {
            var result = session.Read(address, ToProtocolDataType(dataType));
            return new DebugReadResult
            {
                Success = result.Success,
                Error = result.Error,
                Value = result.Value,
            };
        }
        finally
        {
            try { session.Close(); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// 调试写：打开临时会话写单点。
    /// </summary>
    /// <param name="deviceId">设备 Id。</param>
    /// <param name="address">协议地址。</param>
    /// <param name="dataType">数据类型。</param>
    /// <param name="value">写入值。</param>
    /// <returns>写结果。</returns>
    public async Task<DebugWriteResult> DebugWriteAsync(long deviceId, string address, DataType dataType, string? value)
    {
        var device = await GetDeviceAsync(deviceId);
        if (device is null)
            return new DebugWriteResult { Success = false, Error = $"设备 Id {deviceId} 不存在。" };

        if (string.IsNullOrWhiteSpace(address))
            return new DebugWriteResult { Success = false, Error = "地址不能为空。" };

        using var session = _protocolSessions.CreateSession(device);
        var open = session.Open();
        if (!open.Success)
            return new DebugWriteResult { Success = false, Error = open.Error };

        try
        {
            var result = session.Write(address, ToProtocolDataType(dataType), value);
            return new DebugWriteResult
            {
                Success = result.Success,
                Error = result.Error,
            };
        }
        finally
        {
            try { session.Close(); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// 调试批量读：遍历全部非只写变量。
    /// </summary>
    /// <param name="deviceId">设备 Id。</param>
    /// <returns>批量读结果。</returns>
    public async Task<DebugReadAllResult> DebugReadAllAsync(long deviceId)
    {
        var device = await GetDeviceAsync(deviceId);
        if (device is null)
            return new DebugReadAllResult { Success = false, Error = $"设备 Id {deviceId} 不存在。" };

        using var session = _protocolSessions.CreateSession(device);
        var open = session.Open();
        if (!open.Success)
            return new DebugReadAllResult { Success = false, Error = open.Error };

        var result = new DebugReadAllResult { Success = true };
        try
        {
            foreach (var variable in device.Variables)
            {
                // 只写变量跳过
                if (variable.ReadWrite == ReadWriteAccess.WriteOnly)
                    continue;

                var item = new DebugReadAllItem
                {
                    VariableId = variable.Id,
                    Alias = variable.Alias,
                    Address = variable.Address,
                    DataType = variable.DataType,
                };

                try
                {
                    var read = session.Read(variable.Address, ToProtocolDataType(variable.DataType));
                    item.Success = read.Success;
                    item.Error = read.Error;
                    item.Value = read.Value;
                }
                catch (Exception ex)
                {
                    item.Success = false;
                    item.Error = ex.Message;
                }

                result.Items.Add(item);
            }
        }
        finally
        {
            try { session.Close(); } catch { /* ignore */ }
        }

        return result;
    }

    /// <summary>
    /// 导出网关配置为 JSON 备份字符串。
    /// </summary>
    /// <param name="includeMqtt">是否包含 MQTT 配置。</param>
    /// <returns>JSON 字符串。</returns>
    public async Task<string> ExportBackupAsync(bool includeMqtt = true)
    {
        var devices = await _devices.GetAllAsync();
        var backup = new GatewayBackupDto
        {
            SchemaVersion = 2,
            ExportedAtUtc = DateTime.UtcNow,
        };

        foreach (var device in devices)
        {
            var vars = await _variables.GetByDeviceAsync(device.Id);
            backup.Devices.Add(ToDeviceBackup(device, vars));
        }

        if (includeMqtt)
        {
            var mqtt = await _mqtt.GetAsync();
            if (mqtt is not null)
                backup.Mqtt = ToMqttBackup(mqtt);
        }

        return JsonSerializer.Serialize(backup, BackupJsonOptions);
    }

    /// <summary>
    /// 从 JSON 备份导入配置。
    /// </summary>
    /// <param name="json">备份 JSON。</param>
    /// <param name="mode">merge（合并）或 replaceAll（全量替换）。</param>
    /// <param name="includeMqtt">是否导入 MQTT 配置。</param>
    /// <returns>异步任务。</returns>
    public async Task ImportBackupAsync(string json, string mode = "merge", bool includeMqtt = true)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("备份 JSON 不能为空。", nameof(json));

        var normalizedMode = (mode ?? "merge").Trim();
        if (!normalizedMode.Equals("merge", StringComparison.OrdinalIgnoreCase)
            && !normalizedMode.Equals("replaceAll", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("mode 必须是 merge 或 replaceAll。", nameof(mode));
        }

        GatewayBackupDto? backup;
        try
        {
            backup = JsonSerializer.Deserialize<GatewayBackupDto>(json, BackupJsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("备份 JSON 解析失败：" + ex.Message, nameof(json), ex);
        }

        if (backup is null)
            throw new ArgumentException("备份 JSON 无效。", nameof(json));

        var replaceAll = normalizedMode.Equals("replaceAll", StringComparison.OrdinalIgnoreCase);

        if (replaceAll)
            await _devices.DeleteAllAsync();

        foreach (var dto in backup.Devices)
        {
            ValidateProtocol(dto.ProtocolType);
            var device = FromDeviceBackup(dto);
            NormalizeDevice(device);

            if (replaceAll)
            {
                await _devices.InsertAsync(device);
            }
            else if (await _devices.ExistsAsync(device.Id))
            {
                // merge：更新设备并重建变量
                await _devices.UpdateAsync(device);
                await _variables.DeleteByDeviceAsync(device.Id);
            }
            else
            {
                await _devices.InsertAsync(device);
            }

            foreach (var varDto in dto.Variables)
            {
                var variable = FromVariableBackup(varDto, device.Id);
                NormalizeVariable(variable);
                await _variables.InsertAsync(variable);
            }
        }

        if (includeMqtt && backup.Mqtt is not null)
        {
            var mqtt = FromMqttBackup(backup.Mqtt);
            NormalizeMqtt(mqtt);
            await _mqtt.UpsertAsync(mqtt);
        }
    }

    /// <summary>
    /// 校验协议类型是否在允许范围内（0–6）。
    /// </summary>
    /// <param name="protocolType">协议类型。</param>
    private static void ValidateProtocol(ProtocolType protocolType)
    {
        var value = (int)protocolType;
        // Custom 已移除；仅允许 0–6
        if (value < 0 || value > 6)
            throw new ArgumentException($"不支持的协议类型：{protocolType}（仅允许 0–6）。");
    }

    /// <summary>
    /// 规范化设备字段（空字符串、PollInterval、西门子版本）。
    /// </summary>
    /// <param name="device">待规范化设备。</param>
    private static void NormalizeDevice(Device device)
    {
        device.Name ??= string.Empty;
        device.Ip ??= string.Empty;
        device.PortName ??= string.Empty;
        device.PlcVersion ??= string.Empty;
        device.PluginConfigJson ??= string.Empty;
        if (device.PollInterval < 0)
            device.PollInterval = 0;

        if (device.ProtocolType == ProtocolType.SiemensClient)
            device.PlcVersion = IoTClientFactory.NormalizeSiemensVersion(device.PlcVersion);
    }

    /// <summary>
    /// 规范化变量字段（空字符串兜底）。
    /// </summary>
    /// <param name="variable">待规范化变量。</param>
    private static void NormalizeVariable(DeviceVariable variable)
    {
        variable.Address ??= string.Empty;
        variable.Alias ??= string.Empty;
        variable.Description ??= string.Empty;
        variable.HttpKeyJsonPath ??= string.Empty;
        variable.HttpValueJsonPath ??= string.Empty;
    }

    /// <summary>
    /// 规范化 MQTT 配置字段与默认值。
    /// </summary>
    /// <param name="config">待规范化配置。</param>
    private static void NormalizeMqtt(MqttConfig config)
    {
        config.BrokerIp ??= string.Empty;
        config.ClientId ??= string.Empty;
        config.Username ??= string.Empty;
        config.Password ??= string.Empty;
        config.PubTopic ??= string.Empty;
        config.SubTopic ??= string.Empty;
        config.OnlineStatusTopic ??= string.Empty;
        if (config.Port <= 0)
            config.Port = 1883;
        if (config.OnlineStatusReportInterval <= 0)
            config.OnlineStatusReportInterval = 30000;
        if (config.TelemetryPublishInterval < 0)
            config.TelemetryPublishInterval = 0;
    }

    /// <summary>
    /// Models.DataType → Protocol.ProtocolDataType（整型 cast）。
    /// </summary>
    /// <param name="dataType">领域数据类型。</param>
    /// <returns>协议数据类型。</returns>
    private static ProtocolDataType ToProtocolDataType(DataType dataType)
        => (ProtocolDataType)(int)dataType;

    /// <summary>
    /// 设备实体 → 备份 DTO。
    /// </summary>
    /// <param name="device">设备实体。</param>
    /// <param name="variables">变量列表。</param>
    /// <returns>DeviceBackupDto。</returns>
    private static DeviceBackupDto ToDeviceBackup(Device device, IReadOnlyList<DeviceVariable> variables) => new()
    {
        Id = device.Id,
        Name = device.Name,
        Ip = device.Ip,
        Port = device.Port,
        ProtocolType = device.ProtocolType,
        PortName = device.PortName,
        BaudRate = device.BaudRate,
        DataBits = device.DataBits,
        StopBits = device.StopBits,
        Parity = device.Parity,
        PlcVersion = device.PlcVersion,
        PluginConfigJson = device.PluginConfigJson,
        SortOrder = device.SortOrder,
        PollInterval = device.PollInterval,
        IsActive = device.IsActive,
        Variables = variables.Select(v => new DeviceVariableBackupDto
        {
            Id = v.Id,
            DeviceId = v.DeviceId,
            Address = v.Address,
            DataType = v.DataType,
            Alias = v.Alias,
            Description = v.Description,
            ReadWrite = v.ReadWrite,
            HttpKeyJsonPath = v.HttpKeyJsonPath,
            HttpValueJsonPath = v.HttpValueJsonPath,
            ShowOnDefinedPage = v.ShowOnDefinedPage,
            DefinedPageDisplayName = v.DefinedPageDisplayName,
            DefinedPageOperation = v.DefinedPageOperation,
            DefinedPageWriteValue = v.DefinedPageWriteValue,
        }).ToList(),
    };

    /// <summary>
    /// 备份 DTO → 设备实体。
    /// </summary>
    /// <param name="dto">DeviceBackupDto。</param>
    /// <returns>Device 实体。</returns>
    private static Device FromDeviceBackup(DeviceBackupDto dto) => new()
    {
        Id = dto.Id,
        Name = dto.Name,
        Ip = dto.Ip,
        Port = dto.Port,
        ProtocolType = dto.ProtocolType,
        PortName = dto.PortName,
        BaudRate = dto.BaudRate,
        DataBits = dto.DataBits,
        StopBits = dto.StopBits,
        Parity = dto.Parity,
        PlcVersion = dto.PlcVersion,
        PluginConfigJson = dto.PluginConfigJson,
        SortOrder = dto.SortOrder,
        PollInterval = dto.PollInterval,
        IsActive = dto.IsActive,
    };

    /// <summary>
    /// 变量备份 DTO → 变量实体。
    /// </summary>
    /// <param name="dto">DeviceVariableBackupDto。</param>
    /// <param name="deviceId">目标设备 Id。</param>
    /// <returns>DeviceVariable 实体。</returns>
    private static DeviceVariable FromVariableBackup(DeviceVariableBackupDto dto, long deviceId) => new()
    {
        DeviceId = deviceId,
        Address = dto.Address,
        DataType = dto.DataType,
        Alias = dto.Alias,
        Description = dto.Description,
        ReadWrite = dto.ReadWrite,
        HttpKeyJsonPath = dto.HttpKeyJsonPath,
        HttpValueJsonPath = dto.HttpValueJsonPath,
        ShowOnDefinedPage = dto.ShowOnDefinedPage,
        DefinedPageDisplayName = dto.DefinedPageDisplayName ?? string.Empty,
        DefinedPageOperation = dto.DefinedPageOperation,
        DefinedPageWriteValue = dto.DefinedPageWriteValue ?? string.Empty,
    };

    /// <summary>
    /// MQTT 实体 → 备份 DTO。
    /// </summary>
    /// <param name="mqtt">MqttConfig 实体。</param>
    /// <returns>MqttBackupDto。</returns>
    private static MqttBackupDto ToMqttBackup(MqttConfig mqtt) => new()
    {
        IsEnabled = mqtt.IsEnabled,
        EnablePolling = mqtt.EnablePolling,
        BrokerIp = mqtt.BrokerIp,
        Port = mqtt.Port,
        ClientId = mqtt.ClientId,
        Username = mqtt.Username,
        Password = mqtt.Password,
        PubTopic = mqtt.PubTopic,
        SubTopic = mqtt.SubTopic,
        OnlineStatusTopic = mqtt.OnlineStatusTopic,
        OnlineStatusReportInterval = mqtt.OnlineStatusReportInterval,
        TelemetryPublishInterval = mqtt.TelemetryPublishInterval,
    };

    /// <summary>
    /// 备份 DTO → MQTT 实体。
    /// </summary>
    /// <param name="dto">MqttBackupDto。</param>
    /// <returns>MqttConfig 实体。</returns>
    private static MqttConfig FromMqttBackup(MqttBackupDto dto) => new()
    {
        IsEnabled = dto.IsEnabled,
        EnablePolling = dto.EnablePolling,
        BrokerIp = dto.BrokerIp,
        Port = dto.Port,
        ClientId = dto.ClientId,
        Username = dto.Username,
        Password = dto.Password,
        PubTopic = dto.PubTopic,
        SubTopic = dto.SubTopic,
        OnlineStatusTopic = dto.OnlineStatusTopic,
        OnlineStatusReportInterval = dto.OnlineStatusReportInterval,
        TelemetryPublishInterval = dto.TelemetryPublishInterval,
    };
}
