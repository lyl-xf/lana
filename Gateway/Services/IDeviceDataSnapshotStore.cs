using System.Collections.ObjectModel;

namespace Lana.Gateway.Services;

/// <summary>
/// 轮询采集结果的共享实时状态（供 UI 直接绑定）。Worker 写入，页面只读绑定，不整表刷新。
/// </summary>
public interface IDeviceDataSnapshotStore
{
    /// <summary>按设备分组的实时状态（稳定集合引用，项内属性原地更新）。</summary>
    ObservableCollection<DeviceLiveGroup> Groups { get; }

    /// <summary>是否存在可展示的数据。</summary>
    bool HasData { get; }

    /// <summary>写入/合并某设备本轮轮询结果（可在后台线程调用）。</summary>
    void UpdateDevice(long deviceId, string deviceName, IReadOnlyList<DeviceVariableSnapshotEntry> entries);

    void Clear();
}

/// <summary>Worker → Store 的单次变量快照（传输 DTO，非 UI 模型）。</summary>
public sealed class DeviceVariableSnapshotEntry
{
    public long VariableId { get; init; }
    public string Label { get; init; } = string.Empty;
    public string ValueText { get; init; } = string.Empty;
    public DateTime UpdatedAtUtc { get; init; }
}
