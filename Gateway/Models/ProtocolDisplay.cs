namespace Lana.Gateway.Models;

/// <summary>
/// 协议 / 数据类型 / 读写权限的展示名称与分类辅助。
/// </summary>
public static class ProtocolDisplay
{
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

    public static readonly string[] SiemensVersions =
    [
        "S7_200",
        "S7_200Smart",
        "S7_300",
        "S7_400",
        "S7_1200",
        "S7_1500",
    ];

    public static readonly string[] MitsubishiVersions =
    [
        "A_1E",
        "Qna_3E",
    ];

    public static readonly string[] DataTypeNames =
    [
        "Bool", "Int16", "Int32", "Float", "Double", "String",
        "Coil", "Discrete", "Short", "UShort", "Long", "ULong",
    ];

    public static readonly string[] ReadWriteNames =
    [
        "只读",
        "只写",
        "读写",
    ];

    public static string GetProtocolName(ProtocolType protocol)
    {
        var index = (int)protocol;
        if (index >= 0 && index < ProtocolNames.Length)
            return ProtocolNames[index];
        return protocol.ToString();
    }

    public static bool IsSerial(ProtocolType protocol)
        => protocol is ProtocolType.ModbusRtu or ProtocolType.ModbusAscii;

    public static bool IsTcp(ProtocolType protocol)
        => protocol is ProtocolType.ModbusTcp
            or ProtocolType.SiemensClient
            or ProtocolType.MitsubishiClient
            or ProtocolType.OmronFinsClient;

    public static bool IsPlc(ProtocolType protocol)
        => protocol is ProtocolType.SiemensClient
            or ProtocolType.MitsubishiClient
            or ProtocolType.OmronFinsClient;

    /// <summary>仅西门子 / 三菱需要选择 PLC 版本。</summary>
    public static bool NeedsPlcVersion(ProtocolType protocol)
        => protocol is ProtocolType.SiemensClient or ProtocolType.MitsubishiClient;

    public static bool IsHttp(ProtocolType protocol)
        => protocol is ProtocolType.HttpClient;
}
