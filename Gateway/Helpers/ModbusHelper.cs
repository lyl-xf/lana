using Lana.Gateway.Models;

namespace Lana.Gateway.Helpers;

/// <summary>
/// Modbus 地址解析：支持「地址」「站号;地址」「站号;地址;功能码」等形式。
/// </summary>
public static class ModbusHelper
{
    /// <summary>
    /// 解析 Modbus 读/写地址，提取纯地址、站号与功能码。
    /// </summary>
    /// <param name="input">用户输入的地址字符串。</param>
    /// <param name="dataType">数据类型（影响默认功能码选择）。</param>
    /// <param name="isWrite">是否为写操作（写操作默认功能码 5/16）。</param>
    /// <returns>元组：(纯地址, 站号, 功能码)。</returns>
    public static (string address, byte stationNumber, byte functionCode) ParseModbusAddress(
        string input, DataType dataType, bool isWrite = false)
    {
        var address = input;
        byte stationNumber = 1;
        byte functionCode = 3; // 默认读保持寄存器

        // 写操作：线圈用 5，其它寄存器用 16
        if (isWrite)
        {
            functionCode = dataType is DataType.Bool or DataType.Coil ? (byte)5 : (byte)16;
        }
        // 读操作：按数据类型选择默认功能码
        else if (dataType is DataType.Bool or DataType.Coil)
        {
            functionCode = 1; // 读线圈
        }
        else if (dataType == DataType.Discrete)
        {
            functionCode = 2; // 读离散输入
        }

        // 显式分隔符格式：地址;站号;功能码 或 地址,站号,功能码
        if (input.Contains(',') || input.Contains(';'))
        {
            var parts = input.Split([',', ';']);
            address = parts[0].Trim();
            if (parts.Length > 1 && byte.TryParse(parts[1].Trim(), out var sn))
                stationNumber = sn;
            if (parts.Length > 2 && byte.TryParse(parts[2].Trim(), out var fc))
                functionCode = fc;
        }
        // 隐式前缀格式：首位数字表示功能码区域（如 40001 → 4 开头表示保持寄存器）
        else if ((input.Length is 5 or 6) && char.IsDigit(input[0]))
        {
            var firstDigit = input[0];
            if (!isWrite)
            {
                functionCode = firstDigit switch
                {
                    '0' => (byte)1,  // 线圈
                    '1' => (byte)2,  // 离散输入
                    '3' => (byte)4,  // 输入寄存器
                    '4' => (byte)3,  // 保持寄存器
                    _ => functionCode
                };
            }
            else
            {
                if (firstDigit == '0') functionCode = 5;
                else if (firstDigit == '4') functionCode = 16;
            }
        }

        return (address, stationNumber, functionCode);
    }
}
