using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lana.Cameras.Services;
using Lana.Gateway.Models;
using Lana.Gateway.Services;

namespace Lana.ViewModels;

public enum DefinedButtonKind
{
    Read,
    WriteTrue,
    WriteFalse,
    WriteValue,
}

public partial class DefinedVariableAction : ObservableObject
{
    public long VariableId { get; init; }
    public long DeviceId { get; init; }
    public string ButtonText { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
    public DataType DataType { get; init; }
    public DefinedButtonKind Kind { get; init; }
    public string WriteValue { get; init; } = string.Empty;

    [ObservableProperty]
    private bool _isBusy;
}

public partial class DefinedPageViewModel : ViewModelBase, IDisposable
{
    private readonly GatewayDeviceService _deviceService;
    private readonly IDeviceDebugApi _debugApi;
    private readonly CameraService _cameraService;
    private readonly SemaphoreSlim _previewGate = new(1, 1);
    private bool _disposed;

    public DefinedPageViewModel(
        GatewayDeviceService deviceService,
        IDeviceDebugApi debugApi,
        CameraService cameraService)
    {
        _deviceService = deviceService;
        _debugApi = debugApi;
        _cameraService = cameraService;
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

    public async Task OnEnteredAsync()
        => await RefreshAllAsync();

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
            StatusMessage = VariableActions.Count == 0
                ? "暂无自定义按钮。请在「设备管理 → 物模型」中开启「进入自定义页」。"
                : $"已加载 {VariableActions.Count} 个操作按钮 · 摄像头 {PreviewSlots.Count} 路";
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

    private async Task LoadCustomButtonsAsync()
    {
        VariableActions.Clear();
        var devices = await _deviceService.ListDevicesAsync();
        foreach (var device in devices.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.Id))
        {
            var vars = await _deviceService.ListVariablesAsync(device.Id);
            foreach (var v in vars.Where(x => x.ShowOnDefinedPage)
                         .OrderBy(x => x.Alias).ThenBy(x => x.Address).ThenBy(x => x.Id))
            {
                foreach (var action in CreateButtons(device.Id, v))
                    VariableActions.Add(action);
            }
        }
    }

    private static IEnumerable<DefinedVariableAction> CreateButtons(long deviceId, DeviceVariable v)
    {
        var title = string.IsNullOrWhiteSpace(v.Alias) ? v.Address : v.Alias;

        if (v.DefinedPageOperation == DefinedPageOperation.Write)
        {
            yield return Make(
                deviceId,
                v,
                title,
                DefinedButtonKind.WriteValue,
                v.DefinedPageWriteValue ?? string.Empty);
            yield break;
        }

        yield return Make(deviceId, v, title, DefinedButtonKind.Read, string.Empty);
    }

    private static DefinedVariableAction Make(
        long deviceId,
        DeviceVariable v,
        string text,
        DefinedButtonKind kind,
        string writeValue)
        => new()
        {
            VariableId = v.Id,
            DeviceId = deviceId,
            ButtonText = text,
            Address = v.Address,
            DataType = v.DataType,
            Kind = kind,
            WriteValue = writeValue,
        };

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
                playTasks.Add(slot.PlayAsync(CameraService.BuildPlayUrl(camera)));
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
        if (action is null || action.IsBusy)
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
                case DefinedButtonKind.WriteTrue:
                case DefinedButtonKind.WriteFalse:
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
    }
}
