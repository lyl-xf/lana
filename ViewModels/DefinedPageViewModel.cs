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
    /// <summary>物模型变量 Id。</summary>
    public long VariableId { get; init; }

    /// <summary>所属设备 Id。</summary>
    public long DeviceId { get; init; }

    /// <summary>按钮显示文案。</summary>
    public string ButtonText { get; init; } = string.Empty;

    /// <summary>协议地址。</summary>
    public string Address { get; init; } = string.Empty;

    /// <summary>数据类型（决定读写法与是否点动）。</summary>
    public DataType DataType { get; init; }

    /// <summary>按钮行为种类。</summary>
    public DefinedButtonKind Kind { get; init; }

    /// <summary>WriteValue 行为时的默认写入字符串。</summary>
    public string WriteValue { get; init; } = string.Empty;

    /// <summary>点动 Bool：使用 Border + Pointer 事件（Button 会吞掉 PointerPressed）。</summary>
    public bool IsMomentaryBool => Kind == DefinedButtonKind.MomentaryBool;

    /// <summary>非点动：使用普通 Button + Command。</summary>
    public bool IsClickAction => Kind != DefinedButtonKind.MomentaryBool;

    /// <summary>是否正在执行读写操作。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>点动按下中为 true，用于忽略重复 Pointer 事件。</summary>
    [ObservableProperty]
    private bool _isPressed;
}

/// <summary>
/// 「手动操作」页：上半区播放已启用摄像头，下半区左状态右按钮。
/// <para>
/// <b>左侧状态：</b>直接绑定 <see cref="DeviceDataSnapshotStore.Groups"/>（共享实时状态），
/// Worker 轮询后原地更新属性，本 VM 不订阅快照事件、不 Clear 重建列表。
/// </para>
/// <para>
/// <b>右侧按钮：</b>活跃设备中 <c>ShowOnDefinedPage=true</c> 的变量（设备管理 → 物模型配置）；
/// 所有读写经 <see cref="IDeviceDebugApi"/>，Source=<c>DefinedPage</c>。
/// </para>
/// <para>
/// 自定义扩展：改 <see cref="LoadCustomButtonsAsync"/> / <see cref="CreateButton"/> /
/// 执行逻辑；摄像头网格见 PreviewSlots / StartCamerasAsync。
/// </para>
/// </summary>
public partial class DefinedPageViewModel : ViewModelBase, IDisposable
{
    /// <summary>未注入 LiveState 时的空集合占位，避免绑定 null。</summary>
    private static readonly ObservableCollection<DeviceLiveGroup> EmptyStatusGroups = [];

    /// <summary>网关设备服务（加载物模型按钮）。</summary>
    private readonly GatewayDeviceService _deviceService;

    /// <summary>设备调试 API（按钮读写）。</summary>
    private readonly IDeviceDebugApi _debugApi;

    /// <summary>摄像头服务（上半区预览）。</summary>
    private readonly CameraService _cameraService;

    /// <summary>共享实时状态（与 Worker 同一实例）；需监听 <see cref="DeviceDataSnapshotStore.HasData"/> 以更新空状态 UI。</summary>
    private readonly DeviceDataSnapshotStore? _liveState;

    /// <summary>串行化摄像头预览启停，避免并发 Start/Stop。</summary>
    private readonly SemaphoreSlim _previewGate = new(1, 1);

    /// <summary>点动 Bool 按变量 Id 串行写，保证 true→false 顺序。</summary>
    private readonly Dictionary<long, SemaphoreSlim> _momentaryGates = new();

    /// <summary>是否已释放。</summary>
    private bool _disposed;

