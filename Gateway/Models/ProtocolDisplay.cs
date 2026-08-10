namespace Lana.Gateway.Models;

/// <summary>
/// 协议 / 数据类型 / 读写权限的展示名称与分类辅助（设备管理 UI）。
/// 新增 ProtocolType 时同步扩展 ProtocolNames 与 IsSerial/IsTcp/IsPlc 等判断。
/// </summary>
public static class ProtocolDisplay
{
    /// <summary>与 ProtocolType 枚举顺序一一对应的协议显示名称。</summary>
    public static readonly string[] ProtocolNames =
    [
        "ModbusTcp",
        "ModbusRtu",
        "ModbusAscii",
        "SiemensClient",
        "MitsubishiClient",
        "OmronFinsClient",
        "HttpClient",
    ];

    /// <summary>西门子 PLC 版本选项（与 IoTClient SiemensVersion 枚举对应）。</summary>
    public static readonly string[] SiemensVersions =
    [
        "S7_200",
        "S7_200Smart",
        "S7_300",
        "S7_400",
        "S7_1200",
        "S7_1500",
    ];

    /// <summary>三菱 PLC 版本选项（与 IoTClient MitsubishiVersion 枚举对应）。</summary>
    public static readonly string[] MitsubishiVersions =
    [
        "A_1E",
        "Qna_3E",
    ];

    /// <summary>物模型数据类型显示名称（与 DataType 枚举顺序一致）。</summary>
    public static readonly string[] DataTypeNames =
    [
        "Bool", "Int16", "Int32", "Float", "Double", "String",
        "Coil", "Discrete", "Short", "UShort", "Long", "ULong",
    ];

    /// <summary>读写权限中文显示名称（与 ReadWriteAccess 枚举顺序一致）。</summary>
    public static readonly string[] ReadWriteNames =
    [
        "只读",
        "只写",
        "读写",
    ];

    /// <summary>
    /// 获取协议类型的 UI 显示名称。
    /// </summary>
    /// <param name="protocol">协议枚举值。</param>
    /// <returns>对应的中文/英文协议名；超出范围时返回枚举 ToString。</returns>
    public static string GetProtocolName(ProtocolType protocol)
    {
        var index = (int)protocol;
        // 索引越界时回退到枚举名，避免新增协议未同步数组时崩溃
        if (index >= 0 && index < ProtocolNames.Length)
            return ProtocolNames[index];
        return protocol.ToString();
    }

    /// <summary>
    /// 判断协议是否使用串口通信（Modbus RTU / ASCII）。
    /// </summary>
    /// <param name="protocol">协议类型。</param>
    /// <returns>串口协议返回 true。</returns>
    public static bool IsSerial(ProtocolType protocol)
        => protocol is ProtocolType.ModbusRtu or ProtocolType.ModbusAscii;

    /// <summary>
    /// 判断协议是否使用 TCP/IP 通信。
    /// </summary>
    /// <param name="protocol">协议类型。</param>
    /// <returns>TCP 协议返回 true。</returns>
    public static bool IsTcp(ProtocolType protocol)
        => protocol is ProtocolType.ModbusTcp
            or ProtocolType.SiemensClient
            or ProtocolType.MitsubishiClient
            or ProtocolType.OmronFinsClient;

    /// <summary>
    /// 判断协议是否为 PLC 类（西门子 / 三菱 / 欧姆龙）。
    /// </summary>
    /// <param name="protocol">协议类型。</param>
    /// <returns>PLC 协议返回 true。</returns>
    public static bool IsPlc(ProtocolType protocol)
        => protocol is ProtocolType.SiemensClient
            or ProtocolType.MitsubishiClient
            or ProtocolType.OmronFinsClient;

    /// <summary>仅西门子 / 三菱需要选择 PLC 版本。</summary>
    /// <param name="protocol">协议类型。</param>
    /// <returns>需要 PLC 版本选择时返回 true。</returns>
    public static bool NeedsPlcVersion(ProtocolType protocol)
        => protocol is ProtocolType.SiemensClient or ProtocolType.MitsubishiClient;

    /// <summary>
    /// 判断协议是否为 HttpClient（REST/HTTP 采集）。
    /// </summary>
    /// <param name="protocol">协议类型。</param>
    /// <returns>HttpClient 协议返回 true。</returns>
    public static bool IsHttp(ProtocolType protocol)
        => protocol is ProtocolType.HttpClient;
}
