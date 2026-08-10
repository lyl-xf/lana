using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lana.Gateway.Services;

/// <summary>线程安全的共享实时状态：后台写入、UI 线程合并，仅更新变化的属性。</summary>
public sealed partial class DeviceDataSnapshotStore : ObservableObject, IDeviceDataSnapshotStore
{
    private readonly object _gate = new();
    private readonly Dictionary<long, DeviceLiveGroup> _groupsById = new();
    private readonly Dictionary<long, (string DeviceName, List<DeviceVariableSnapshotEntry> Entries)> _pendingUpdates = new();
    private readonly Dictionary<long, DeviceSnapshotFingerprint> _fingerprints = new();
    private bool _uiFlushScheduled;

    public ObservableCollection<DeviceLiveGroup> Groups { get; } = [];

    [ObservableProperty]
    private bool _hasData;

    public void UpdateDevice(long deviceId, string deviceName, IReadOnlyList<DeviceVariableSnapshotEntry> entries)
    {
        var snapshotEntries = entries.ToList();
        lock (_gate)
        {
            if (!HasSnapshotChanged(deviceId, deviceName, snapshotEntries))
                return;

            _fingerprints[deviceId] = CreateFingerprint(deviceName, snapshotEntries);
            _pendingUpdates[deviceId] = (deviceName, snapshotEntries);

            if (_uiFlushScheduled)
                return;

            _uiFlushScheduled = true;
        }

        Dispatcher.UIThread.Post(FlushPendingUpdates, DispatcherPriority.Background);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _pendingUpdates.Clear();
            _fingerprints.Clear();
            _groupsById.Clear();
            _uiFlushScheduled = false;
        }

        Dispatcher.UIThread.Post(() =>
        {
            Groups.Clear();
            HasData = false;
        }, DispatcherPriority.Background);
    }

    private void FlushPendingUpdates()
    {
        List<(long DeviceId, string DeviceName, List<DeviceVariableSnapshotEntry> Entries)> batch;
        lock (_gate)
        {
            _uiFlushScheduled = false;
            batch = _pendingUpdates
                .Select(kv => (kv.Key, kv.Value.DeviceName, kv.Value.Entries))
                .ToList();
            _pendingUpdates.Clear();
        }

        foreach (var (deviceId, deviceName, entries) in batch)
            ApplyUpdateOnUiThread(deviceId, deviceName, entries);

        HasData = Groups.Any(g => g.Points.Count > 0);
    }

    private void ApplyUpdateOnUiThread(
        long deviceId,
        string deviceName,
        IReadOnlyList<DeviceVariableSnapshotEntry> entries)
    {
        if (!_groupsById.TryGetValue(deviceId, out var group))
        {
            group = new DeviceLiveGroup { DeviceId = deviceId, DeviceName = deviceName };
            _groupsById[deviceId] = group;
            InsertGroupSorted(group);
        }
        else if (!string.Equals(group.DeviceName, deviceName, StringComparison.Ordinal))
        {
            group.DeviceName = deviceName;
            ResortGroup(group);
        }

        group.UpdatedText = entries.Count > 0
            ? entries.Max(e => e.UpdatedAtUtc).ToLocalTime().ToString("HH:mm:ss")
            : "--";

        SyncPoints(group, entries);
    }

    private static void SyncPoints(DeviceLiveGroup group, IReadOnlyList<DeviceVariableSnapshotEntry> entries)
    {
        var incomingKeys = new HashSet<string>(entries.Select(PointKey));
        for (var i = group.Points.Count - 1; i >= 0; i--)
        {
            if (!incomingKeys.Contains(PointKey(group.Points[i])))
                group.Points.RemoveAt(i);
        }

        for (var targetIndex = 0; targetIndex < entries.Count; targetIndex++)
        {
            var entry = entries[targetIndex];
            var key = PointKey(entry);
            var currentIndex = FindPointIndex(group.Points, key);

            if (currentIndex < 0)
            {
                group.Points.Insert(Math.Min(targetIndex, group.Points.Count), new DeviceLivePoint
                {
                    VariableId = entry.VariableId,
                    Label = entry.Label,
                    ValueText = entry.ValueText,
                });
                continue;
            }

            var point = group.Points[currentIndex];
            if (currentIndex != targetIndex)
            {
                group.Points.RemoveAt(currentIndex);
                group.Points.Insert(Math.Min(targetIndex, group.Points.Count), point);
            }

            if (!string.Equals(point.Label, entry.Label, StringComparison.Ordinal))
            {
                group.Points.RemoveAt(targetIndex);
                group.Points.Insert(targetIndex, new DeviceLivePoint
                {
                    VariableId = entry.VariableId,
                    Label = entry.Label,
                    ValueText = entry.ValueText,
                });
                continue;
            }

            if (!string.Equals(point.ValueText, entry.ValueText, StringComparison.Ordinal))
                point.ValueText = entry.ValueText;
        }
    }

    private void InsertGroupSorted(DeviceLiveGroup group)
    {
        var index = 0;
        while (index < Groups.Count)
        {
            var other = Groups[index];
            if (string.Compare(other.DeviceName, group.DeviceName, StringComparison.OrdinalIgnoreCase) > 0
                || (string.Equals(other.DeviceName, group.DeviceName, StringComparison.OrdinalIgnoreCase)
                    && other.DeviceId > group.DeviceId))
                break;
            index++;
        }

        Groups.Insert(index, group);
    }

    private void ResortGroup(DeviceLiveGroup group)
    {
        var index = Groups.IndexOf(group);
        if (index < 0)
            return;

        Groups.RemoveAt(index);
        InsertGroupSorted(group);
    }

    private bool HasSnapshotChanged(
        long deviceId,
        string deviceName,
        IReadOnlyList<DeviceVariableSnapshotEntry> entries)
    {
        if (!_fingerprints.TryGetValue(deviceId, out var previous))
            return true;

        var next = CreateFingerprint(deviceName, entries);
        return !previous.Equals(next);
    }

    private static DeviceSnapshotFingerprint CreateFingerprint(
        string deviceName,
        IReadOnlyList<DeviceVariableSnapshotEntry> entries)
    {
        var parts = new List<string>(entries.Count + 1) { deviceName };
        foreach (var entry in entries)
            parts.Add($"{entry.VariableId}|{entry.Label}|{entry.ValueText}");
        return new DeviceSnapshotFingerprint(parts);
    }

    private static string PointKey(DeviceVariableSnapshotEntry entry)
        => entry.VariableId != 0 ? $"id:{entry.VariableId}" : $"label:{entry.Label}";

    private static string PointKey(DeviceLivePoint point)
        => point.VariableId != 0 ? $"id:{point.VariableId}" : $"label:{point.Label}";

    private static int FindPointIndex(IList<DeviceLivePoint> points, string key)
    {
        for (var i = 0; i < points.Count; i++)
        {
            if (PointKey(points[i]) == key)
                return i;
        }

        return -1;
    }

    private readonly struct DeviceSnapshotFingerprint : IEquatable<DeviceSnapshotFingerprint>
    {
        private readonly string _payload;

        public DeviceSnapshotFingerprint(IReadOnlyList<string> parts)
            => _payload = string.Join('\n', parts);

        public bool Equals(DeviceSnapshotFingerprint other)
            => string.Equals(_payload, other._payload, StringComparison.Ordinal);

        public override bool Equals(object? obj)
            => obj is DeviceSnapshotFingerprint other && Equals(other);

        public override int GetHashCode()
            => _payload.GetHashCode(StringComparison.Ordinal);
    }
}
