using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lana.Gateway.Services;

/// <summary>
/// 定义页 / 其它 UI 共用的「单设备实时状态」分组模型。
/// <para>
/// 由 <see cref="DeviceDataSnapshotStore"/> 持有并维护；页面通过
/// <see cref="ObservableCollection{T}"/> 绑定 <see cref="Points"/>，Worker 轮询后
/// 仅更新变化的 <see cref="DeviceLivePoint.ValueText"/>，避免整表 Clear/重建。
/// </para>
/// </summary>
public partial class DeviceLiveGroup : ObservableObject
{
    /// <summary>设备主键，与 <see cref="Models.Device.Id"/> 一致。</summary>
    public long DeviceId { get; init; }

    /// <summary>设备显示名（轮询写入时可随配置变更而更新）。</summary>
    [ObservableProperty]
    private string _deviceName = string.Empty;

    /// <summary>本设备最近一次有效采集的本地时间（HH:mm:ss），无数据时为 <c>"--"</c>。</summary>
    [ObservableProperty]
    private string _updatedText = "--";

    /// <summary>
    /// 该设备下各变量的实时展示项。集合引用稳定；增删点仅在物模型变更时发生，日常轮询只改项内属性。
    /// </summary>
    public ObservableCollection<DeviceLivePoint> Points { get; } = [];
}

/// <summary>
/// 单个变量（或 payload 扩展键）的实时展示行。
/// <para><see cref="Label"/> 在创建时固定（通常来自物模型 Description）；<see cref="ValueText"/> 随轮询刷新。</para>
/// </summary>
public partial class DeviceLivePoint : ObservableObject
{
    /// <summary>物模型变量 Id；payload 中无对应变量时为 0，此时以 <see cref="Label"/> 区分。</summary>
    public long VariableId { get; init; }

    /// <summary>左侧标签文案（Description / Alias / 或 payload 键名）。</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>右侧采集值文本（已格式化，可直接绑定）。</summary>
    [ObservableProperty]
    private string _valueText = string.Empty;
}
