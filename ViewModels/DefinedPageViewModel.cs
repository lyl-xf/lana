using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lana.Cameras.Services;
using Lana.Gateway.Models;
using Lana.Gateway.Services;

namespace Lana.ViewModels;

/// <summary>
/// 定义页按钮行为类型。
/// 扩展新行为：增加枚举 → CreateButton 判定 → ExecuteButtonAsync / 点动逻辑 → AXAML 模板。
/// </summary>
public enum DefinedButtonKind
{
    /// <summary>单击读取地址并显示结果。</summary>
    Read,
    /// <summary>单击写入预配置的 DefinedPageWriteValue。</summary>
    WriteValue,
    /// <summary>按下写 true、松开写 false（Bool/Coil/Discrete）。</summary>
    MomentaryBool,
}

/// <summary>定义页上的单个操作按钮模型（由物模型变量生成）。</summary>
public partial class DefinedVariableAction : ObservableObject
{
    public long VariableId { get; init; }
    public long DeviceId { get; init; }
    public string ButtonText { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
    public DataType DataType { get; init; }
    public DefinedButtonKind Kind { get; init; }
    public string WriteValue { get; init; } = string.Empty;
    /// <summary>点动 Bool：使用 Border + Pointer 事件（Button 会吞掉 PointerPressed）。</summary>
    public bool IsMomentaryBool => Kind == DefinedButtonKind.MomentaryBool;
    /// <summary>非点动：使用普通 Button + Command。</summary>
    public bool IsClickAction => Kind != DefinedButtonKind.MomentaryBool;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isPressed;
}

/// <summary>
/// 「定义页面」：上半区播放已启用摄像头，下半区左状态右按钮。
/// <para>
/// 按钮来源：活跃设备中 <c>ShowOnDefinedPage=true</c> 的变量（设备管理 → 物模型配置）。
/// 所有读写经 <see cref="IDeviceDebugApi"/>，Source=<c>DefinedPage</c>。
/// </para>
/// <para>
/// 自定义扩展：改 <see cref="LoadCustomButtonsAsync"/> / <see cref="CreateButton"/> /
/// 执行逻辑；摄像头网格见 PreviewSlots / StartCamerasAsync。
/// </para>
/// </summary>
public partial class DefinedPageViewModel : ViewModelBase, IDisposable
{
    private static readonly ObservableCollection<DeviceLiveGroup> EmptyStatusGroups = [];

    private readonly GatewayDeviceService _deviceService;
    private readonly IDeviceDebugApi _debugApi;
    private readonly CameraService _cameraService;
    private readonly DeviceDataSnapshotStore? _liveState;
    private readonly SemaphoreSlim _previewGate = new(1, 1);
    private readonly Dictionary<long, SemaphoreSlim> _momentaryGates = new();
    private bool _disposed;

    public DefinedPageViewModel(
        GatewayDeviceService deviceService,
        IDeviceDebugApi debugApi,
        CameraService cameraService,
        IDeviceDataSnapshotStore? liveState = null)
    {
        _deviceService = deviceService;
        _debugApi = debugApi;
        _cameraService = cameraService;
        _liveState = liveState as DeviceDataSnapshotStore;
        if (_liveState is not null)
        {
            _liveState.PropertyChanged += OnLiveStatePropertyChanged;
            HasStatusData = _liveState.HasData;
            if (HasStatusData)
                StatusPanelHint = "数据来自后台轮询（绑定共享状态，只读展示）";
        }

        _ = LibVlcHost.EnsureInitializedAsync();
    }

    [ObservableProperty]
    private string _statusMessage = "点击下方按钮进行操作";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _previewGridColumns = 1;

    public ObservableCollection<CameraPreviewSlot> PreviewSlots { get; } = [];

    public ObservableCollection<DefinedVariableAction> VariableActions { get; } = [];

    /// <summary>左侧设备状态（绑定共享 LiveState，Worker 原地更新属性）。</summary>
    public ObservableCollection<DeviceLiveGroup> StatusGroups
        => _liveState?.Groups ?? EmptyStatusGroups;

    [ObservableProperty]
    private bool _hasStatusData;

    [ObservableProperty]
    private string _statusPanelHint = "开启「轮询查询」后将显示采集数据";

    /// <summary>Shell 切入本页时调用：刷新按钮与摄像头预览。</summary>
    public async Task OnEnteredAsync()
        => await RefreshAllAsync();

    /// <summary>Shell 离开本页时调用：停止预览。</summary>
    public void StopPreviewsIfAny()
        => _ = StopAllPreviewsAsync();

