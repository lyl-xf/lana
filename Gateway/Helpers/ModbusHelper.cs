using Lana.Gateway.Models;

namespace Lana.Gateway.Helpers;

/// <summary>
/// Modbus 地址解析：支持「地址」「站号;地址」「站号;地址;功能码」等形式。
/// </summary>
public static class ModbusHelper
{
    public static (string address, byte stationNumber, byte functionCode) ParseModbusAddress(
        string input, DataType dataType, bool isWrite = false)
    {
        var address = input;
        byte stationNumber = 1;
        byte functionCode = 3;

        if (isWrite)
        {
            functionCode = dataType is DataType.Bool or DataType.Coil ? (byte)5 : (byte)16;
        }
        else if (dataType is DataType.Bool or DataType.Coil)
        {
            functionCode = 1;
        }
        else if (dataType == DataType.Discrete)
        {
            functionCode = 2;
        }

        if (input.Contains(',') || input.Contains(';'))
        {
            var parts = input.Split([',', ';']);
            address = parts[0].Trim();
            if (parts.Length > 1 && byte.TryParse(parts[1].Trim(), out var sn))
                stationNumber = sn;
            if (parts.Length > 2 && byte.TryParse(parts[2].Trim(), out var fc))
                functionCode = fc;
        }
        else if ((input.Length is 5 or 6) && char.IsDigit(input[0]))
        {
            var firstDigit = input[0];
            if (!isWrite)
            {
                functionCode = firstDigit switch
                {
                    '0' => (byte)1,
                    '1' => (byte)2,
                    '3' => (byte)4,
                    '4' => (byte)3,
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
