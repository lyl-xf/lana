using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lana.Gateway.Services;

/// <summary>单设备实时状态分组（UI 绑定，属性变更仅更新对应单元格）。</summary>
public partial class DeviceLiveGroup : ObservableObject
{
    public long DeviceId { get; init; }

    [ObservableProperty]
    private string _deviceName = string.Empty;

    [ObservableProperty]
    private string _updatedText = "--";

    public ObservableCollection<DeviceLivePoint> Points { get; } = [];
}

/// <summary>单个变量的实时展示项。</summary>
public partial class DeviceLivePoint : ObservableObject
{
    public long VariableId { get; init; }

    public string Label { get; init; } = string.Empty;

    [ObservableProperty]
    private string _valueText = string.Empty;
}
