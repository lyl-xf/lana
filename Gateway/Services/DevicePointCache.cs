using Lana.Gateway.Models;

namespace Lana.Gateway.Services;

/// <summary>
/// 进程内设备点 live 缓存：Poll 整轮 commit，带 Version；线程安全。
/// </summary>
public sealed class DevicePointCache : IDevicePointCache
{
    private readonly object _gate = new();
    private readonly Dictionary<long, DeviceCacheState> _devices = new();

    /// <inheritdoc />
    public void CommitPoll(
        long deviceId,
        Device device,
        IReadOnlyDictionary<string, object> payload,
        DateTime updatedAtUtc)
    {
        var points = new Dictionary<string, CachedPointValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in payload)
        {
            if (kv.Value is null)
                continue;

            var variable = device.Variables.FirstOrDefault(v =>
                string.Equals(v.Alias, kv.Key, StringComparison.OrdinalIgnoreCase));

            points[kv.Key] = new CachedPointValue
            {
                VariableId = variable?.Id ?? 0,
                RawValue = kv.Value,
                ValueText = DeviceSnapshotFormatting.FormatValue(kv.Value, variable?.DataType),
                UpdatedAtUtc = updatedAtUtc,
            };
        }

        lock (_gate)
        {
            if (!_devices.TryGetValue(deviceId, out var state))
            {
                state = new DeviceCacheState();
                _devices[deviceId] = state;
            }

            state.Version++;
            state.UpdatedAtUtc = updatedAtUtc;
            state.Points = points;
        }
    }

    /// <inheritdoc />
    public long GetVersion(long deviceId)
    {
        lock (_gate)
            return _devices.TryGetValue(deviceId, out var state) ? state.Version : 0;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object> GetTelemetryPayload(Device device)
    {
        lock (_gate)
        {
            if (!_devices.TryGetValue(device.Id, out var state) || state.Points.Count == 0)
                return EmptyPayload;

            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var knownAliases = new HashSet<string>(
                device.Variables
                    .Where(v => !string.IsNullOrWhiteSpace(v.Alias))
                    .Select(v => v.Alias),
                StringComparer.OrdinalIgnoreCase);

            foreach (var variable in device.Variables)
            {
                if (!DeviceVariablePollRules.ShouldIncludeInTelemetry(variable))
                    continue;
                if (!state.Points.TryGetValue(variable.Alias, out var point))
                    continue;

                result[variable.Alias] = point.RawValue;
            }

            var includeDynamicKeys = device.Variables.Any(v =>
                v.IncludeInPoll
                && v.IncludeInTelemetry
                && device.ProtocolType == ProtocolType.HttpClient);

            if (includeDynamicKeys)
            {
                foreach (var kv in state.Points)
                {
                    if (knownAliases.Contains(kv.Key))
                        continue;

                    result[kv.Key] = kv.Value.RawValue;
                }
            }

            return result;
        }
    }

    /// <inheritdoc />
    public void PatchPoint(
        long deviceId,
        Device device,
        string alias,
        object value,
        DateTime updatedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(alias))
            return;

        var variable = device.Variables.FirstOrDefault(v =>
            string.Equals(v.Alias, alias, StringComparison.OrdinalIgnoreCase));

        lock (_gate)
        {
            if (!_devices.TryGetValue(deviceId, out var state))
            {
                state = new DeviceCacheState();
                _devices[deviceId] = state;
            }

            state.Version++;
            state.UpdatedAtUtc = updatedAtUtc;
            state.Points[alias] = new CachedPointValue
            {
                VariableId = variable?.Id ?? 0,
                RawValue = value,
                ValueText = DeviceSnapshotFormatting.FormatValue(value, variable?.DataType),
                UpdatedAtUtc = updatedAtUtc,
            };
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_gate)
            _devices.Clear();
    }

    private static readonly IReadOnlyDictionary<string, object> EmptyPayload =
        new Dictionary<string, object>();

    private sealed class DeviceCacheState
    {
        public long Version;
        public DateTime UpdatedAtUtc;
        public Dictionary<string, CachedPointValue> Points = new(StringComparer.OrdinalIgnoreCase);
    }
}
