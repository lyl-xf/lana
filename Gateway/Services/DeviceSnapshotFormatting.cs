using Lana.Gateway.Models;

namespace Lana.Gateway.Services;

/// <summary>
/// 快照 / 缓存值的格式化（手动操作页状态区、DevicePointCache 共用）。
/// </summary>
public static class DeviceSnapshotFormatting
{
    /// <summary>将协议返回值格式化为展示文本。</summary>
    public static string FormatValue(object value, DataType? dataType = null)
    {
        if (TryFormatBoolDisplay(value, dataType, out var boolText))
            return boolText;

        return value switch
        {
            string s => s,
            _ => value.ToString() ?? string.Empty,
        };
    }

    /// <summary>快照展示标签：Description → Alias → Address。</summary>
    public static string ResolveLabel(DeviceVariable variable)
    {
        if (!string.IsNullOrWhiteSpace(variable.Description))
            return variable.Description.Trim();
        if (!string.IsNullOrWhiteSpace(variable.Alias))
            return variable.Alias.Trim();
        return variable.Address;
    }

    private static bool TryFormatBoolDisplay(object value, DataType? dataType, out string text)
    {
        text = string.Empty;
        var isBoolType = dataType is DataType.Bool or DataType.Coil or DataType.Discrete;

        if (value is bool b)
        {
            text = b ? "是" : "否";
            return true;
        }

        if (!isBoolType)
            return false;

        switch (value)
        {
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                var n = Convert.ToInt64(value);
                text = n != 0 ? "是" : "否";
                return true;
            case string s when bool.TryParse(s, out var parsed):
                text = parsed ? "是" : "否";
                return true;
            case string s when long.TryParse(s, out var num):
                text = num != 0 ? "是" : "否";
                return true;
            default:
                return false;
        }
    }
}
