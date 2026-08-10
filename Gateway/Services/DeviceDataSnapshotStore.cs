using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lana.Gateway.Services;

/// <summary>
/// <see cref="IDeviceDataSnapshotStore"/> 默认实现：线程安全的共享实时状态。
/// <para>
/// <b>后台：</b>指纹比对，无变化则跳过；有变化写入 <c>_pendingUpdates</c> 并调度一次 UI Flush。<br/>
/// <b>UI 线程：</b>批量合并 pending，对 <see cref="DeviceLiveGroup"/> / <see cref="DeviceLivePoint"/> 原地 diff，
/// 仅触发变更属性的 INotifyPropertyChanged。
/// </para>
/// <para>生命周期：由 <c>MainViewModel</c> 构造单例，注入 Worker 与 <c>DefinedPageViewModel</c>。</para>
/// </summary>
public sealed partial class DeviceDataSnapshotStore : ObservableObject, IDeviceDataSnapshotStore
{
    /// <summary>保护指纹、pending 队列与内部索引。</summary>
    private readonly object _gate = new();

    /// <summary>deviceId → 已挂到 <see cref="Groups"/> 的分组实例（与 UI 绑定同一对象）。</summary>
    private readonly Dictionary<long, DeviceLiveGroup> _groupsById = new();

    /// <summary>待合并到 UI 的设备轮次（同 deviceId 后写覆盖先写）。</summary>
    private readonly Dictionary<long, (string DeviceName, List<DeviceVariableSnapshotEntry> Entries)> _pendingUpdates = new();

    /// <summary>各设备上次已应用的快照指纹，用于后台快速跳过无变化轮次。</summary>
    private readonly Dictionary<long, DeviceSnapshotFingerprint> _fingerprints = new();

    /// <summary>是否已调度 <see cref="FlushPendingUpdates"/>，避免重复 Post。</summary>
    private bool _uiFlushScheduled;

    /// <inheritdoc />
    public ObservableCollection<DeviceLiveGroup> Groups { get; } = [];

    /// <inheritdoc />
    [ObservableProperty]
    private bool _hasData;

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <summary>UI 线程：取出 pending 批次，逐设备合并并刷新 <see cref="HasData"/>。</summary>
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

    /// <summary>UI 线程：确保分组存在并同步设备名、更新时间、点位列表。</summary>
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

    /// <summary>
    /// UI 线程：将 <paramref name="entries"/> 与分组内 <see cref="DeviceLiveGroup.Points"/> 对齐。
    /// 移除多余点、调整顺序、新增点；已存在点仅更新 <see cref="DeviceLivePoint.ValueText"/>。
    /// </summary>
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

    /// <summary>按设备名（不区分大小写）、再按 Id 插入 <see cref="Groups"/>。</summary>
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

    /// <summary>设备改名后重新排序分组。</summary>
    private void ResortGroup(DeviceLiveGroup group)
    {
        var index = Groups.IndexOf(group);
        if (index < 0)
            return;

        Groups.RemoveAt(index);
        InsertGroupSorted(group);
    }

    /// <summary>对比上次指纹，判断本轮是否有实质变化。</summary>
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

    /// <summary>构造可比较的指纹字符串（设备名 + 各点 Id/Label/Value）。</summary>
    private static DeviceSnapshotFingerprint CreateFingerprint(
        string deviceName,
        IReadOnlyList<DeviceVariableSnapshotEntry> entries)
    {
        var parts = new List<string>(entries.Count + 1) { deviceName };
        foreach (var entry in entries)
            parts.Add($"{entry.VariableId}|{entry.Label}|{entry.ValueText}");
        return new DeviceSnapshotFingerprint(parts);
    }

    /// <summary>点位唯一键：有 VariableId 用 Id，否则用 Label（payload 扩展键）。</summary>
    private static string PointKey(DeviceVariableSnapshotEntry entry)
        => entry.VariableId != 0 ? $"id:{entry.VariableId}" : $"label:{entry.Label}";

    /// <inheritdoc cref="PointKey(DeviceVariableSnapshotEntry)"/>
    private static string PointKey(DeviceLivePoint point)
        => point.VariableId != 0 ? $"id:{point.VariableId}" : $"label:{point.Label}";

    /// <summary>在集合中按 <see cref="PointKey(DeviceLivePoint)"/> 查找索引。</summary>
    private static int FindPointIndex(IList<DeviceLivePoint> points, string key)
    {
        for (var i = 0; i < points.Count; i++)
        {
            if (PointKey(points[i]) == key)
                return i;
        }

        return -1;
    }

    /// <summary>设备快照指纹值对象，用于 O(1) 比较整轮是否变化。</summary>
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
