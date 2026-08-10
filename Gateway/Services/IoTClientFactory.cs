using System.IO.Ports;
using IoTClient.Clients.Modbus;
using IoTClient.Clients.PLC;
using IoTClient.Common.Enums;
using IoTClient.Enums;
using Lana.Gateway.Models;

namespace Lana.Gateway.Services;

/// <summary>
/// 按协议创建 IoTClient 动态客户端实例。
/// 新增 IoTClient 支持的协议：在此 switch 增加分支，并扩展 ProtocolType。
/// </summary>
public static class IoTClientFactory
{
    /// <summary>创建未 Open 的客户端；HttpClient 协议请走 HttpClientDeviceSession。</summary>
    /// <param name="device">设备实体（含协议与连接参数）。</param>
    /// <returns>IoTClient 动态客户端实例。</returns>
    /// <exception cref="NotSupportedException">不支持的协议类型。</exception>
    public static dynamic CreateClient(Device device)
    {
        return device.ProtocolType switch
        {
            ProtocolType.ModbusTcp => new ModbusTcpClient(device.Ip, device.Port),
            ProtocolType.ModbusRtu => new ModbusRtuClient(
                device.PortName, device.BaudRate, device.DataBits,
                (StopBits)device.StopBits, (Parity)device.Parity),
            ProtocolType.ModbusAscii => new ModbusAsciiClient(
                device.PortName, device.BaudRate, device.DataBits,
                (StopBits)device.StopBits, (Parity)device.Parity),
            ProtocolType.SiemensClient => new SiemensClient(
                ParseSiemensVersion(device.PlcVersion),
                new System.Net.IPEndPoint(System.Net.IPAddress.Parse(device.Ip), device.Port)),
            ProtocolType.MitsubishiClient => new MitsubishiClient(
                ParseMitsubishiVersion(device.PlcVersion),
                device.Ip, device.Port),
            ProtocolType.OmronFinsClient => new OmronFinsClient(device.Ip, device.Port),
            _ => throw new NotSupportedException($"协议 {device.ProtocolType} 不受支持（自定义协议已移除）。")
        };
    }

    /// <summary>
    /// 解析西门子 PLC 版本字符串为 IoTClient 枚举。
    /// IoTClient 枚举为 S7_200Smart；兼容误存的 S7-200Smart / S7200Smart。
    /// </summary>
    /// <param name="plcVersion">版本字符串。</param>
    /// <returns>SiemensVersion 枚举值。</returns>
    /// <exception cref="ArgumentException">无法识别的版本。</exception>
    public static SiemensVersion ParseSiemensVersion(string? plcVersion)
    {
        var normalized = NormalizeSiemensVersion(plcVersion);
        if (Enum.TryParse<SiemensVersion>(normalized, ignoreCase: true, out var version))
            return version;

        throw new ArgumentException(
            $"不支持的西门子 PLC 版本「{plcVersion}」。请使用：{string.Join(", ", ProtocolDisplay.SiemensVersions)}");
    }

    /// <summary>
    /// 规范化西门子版本字符串（兼容多种写法）。
    /// </summary>
    /// <param name="plcVersion">原始版本字符串。</param>
    /// <returns>规范化后的版本名（如 S7_1200）。</returns>
    public static string NormalizeSiemensVersion(string? plcVersion)
    {
        if (string.IsNullOrWhiteSpace(plcVersion))
            return nameof(SiemensVersion.S7_1200);

        var value = plcVersion.Trim();
        if (Enum.TryParse<SiemensVersion>(value, ignoreCase: true, out _))
            return value;

        // S7-200Smart / S7 200 Smart → S7_200Smart
        value = value.Replace(" ", string.Empty).Replace('-', '_');
        if (!value.StartsWith("S7_", StringComparison.OrdinalIgnoreCase) &&
            value.StartsWith("S7", StringComparison.OrdinalIgnoreCase))
        {
            value = "S7_" + value[2..];
        }

        return value;
    }

    /// <summary>
    /// 解析三菱 PLC 版本字符串为 IoTClient 枚举。
    /// </summary>
    /// <param name="plcVersion">版本字符串。</param>
    /// <returns>MitsubishiVersion 枚举值。</returns>
    /// <exception cref="ArgumentException">无法识别的版本。</exception>
    private static MitsubishiVersion ParseMitsubishiVersion(string? plcVersion)
    {
        if (string.IsNullOrWhiteSpace(plcVersion))
            return MitsubishiVersion.Qna_3E;

        if (Enum.TryParse<MitsubishiVersion>(plcVersion.Trim(), ignoreCase: true, out var version))
            return version;

        throw new ArgumentException(
            $"不支持的三菱 PLC 版本「{plcVersion}」。请使用：{string.Join(", ", ProtocolDisplay.MitsubishiVersions)}");
    }
}
