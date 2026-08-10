using Lana.Gateway.Helpers;
using Lana.Gateway.Models;
using Lana.Gateway.Protocol;

namespace Lana.Gateway.Services;

/// <summary>
/// IoTClient 动态读写：按协议区分 Modbus（地址/站号/功能码）与 PLC。
/// 扩展新 DataType 时在此 switch 补齐 Read/Write 分支。
/// </summary>
public static class ProtocolIoTClientOperations
{
    /// <summary>
    /// 宽松布尔解析（"1" 或 "true" 视为 true）。
    /// </summary>
    /// <param name="s">字符串值。</param>
    /// <returns>解析结果。</returns>
    private static bool ParseLooseBool(string? s) =>
        s == "1" || (bool.TryParse(s, out var b) && b);

    /// <summary>
    /// 从 IoTClient 读取指定地址的值。
    /// </summary>
    /// <param name="client">IoTClient 动态实例。</param>
    /// <param name="protocolType">协议类型（决定地址解析方式）。</param>
    /// <param name="address">读点地址。</param>
    /// <param name="dataType">数据类型。</param>
    /// <returns>读结果（含 Value）。</returns>
    public static ProtocolResult<object?> Read(dynamic client, ProtocolType protocolType, string address, DataType dataType)
    {
        try
        {
            dynamic? readResult = null;
            var isModbus = protocolType is ProtocolType.ModbusTcp or ProtocolType.ModbusRtu or ProtocolType.ModbusAscii;

            if (isModbus)
            {
                // Modbus：解析站号/功能码后调用对应 Read 方法
                var (addr, sn, fc) = ModbusHelper.ParseModbusAddress(address, dataType, false);
                readResult = dataType switch
                {
                    DataType.Bool => (dynamic)client.ReadCoil(addr, sn, fc),
                    DataType.Int16 => (dynamic)client.ReadInt16(addr, sn, fc),
                    DataType.Int32 => (dynamic)client.ReadInt32(addr, sn, fc),
                    DataType.Float => (dynamic)client.ReadFloat(addr, sn, fc),
                    DataType.Double => (dynamic)client.ReadDouble(addr, sn, fc),
                    DataType.String => (dynamic)client.ReadString(addr, sn, fc),
                    DataType.Coil => (dynamic)client.ReadCoil(addr, sn, fc),
                    DataType.Discrete => (dynamic)client.ReadDiscrete(addr, sn, fc),
                    DataType.Short => (dynamic)client.ReadInt16(addr, sn, fc),
                    DataType.UShort => (dynamic)client.ReadUInt16(addr, sn, fc),
                    DataType.Long => (dynamic)client.ReadInt64(addr, sn, fc),
                    DataType.ULong => (dynamic)client.ReadUInt64(addr, sn, fc),
                    _ => null
                };
            }
            else
            {
                // PLC：地址直接传入，无需 Modbus 解析
                readResult = dataType switch
                {
                    DataType.Bool => (dynamic)client.ReadBoolean(address),
                    DataType.Int16 => (dynamic)client.ReadInt16(address),
                    DataType.Int32 => (dynamic)client.ReadInt32(address),
                    DataType.Float => (dynamic)client.ReadFloat(address),
                    DataType.Double => (dynamic)client.ReadDouble(address),
                    DataType.String => (dynamic)client.ReadString(address),
                    DataType.Coil => (dynamic)client.ReadCoil(address),
                    DataType.Discrete => (dynamic)client.ReadDiscrete(address),
                    DataType.Short => (dynamic)client.ReadInt16(address),
                    DataType.UShort => (dynamic)client.ReadUInt16(address),
                    DataType.Long => (dynamic)client.ReadInt64(address),
                    DataType.ULong => (dynamic)client.ReadUInt64(address),
                    _ => null
                };
            }

            if (readResult == null)
                return ProtocolResult<object?>.Fail("不支持的数据类型");

            return readResult.IsSucceed
                ? ProtocolResult<object?>.Ok((object?)readResult.Value)
                : ProtocolResult<object?>.Fail(readResult.Err?.ToString());
        }
        catch (Exception ex)
        {
            return ProtocolResult<object?>.Fail(ex.Message);
        }
    }

    /// <summary>
    /// 向 IoTClient 写入指定地址的值。
    /// </summary>
    /// <param name="client">IoTClient 动态实例。</param>
    /// <param name="protocolType">协议类型。</param>
    /// <param name="address">写点地址。</param>
    /// <param name="dataType">数据类型。</param>
    /// <param name="value">字符串形式的写入值。</param>
    /// <returns>写结果。</returns>
    public static ProtocolResult Write(dynamic client, ProtocolType protocolType, string address, DataType dataType, string? value)
    {
        try
        {
            var isModbus = protocolType is ProtocolType.ModbusTcp or ProtocolType.ModbusRtu or ProtocolType.ModbusAscii;

            if (isModbus)
            {
                var (addr, sn, fc) = ModbusHelper.ParseModbusAddress(address, dataType, true);
                switch (dataType)
                {
                    case DataType.Bool:
                    case DataType.Coil:
                        client.Write(addr, ParseLooseBool(value), sn, fc);
                        break;
                    case DataType.Int16:
                    case DataType.Short:
                        client.Write(addr, short.Parse(value!), sn, fc);
                        break;
                    case DataType.Int32:
                        client.Write(addr, int.Parse(value!), sn, fc);
                        break;
                    case DataType.Float:
                        client.Write(addr, float.Parse(value!), sn, fc);
                        break;
                    case DataType.Double:
                        client.Write(addr, double.Parse(value!), sn, fc);
                        break;
                    case DataType.String:
                        client.Write(addr, value ?? "", sn, fc);
                        break;
                    case DataType.Discrete:
                        return ProtocolResult.Ok(); // 离散输入只读
                    case DataType.UShort:
                        client.Write(addr, ushort.Parse(value!), sn, fc);
                        break;
                    case DataType.Long:
                        client.Write(addr, long.Parse(value!), sn, fc);
                        break;
                    case DataType.ULong:
                        client.Write(addr, ulong.Parse(value!), sn, fc);
                        break;
                    default:
                        return ProtocolResult.Fail("不支持的数据类型");
                }
            }
            else
            {
                // PLC 写：地址直接使用
                switch (dataType)
                {
                    case DataType.Bool:
                    case DataType.Coil:
                        client.Write(address, ParseLooseBool(value));
                        break;
                    case DataType.Int16:
                    case DataType.Short:
                        client.Write(address, short.Parse(value!));
                        break;
                    case DataType.Int32:
                        client.Write(address, int.Parse(value!));
                        break;
                    case DataType.Float:
                        client.Write(address, float.Parse(value!));
                        break;
                    case DataType.Double:
                        client.Write(address, double.Parse(value!));
                        break;
                    case DataType.String:
                        client.Write(address, value ?? "");
                        break;
                    case DataType.Discrete:
                        return ProtocolResult.Ok();
                    case DataType.UShort:
                        client.Write(address, ushort.Parse(value!));
                        break;
                    case DataType.Long:
                        client.Write(address, long.Parse(value!));
                        break;
                    case DataType.ULong:
                        client.Write(address, ulong.Parse(value!));
                        break;
                    default:
                        return ProtocolResult.Fail("不支持的数据类型");
                }
            }

            return ProtocolResult.Ok();
        }
        catch (Exception ex)
        {
            return ProtocolResult.Fail(ex.Message);
        }
    }
}
