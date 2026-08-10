namespace Lana.Gateway.Models;

/// <summary>网关配置备份根 DTO（导出/导入 JSON 的顶层结构）。</summary>
public sealed class GatewayBackupDto
{
    /// <summary>备份格式版本号（当前为 2）。</summary>
    public int SchemaVersion { get; set; } = 2;
    /// <summary>导出时间（UTC）。</summary>
    public DateTime ExportedAtUtc { get; set; }
    /// <summary>设备及其变量列表。</summary>
    public List<DeviceBackupDto> Devices { get; set; } = [];
    /// <summary>MQTT 配置（可选，导出时可排除）。</summary>
    public MqttBackupDto? Mqtt { get; set; }
}

/// <summary>设备备份 DTO（不含运行时 Variables 对象，变量在 Variables 列表中）。</summary>
public sealed class DeviceBackupDto
{
    /// <summary>设备 Id。</summary>
    public long Id { get; set; }
    /// <summary>设备名称。</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>IP 地址。</summary>
    public string Ip { get; set; } = string.Empty;
    /// <summary>端口号。</summary>
    public int Port { get; set; }
    /// <summary>协议类型。</summary>
    public ProtocolType ProtocolType { get; set; }
    /// <summary>串口名。</summary>
    public string PortName { get; set; } = string.Empty;
    /// <summary>波特率。</summary>
    public int BaudRate { get; set; }
    /// <summary>数据位。</summary>
    public int DataBits { get; set; }
    /// <summary>停止位。</summary>
    public int StopBits { get; set; }
    /// <summary>校验位。</summary>
    public int Parity { get; set; }
    /// <summary>PLC 版本。</summary>
    public string PlcVersion { get; set; } = string.Empty;
    /// <summary>插件/HttpClient JSON 配置。</summary>
    public string PluginConfigJson { get; set; } = string.Empty;
    /// <summary>排序权重。</summary>
    public int SortOrder { get; set; }
    /// <summary>采集周期（毫秒）。</summary>
    public int PollInterval { get; set; }
    /// <summary>是否启用。</summary>
    public bool IsActive { get; set; }
    /// <summary>该设备的变量列表。</summary>
    public List<DeviceVariableBackupDto> Variables { get; set; } = [];
}

/// <summary>物模型变量备份 DTO。</summary>
public sealed class DeviceVariableBackupDto
{
    /// <summary>变量 Id（导入 merge 模式下可能被忽略并重新自增）。</summary>
    public long Id { get; set; }
    /// <summary>所属设备 Id。</summary>
    public long DeviceId { get; set; }
    /// <summary>协议地址。</summary>
    public string Address { get; set; } = string.Empty;
    /// <summary>数据类型。</summary>
    public DataType DataType { get; set; }
    /// <summary>英文别名。</summary>
    public string Alias { get; set; } = string.Empty;
    /// <summary>中文描述。</summary>
    public string Description { get; set; } = string.Empty;
    /// <summary>读写权限。</summary>
    public ReadWriteAccess ReadWrite { get; set; }
    /// <summary>HttpClient key JSON 路径。</summary>
    public string HttpKeyJsonPath { get; set; } = string.Empty;
    /// <summary>HttpClient value JSON 路径。</summary>
    public string HttpValueJsonPath { get; set; } = string.Empty;
    /// <summary>是否在定义页显示。</summary>
    public bool ShowOnDefinedPage { get; set; }
    /// <summary>定义页按钮显示名。</summary>
    public string DefinedPageDisplayName { get; set; } = string.Empty;
    /// <summary>定义页按钮操作类型。</summary>
    public DefinedPageOperation DefinedPageOperation { get; set; } = DefinedPageOperation.Read;
    /// <summary>定义页写入默认值。</summary>
    public string DefinedPageWriteValue { get; set; } = string.Empty;
}

/// <summary>MQTT 配置备份 DTO。</summary>
public sealed class MqttBackupDto
{
    /// <summary>是否启用 MQTT。</summary>
    public bool IsEnabled { get; set; } = true;
    /// <summary>是否启用轮询采集。</summary>
    public bool EnablePolling { get; set; } = true;
    /// <summary>Broker IP。</summary>
    public string BrokerIp { get; set; } = string.Empty;
    /// <summary>Broker 端口。</summary>
    public int Port { get; set; }
    /// <summary>客户端 Id。</summary>
    public string ClientId { get; set; } = string.Empty;
    /// <summary>用户名。</summary>
    public string Username { get; set; } = string.Empty;
    /// <summary>密码。</summary>
    public string Password { get; set; } = string.Empty;
    /// <summary>发布主题。</summary>
    public string PubTopic { get; set; } = string.Empty;
    /// <summary>订阅主题。</summary>
    public string SubTopic { get; set; } = string.Empty;
    /// <summary>在线状态主题。</summary>
    public string OnlineStatusTopic { get; set; } = string.Empty;
    /// <summary>在线状态上报间隔（毫秒）。</summary>
    public int OnlineStatusReportInterval { get; set; } = 30000;
    /// <summary>遥测限频间隔（毫秒）。</summary>
    public int TelemetryPublishInterval { get; set; }
}
