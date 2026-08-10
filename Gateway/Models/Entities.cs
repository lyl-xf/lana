namespace Lana.Gateway.Models;

// 网关领域模型。持久化列变更需同步：属性 → GatewaySchema 迁移 → Mapper SQL → UI / 备份 DTO。

/// <summary>
/// 设备协议类型（整型入库）。新增协议请保持连续编号，并同步
/// ProtocolSessionFactory / ProtocolDisplay / DevicesViewModel 校验上限。
/// </summary>
public enum ProtocolType
{
    ModbusTcp = 0,
    ModbusRtu = 1,
    ModbusAscii = 2,
    SiemensClient = 3,
    MitsubishiClient = 4,
    OmronFinsClient = 5,
    HttpClient = 6,
}

/// <summary>物模型数据类型（与协议层 ProtocolDataType 对齐）。</summary>
public enum DataType
{
    Bool, Int16, Int32, Float, Double, String, Coil, Discrete, Short, UShort, Long, ULong
}

/// <summary>变量读写权限（采集与 MQTT 写指令会参考）。</summary>
public enum ReadWriteAccess
{
    ReadOnly, WriteOnly, ReadWrite
}

/// <summary>手动操作页按钮操作类型（非 Bool 时有效；Bool 固定点动 true/false）。</summary>
public enum DefinedPageOperation
{
    Read = 0,
    Write = 1,
}

/// <summary>网关设备（连接参数 + 采集开关）。</summary>
public sealed class Device
{
    /// <summary>设备主键（业务 Id，非自增）。</summary>
    public long Id { get; set; }
    /// <summary>设备显示名称。</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>TCP/IP 或 PLC 连接 IP 地址。</summary>
    public string Ip { get; set; } = string.Empty;
    /// <summary>TCP 端口号。</summary>
    public int Port { get; set; }
    /// <summary>通信协议类型。</summary>
    public ProtocolType ProtocolType { get; set; }
    /// <summary>串口名称（Modbus RTU/ASCII 使用）。</summary>
    public string PortName { get; set; } = string.Empty;
    /// <summary>串口波特率。</summary>
    public int BaudRate { get; set; } = 9600;
    /// <summary>串口数据位。</summary>
    public int DataBits { get; set; } = 8;
    /// <summary>串口停止位。</summary>
    public int StopBits { get; set; } = 1;
    /// <summary>串口校验位（System.IO.Ports.Parity 整型值）。</summary>
    public int Parity { get; set; }
    /// <summary>PLC 型号版本（西门子/三菱）。</summary>
    public string PlcVersion { get; set; } = string.Empty;
    /// <summary>HttpClient 等协议的 JSON 配置（原 PluginConfigJson）。</summary>
    public string PluginConfigJson { get; set; } = string.Empty;
    /// <summary>列表排序权重（越小越靠前）。</summary>
    public int SortOrder { get; set; }
    /// <summary>采集周期（毫秒）。0 表示关闭数据采集，不参与轮询。</summary>
    public int PollInterval { get; set; } = 1000;
    /// <summary>是否启用（false 时 Worker 不轮询，MQTT 指令也会拒绝）。</summary>
    public bool IsActive { get; set; } = true;
    /// <summary>该设备下的物模型变量列表（运行时由 Mapper 填充）。</summary>
    public List<DeviceVariable> Variables { get; set; } = [];
}

/// <summary>
/// 物模型变量。Alias 用于 MQTT 上报/订阅字段名；
/// ShowOnDefinedPage* 仅影响「手动操作」页按钮，不影响采集与上报。
/// </summary>
public sealed class DeviceVariable
{
    /// <summary>变量主键（自增）。</summary>
    public long Id { get; set; }
    /// <summary>所属设备 Id。</summary>
    public long DeviceId { get; set; }
    /// <summary>协议读点地址（Modbus/PLC 地址或 HttpClient JSON 路径）。</summary>
    public string Address { get; set; } = string.Empty;
    /// <summary>数据类型。</summary>
    public DataType DataType { get; set; }
    /// <summary>英文别名；MQTT JSON 字段名、调试显示常用此字段。</summary>
    public string Alias { get; set; } = string.Empty;
    /// <summary>中文描述（UI 展示、快照 Label 优先使用）。</summary>
    public string Description { get; set; } = string.Empty;
    /// <summary>读写权限。</summary>
    public ReadWriteAccess ReadWrite { get; set; } = ReadWriteAccess.ReadWrite;
    /// <summary>HttpClient：从响应中取 key 的 JSON Path。</summary>
    public string HttpKeyJsonPath { get; set; } = string.Empty;
    /// <summary>HttpClient：从响应中取 value 的 JSON Path。</summary>
    public string HttpValueJsonPath { get; set; } = string.Empty;
    /// <summary>开启后自动出现在「手动操作」页按钮区。</summary>
    public bool ShowOnDefinedPage { get; set; }
    /// <summary>手动操作页按钮显示名称（中文等，区别于英文别名）。</summary>
    public string DefinedPageDisplayName { get; set; } = string.Empty;
    /// <summary>手动操作按钮是读取还是写入（bool 类型固定为按下 true / 松开 false）。</summary>
    public DefinedPageOperation DefinedPageOperation { get; set; } = DefinedPageOperation.Read;
    /// <summary>手动操作写入时使用的默认值（仅非 bool 写入模式有效）。</summary>
    public string DefinedPageWriteValue { get; set; } = string.Empty;
}

/// <summary>全局 MQTT 连接与主题配置（通常单行）。</summary>
public sealed class MqttConfig
{
    /// <summary>配置行主键。</summary>
    public long Id { get; set; }
    /// <summary>开启后连接 Broker 并处理订阅指令；与轮询、周期上报可独立组合。</summary>
    public bool IsEnabled { get; set; } = true;
    /// <summary>开启后 Worker 按各设备 PollInterval 轮询读点并更新快照。</summary>
    public bool EnablePolling { get; set; } = true;
    /// <summary>MQTT Broker IP 或域名。</summary>
    public string BrokerIp { get; set; } = string.Empty;
    /// <summary>MQTT Broker 端口。</summary>
    public int Port { get; set; } = 1883;
    /// <summary>MQTT 客户端 Id。</summary>
    public string ClientId { get; set; } = string.Empty;
    /// <summary>MQTT 用户名（可为空）。</summary>
    public string Username { get; set; } = string.Empty;
    /// <summary>MQTT 密码（可为空）。</summary>
    public string Password { get; set; } = string.Empty;
    /// <summary>遥测/指令响应发布主题。</summary>
    public string PubTopic { get; set; } = string.Empty;
    /// <summary>远程写/读指令订阅主题。</summary>
    public string SubTopic { get; set; } = string.Empty;
    /// <summary>设备在线状态变更上报主题。</summary>
    public string OnlineStatusTopic { get; set; } = string.Empty;
    /// <summary>在线状态主题上报间隔（毫秒）。</summary>
    public int OnlineStatusReportInterval { get; set; } = 30000;
    /// <summary>
    /// 周期遥测 PubTopic 最小间隔（毫秒，按设备）。
    /// 0 = 每次轮询成功即上报；&gt;0 = 限频上报（轮询仍按 PollInterval 执行）。
    /// </summary>
    public int TelemetryPublishInterval { get; set; }
}
