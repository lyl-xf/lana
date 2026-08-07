namespace Lana.Gateway.Models;

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

public enum DataType
{
    Bool, Int16, Int32, Float, Double, String, Coil, Discrete, Short, UShort, Long, ULong
}

public enum ReadWriteAccess
{
    ReadOnly, WriteOnly, ReadWrite
}

/// <summary>自定义页按钮操作类型。</summary>
public enum DefinedPageOperation
{
    Read = 0,
    Write = 1,
}

public sealed class Device
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public int Port { get; set; }
    public ProtocolType ProtocolType { get; set; }
    public string PortName { get; set; } = string.Empty;
    public int BaudRate { get; set; } = 9600;
    public int DataBits { get; set; } = 8;
    public int StopBits { get; set; } = 1;
    public int Parity { get; set; }
    public string PlcVersion { get; set; } = string.Empty;
    /// <summary>HttpClient 等协议的 JSON 配置（原 PluginConfigJson）。</summary>
    public string PluginConfigJson { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    /// <summary>采集周期（毫秒）。0 表示关闭数据采集，不参与轮询。</summary>
    public int PollInterval { get; set; } = 1000;
    public bool IsActive { get; set; } = true;
    public List<DeviceVariable> Variables { get; set; } = [];
}

public sealed class DeviceVariable
{
    public long Id { get; set; }
    public long DeviceId { get; set; }
    public string Address { get; set; } = string.Empty;
    public DataType DataType { get; set; }
    public string Alias { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ReadWriteAccess ReadWrite { get; set; } = ReadWriteAccess.ReadWrite;
    public string HttpKeyJsonPath { get; set; } = string.Empty;
    public string HttpValueJsonPath { get; set; } = string.Empty;
    /// <summary>开启后自动出现在「定义页面」自定义按钮区。</summary>
    public bool ShowOnDefinedPage { get; set; }
    /// <summary>自定义页按钮是读取还是写入。</summary>
    public DefinedPageOperation DefinedPageOperation { get; set; } = DefinedPageOperation.Read;
    /// <summary>自定义页写入时使用的默认值（仅写入模式有效）。</summary>
    public string DefinedPageWriteValue { get; set; } = string.Empty;
}

public sealed class MqttConfig
{
    public long Id { get; set; }
    /// <summary>关闭后 MQTT 客户端不参与采集上报与指令订阅。</summary>
    public bool IsEnabled { get; set; } = true;
    public string BrokerIp { get; set; } = string.Empty;
    public int Port { get; set; } = 1883;
    public string ClientId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string PubTopic { get; set; } = string.Empty;
    public string SubTopic { get; set; } = string.Empty;
    public string OnlineStatusTopic { get; set; } = string.Empty;
    public int OnlineStatusReportInterval { get; set; } = 30000;
}
