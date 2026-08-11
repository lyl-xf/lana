using Lana.Gateway.Models;

namespace Lana.Gateway.Services;

/// <summary>设备快照 DTO 构建（UI 状态区）。</summary>
public static class DeviceSnapshotBuilder
{
    /// <summary>由 payload 构建快照条目：ShowInStatus 变量 + Http 动态键。</summary>
    public static List<DeviceVariableSnapshotEntry> BuildStatusEntries(
        Device device,
        IReadOnlyDictionary<string, object> payload,
        DateTime updatedAtUtc)
    {
        var list = new List<DeviceVariableSnapshotEntry>();
        var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var variable in device.Variables)
        {
            if (string.IsNullOrWhiteSpace(variable.Alias))
                continue;
            if (!DeviceVariablePollRules.ShouldShowInStatus(variable))
                continue;
            if (!payload.TryGetValue(variable.Alias, out var val) || val is null)
                continue;

            usedKeys.Add(variable.Alias);
            list.Add(new DeviceVariableSnapshotEntry
            {
                VariableId = variable.Id,
                Label = DeviceSnapshotFormatting.ResolveLabel(variable),
                ValueText = DeviceSnapshotFormatting.FormatValue(val, variable.DataType),
                UpdatedAtUtc = updatedAtUtc,
            });
        }

        foreach (var kv in payload)
        {
            if (usedKeys.Contains(kv.Key) || kv.Value is null)
                continue;

            list.Add(new DeviceVariableSnapshotEntry
            {
                VariableId = 0,
                Label = kv.Key,
                ValueText = DeviceSnapshotFormatting.FormatValue(kv.Value),
                UpdatedAtUtc = updatedAtUtc,
            });
        }

        return list;
    }

    /// <summary>单点 Patch 条目（写成功后刷新 UI）。</summary>
    public static DeviceVariableSnapshotEntry? BuildPatchEntry(
        DeviceVariable variable,
        object value,
        DateTime updatedAtUtc)
    {
        if (!DeviceVariablePollRules.ShouldShowInStatus(variable))
            return null;

        return new DeviceVariableSnapshotEntry
        {
            VariableId = variable.Id,
            Label = DeviceSnapshotFormatting.ResolveLabel(variable),
            ValueText = DeviceSnapshotFormatting.FormatValue(value, variable.DataType),
            UpdatedAtUtc = updatedAtUtc,
        };
    }
}