    /// <summary>
    /// 构造定义页 VM。
    /// </summary>
    /// <param name="deviceService">网关设备服务。</param>
    /// <param name="debugApi">设备调试 API。</param>
    /// <param name="cameraService">摄像头服务。</param>
    /// <param name="liveState">
    /// 由 MainViewModel 注入的 <see cref="DeviceDataSnapshotStore"/>；传接口时内部 as 为具体类型以订阅 PropertyChanged。
    /// </param>
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
                StatusPanelHint = "数据来自后台轮询缓存（仅「状态展示」点位，只读）";
        }

        _ = LibVlcHost.EnsureInitializedAsync();
    }

    /// <summary>底部状态栏提示信息。</summary>
    [ObservableProperty]
    private string _statusMessage = "点击下方按钮进行操作";

    /// <summary>是否正在刷新（按钮/摄像头）。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>摄像头预览 UniformGrid 列数（1 或 2）。</summary>
    [ObservableProperty]
    private int _previewGridColumns = 1;

    /// <summary>上半区摄像头预览槽位。</summary>
    public ObservableCollection<CameraPreviewSlot> PreviewSlots { get; } = [];

    /// <summary>下半区右侧操作按钮。</summary>
    public ObservableCollection<DefinedVariableAction> VariableActions { get; } = [];

    /// <summary>
    /// 下半区左侧设备状态：与 <see cref="DeviceDataSnapshotStore.Groups"/> 同一引用，绑一次即可。
    /// </summary>
    public ObservableCollection<DeviceLiveGroup> StatusGroups
        => _liveState?.Groups ?? EmptyStatusGroups;

    /// <summary>是否有轮询数据可展示（镜像 LiveState.HasData）。</summary>
    [ObservableProperty]
    private bool _hasStatusData;

    /// <summary>状态区顶部提示文案。</summary>
    [ObservableProperty]
    private string _statusPanelHint = "开启轮询后，勾选「状态展示」的点位将显示在左侧";

    /// <summary>
    /// Shell 切入本页时调用：刷新按钮与摄像头预览。
    /// </summary>
    /// <returns>表示刷新完成的 Task。</returns>
    public async Task OnEnteredAsync()
        => await RefreshAllAsync();

    /// <summary>
    /// Shell 离开本页时调用：停止预览。
    /// </summary>
    public void StopPreviewsIfAny()
        => _ = StopAllPreviewsAsync();

    /// <summary>
    /// 刷新摄像头预览与操作按钮列表（不刷新左侧状态，状态由绑定自动更新）。
    /// </summary>
    /// <returns>表示刷新完成的 Task。</returns>
    [RelayCommand]
    private async Task RefreshAllAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            // 并行加载按钮与启动摄像头预览
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

    /// <summary>
    /// 汇总状态项、按钮数、摄像头路数，用于底部状态栏。
    /// </summary>
    /// <returns>状态摘要字符串。</returns>
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
            return "暂无数据。请开启轮询或在物模型中开启「手动操作」并配置操作名称。";

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// LiveState.HasData 变化时同步空状态提示（点位值变化不经过此处，由绑定自动刷新）。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">属性变更参数。</param>
    private void OnLiveStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed || _liveState is null || e.PropertyName != nameof(DeviceDataSnapshotStore.HasData))
            return;

        HasStatusData = _liveState.HasData;
        StatusPanelHint = HasStatusData
            ? "数据来自后台轮询（绑定共享状态，只读展示）"
            : "暂无轮询数据。请在设备管理 → MQTT 中开启「轮询查询」，并确保设备采集周期 > 0";
    }

    /// <summary>
    /// 从设备/物模型加载自定义按钮列表（仅 ShowOnDefinedPage）。
    /// </summary>
    /// <returns>表示加载完成的 Task。</returns>
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
    /// <param name="deviceId">设备 Id。</param>
    /// <param name="v">物模型变量。</param>
    /// <returns>按钮操作模型。</returns>
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

    /// <summary>
    /// 解析按钮显示名称（自定义页名 > 别名 > 地址）。
    /// </summary>
    /// <param name="v">物模型变量。</param>
    /// <returns>显示名称。</returns>
    private static string ResolveDisplayName(DeviceVariable v)
    {
        if (!string.IsNullOrWhiteSpace(v.DefinedPageDisplayName))
            return v.DefinedPageDisplayName.Trim();
        if (!string.IsNullOrWhiteSpace(v.Alias))
            return v.Alias.Trim();
        return v.Address;
    }

    /// <summary>
    /// 启动全部已启用摄像头的预览。
    /// </summary>
    /// <returns>表示启动完成的 Task。</returns>
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

    /// <summary>
    /// 根据预览槽数量更新网格列数。
    /// </summary>
    private void RefreshPreviewLayout()
        => PreviewGridColumns = PreviewSlots.Count <= 1 ? 1 : 2;

    /// <summary>
    /// 停止全部预览（阻塞等待锁）。
    /// </summary>
    /// <returns>表示停止完成的 Task。</returns>
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

    /// <summary>
    /// 停止并释放全部预览槽（须由持有 _previewGate 的调用方触发）。
    /// </summary>
    /// <returns>表示释放完成的 Task。</returns>
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
                    /* 单个槽释放失败不影响其余 */
                }
            }
        });
    }

    /// <summary>
    /// 执行非点动按钮（读或写默认值）。
    /// </summary>
    /// <param name="action">目标按钮模型。</param>
    /// <returns>表示执行完成的 Task。</returns>
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

    /// <summary>
    /// 定义页 Bool 按下：写入 true（由 View 指针事件调用）。
    /// </summary>
    /// <param name="action">目标按钮模型。</param>
    /// <returns>表示写入完成的 Task。</returns>
    public Task PressBoolAsync(DefinedVariableAction? action)
        => WriteMomentaryAsync(action, pressed: true);

    /// <summary>
    /// 定义页 Bool 松开 / 失焦：写入 false。
    /// </summary>
    /// <param name="action">目标按钮模型。</param>
    /// <returns>表示写入完成的 Task。</returns>
    public Task ReleaseBoolAsync(DefinedVariableAction? action)
        => WriteMomentaryAsync(action, pressed: false);

    /// <summary>
    /// 点动写：同一变量串行，保证先 true 后 false。
    /// </summary>
    /// <param name="action">目标按钮模型。</param>
    /// <param name="pressed">true=按下，false=松开。</param>
    /// <returns>表示写入完成的 Task。</returns>
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

    /// <summary>
    /// 获取或创建指定变量的点动串行锁。
    /// </summary>
    /// <param name="variableId">物模型变量 Id。</param>
    /// <returns>该变量专用的 SemaphoreSlim。</returns>
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

    /// <summary>
    /// 格式化调试返回值用于状态栏显示。
    /// </summary>
    /// <param name="value">原始值。</param>
    /// <returns>可读字符串。</returns>
    private static string FormatValue(object? value)
        => value switch
        {
            null => "(null)",
            string s => s,
            bool b => b ? "true" : "false",
            _ => value.ToString() ?? string.Empty,
        };

    /// <summary>
    /// 释放预览、LiveState 订阅及点动锁。
    /// </summary>
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
                /* 释放异常时忽略 */
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