    [RelayCommand]
    private async Task RefreshAllAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            await Task.WhenAll(LoadCustomButtonsAsync(), StartCamerasAsync());
            StatusMessage = BuildStatusSummary();
        }
        catch (Exception ex)
        {
            StatusMessage = "刷新失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string BuildStatusSummary()
    {
        var parts = new List<string>();
        if (StatusGroups.Count > 0)
            parts.Add($"状态 {StatusGroups.Sum(g => g.Points.Count)} 项");
        if (VariableActions.Count > 0)
            parts.Add($"{VariableActions.Count} 个操作按钮");
        if (PreviewSlots.Count > 0)
            parts.Add($"摄像头 {PreviewSlots.Count} 路");

        if (parts.Count == 0)
            return "暂无数据。请开启轮询或在物模型中配置「进入自定义页」。";

        return string.Join(" · ", parts);
    }

    private void OnLiveStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed || _liveState is null || e.PropertyName != nameof(DeviceDataSnapshotStore.HasData))
            return;

        HasStatusData = _liveState.HasData;
        StatusPanelHint = HasStatusData
            ? "数据来自后台轮询（绑定共享状态，只读展示）"
            : "暂无轮询数据。请在设备管理 → MQTT 中开启「轮询查询」，并确保设备采集周期 > 0";
    }

    /// <summary>从设备/物模型加载自定义按钮列表（仅 ShowOnDefinedPage）。</summary>
    private async Task LoadCustomButtonsAsync()
    {
        VariableActions.Clear();
        var devices = await _deviceService.ListDevicesAsync();
        foreach (var device in devices.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.Id))
        {
            var vars = await _deviceService.ListVariablesAsync(device.Id);
            foreach (var v in vars.Where(x => x.ShowOnDefinedPage)
                         .OrderBy(x => x.DefinedPageDisplayName)
                         .ThenBy(x => x.Alias)
                         .ThenBy(x => x.Address)
                         .ThenBy(x => x.Id))
            {
                VariableActions.Add(CreateButton(device.Id, v));
            }
        }
    }

    /// <summary>
    /// 按数据类型与 DefinedPageOperation 生成按钮模型。
    /// Bool/Coil/Discrete → 点动；其余 Write → 写默认值；否则读。
    /// </summary>
    private static DefinedVariableAction CreateButton(long deviceId, DeviceVariable v)
    {
        var title = ResolveDisplayName(v);
        var isBool = v.DataType is DataType.Bool or DataType.Coil or DataType.Discrete;

        if (isBool)
        {
            return new DefinedVariableAction
            {
                VariableId = v.Id,
                DeviceId = deviceId,
                ButtonText = title,
                Address = v.Address,
                DataType = v.DataType,
                Kind = DefinedButtonKind.MomentaryBool,
            };
        }

        if (v.DefinedPageOperation == DefinedPageOperation.Write)
        {
            return new DefinedVariableAction
            {
                VariableId = v.Id,
                DeviceId = deviceId,
                ButtonText = title,
                Address = v.Address,
                DataType = v.DataType,
                Kind = DefinedButtonKind.WriteValue,
                WriteValue = v.DefinedPageWriteValue ?? string.Empty,
            };
        }

        return new DefinedVariableAction
        {
            VariableId = v.Id,
            DeviceId = deviceId,
            ButtonText = title,
            Address = v.Address,
            DataType = v.DataType,
            Kind = DefinedButtonKind.Read,
        };
    }

    private static string ResolveDisplayName(DeviceVariable v)
    {
        if (!string.IsNullOrWhiteSpace(v.DefinedPageDisplayName))
            return v.DefinedPageDisplayName.Trim();
        if (!string.IsNullOrWhiteSpace(v.Alias))
            return v.Alias.Trim();
        return v.Address;
    }

    private async Task StartCamerasAsync()
    {
        if (!await _previewGate.WaitAsync(0))
            return;

        try
        {
            await StopAllPreviewsCoreAsync();
            var enabled = await _cameraService.ListEnabledAsync();
            if (enabled.Count == 0)
                return;

            await LibVlcHost.EnsureInitializedAsync();
            var host = LibVlcHost.Instance;
            var playTasks = new List<Task>();
            foreach (var camera in enabled.OrderBy(x => x.SortOrder).ThenBy(x => x.Id))
            {
                var slot = new CameraPreviewSlot(camera.Id, camera.Name, host.CreatePlayer());
                PreviewSlots.Add(slot);
                playTasks.Add(slot.PlayAsync(CameraService.BuildPlayRequest(camera)));
            }

            RefreshPreviewLayout();
            await Task.WhenAll(playTasks);
        }
        finally
        {
            _previewGate.Release();
        }
    }

    private void RefreshPreviewLayout()
        => PreviewGridColumns = PreviewSlots.Count <= 1 ? 1 : 2;

    private async Task StopAllPreviewsAsync()
    {
        await _previewGate.WaitAsync();
        try
        {
            await StopAllPreviewsCoreAsync();
        }
        finally
        {
            _previewGate.Release();
        }
    }

    private async Task StopAllPreviewsCoreAsync()
    {
        var slots = PreviewSlots.ToList();
        if (slots.Count == 0)
            return;

        PreviewSlots.Clear();
        RefreshPreviewLayout();

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var slot in slots)
                slot.NotifyDetaching();
        });
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        await Task.Delay(40);

        await Task.Run(() =>
        {
            foreach (var slot in slots)
            {
                try
                {
                    slot.Dispose();
                }
                catch
                {
                    /* ignore */
                }
            }
        });
    }

    [RelayCommand]
    private async Task ExecuteButtonAsync(DefinedVariableAction? action)
    {
        if (action is null || action.IsBusy || action.IsMomentaryBool)
            return;

        try
        {
            action.IsBusy = true;
            StatusMessage = $"正在执行：{action.ButtonText}…";

            switch (action.Kind)
            {
                case DefinedButtonKind.Read:
                {
                    var result = await _debugApi.ReadAsync(
                        action.DeviceId,
                        action.Address,
                        action.DataType,
                        new DeviceDebugContext { Source = "DefinedPage" });
                    StatusMessage = result.Success
                        ? $"{action.ButtonText} 读取成功：{FormatValue(result.Value)}"
                        : $"{action.ButtonText} 读取失败：{result.Error}";
                    break;
                }
                case DefinedButtonKind.WriteValue:
                {
                    if (string.IsNullOrWhiteSpace(action.WriteValue))
                    {
                        StatusMessage = $"{action.ButtonText} 未配置默认写入值";
                        break;
                    }

                    var result = await _debugApi.WriteAsync(
                        action.DeviceId,
                        action.Address,
                        action.DataType,
                        action.WriteValue,
                        new DeviceDebugContext { Source = "DefinedPage" });
                    StatusMessage = result.Success
                        ? $"{action.ButtonText} 写入成功（{action.WriteValue}）"
                        : $"{action.ButtonText} 写入失败：{result.Error}";
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"{action.ButtonText} 异常：{ex.Message}";
        }
        finally
        {
            action.IsBusy = false;
        }
    }

    /// <summary>定义页 Bool 按下：写入 true（由 View 指针事件调用）。</summary>
    public Task PressBoolAsync(DefinedVariableAction? action)
        => WriteMomentaryAsync(action, pressed: true);

    /// <summary>定义页 Bool 松开 / 失焦：写入 false。</summary>
    public Task ReleaseBoolAsync(DefinedVariableAction? action)
        => WriteMomentaryAsync(action, pressed: false);

    /// <summary>点动写：同一变量串行，保证先 true 后 false。</summary>
    private async Task WriteMomentaryAsync(DefinedVariableAction? action, bool pressed)
    {
        if (action is null || !action.IsMomentaryBool)
            return;

        // 按下时若已按下则忽略；松开时若从未按下则忽略
        if (pressed)
        {
            if (action.IsPressed)
                return;
            action.IsPressed = true;
        }
        else
        {
            if (!action.IsPressed)
                return;
            action.IsPressed = false;
        }

        var gate = GetMomentaryGate(action.VariableId);
        await gate.WaitAsync();
        var value = pressed ? "true" : "false";
        try
        {
            action.IsBusy = true;
            StatusMessage = pressed
                ? $"{action.ButtonText} 按下 → 写入 true…"
                : $"{action.ButtonText} 松开 → 写入 false…";

            var result = await _debugApi.WriteAsync(
                action.DeviceId,
                action.Address,
                action.DataType,
                value,
                new DeviceDebugContext { Source = "DefinedPage" });

            StatusMessage = result.Success
                ? $"{action.ButtonText} {(pressed ? "按下" : "松开")}成功（{value}）"
                : $"{action.ButtonText} {(pressed ? "按下" : "松开")}失败：{result.Error}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"{action.ButtonText} {(pressed ? "按下" : "松开")}异常：{ex.Message}";
        }
        finally
        {
            action.IsBusy = false;
            gate.Release();
        }
    }

    private SemaphoreSlim GetMomentaryGate(long variableId)
    {
        lock (_momentaryGates)
        {
            if (_momentaryGates.TryGetValue(variableId, out var gate))
                return gate;
            gate = new SemaphoreSlim(1, 1);
            _momentaryGates[variableId] = gate;
            return gate;
        }
    }

    private static string FormatValue(object? value)
        => value switch
        {
            null => "(null)",
            string s => s,
            bool b => b ? "true" : "false",
            _ => value.ToString() ?? string.Empty,
        };

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_liveState is not null)
            _liveState.PropertyChanged -= OnLiveStatePropertyChanged;

        var slots = PreviewSlots.ToList();
        PreviewSlots.Clear();
        foreach (var slot in slots)
        {
            try
            {
                slot.NotifyDetaching();
                slot.Dispose();
            }
            catch
            {
                /* ignore */
            }
        }

        _previewGate.Dispose();
        lock (_momentaryGates)
        {
            foreach (var gate in _momentaryGates.Values)
                gate.Dispose();
            _momentaryGates.Clear();
        }
    }
}
