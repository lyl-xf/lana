using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lana.Cameras.Models;
using Lana.Cameras.Services;
using LibVLCSharp.Shared;

namespace Lana.ViewModels;

/// <summary>摄像头管理列表项；IsSelectedForPreview 控制右侧预览区。</summary>
public partial class CameraListItem : ObservableObject
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string SourceSummary { get; init; } = string.Empty;
    public bool IsEnabled { get; init; }
    public int SortOrder { get; init; }
    public Camera Source { get; init; } = null!;

    [ObservableProperty]
    private bool _isSelectedForPreview;
}

/// <summary>
/// 单路预览槽：持有 MediaPlayer。停止前务必先触发 Detaching，让 VideoView 解绑，避免冻结。
/// 定义页与摄像头管理页共用本类型。
/// </summary>
public partial class CameraPreviewSlot : ObservableObject, IDisposable
{
    private Media? _media;
    private bool _disposed;
    private readonly object _gate = new();

    public long CameraId { get; }
    public string Name { get; }
    public MediaPlayer Player { get; }

    /// <summary>在停止/释放原生播放器之前触发，供 VideoView 先解绑。</summary>
    public event Action? Detaching;

    [ObservableProperty]
    private string _status = "未播放";

    [ObservableProperty]
    private bool _isPlaying;

    public CameraPreviewSlot(long cameraId, string name, MediaPlayer player)
    {
        CameraId = cameraId;
        Name = name;
        Player = player;
        Player.Playing += OnPlaying;
        Player.EncounteredError += OnEncounteredError;
        Player.Stopped += OnStopped;
    }

    private void OnPlaying(object? sender, EventArgs e)
        => PostUi(() =>
        {
            IsPlaying = true;
            Status = "播放中";
        });

    private void OnEncounteredError(object? sender, EventArgs e)
        => PostUi(() =>
        {
            IsPlaying = false;
            Status = "播放失败";
        });

    private void OnStopped(object? sender, EventArgs e)
        => PostUi(() =>
        {
            IsPlaying = false;
            Status = "已停止";
        });

    private static void PostUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    public void NotifyDetaching()
    {
        try
        {
            Detaching?.Invoke();
        }
        catch
        {
            /* ignore */
        }
    }

    public Task PlayAsync(CameraPlayRequest request)
        => Task.Run(() =>
        {
            lock (_gate)
            {
                if (_disposed)
                    return;

                StopInternalUnlocked();
                var host = LibVlcHost.Instance;
                _media = host.CreateMedia(request);
                Player.Play(_media);
            }

            PostUi(() => Status = "连接中…");
        });

    public Task StopAsync()
        => Task.Run(() =>
        {
            lock (_gate)
            {
                StopInternalUnlocked();
            }
        });

    private void StopInternalUnlocked()
    {
        try
        {
            Player.Stop();
        }
        catch
        {
            /* ignore */
        }

        try
        {
            _media?.Dispose();
        }
        catch
        {
            /* ignore */
        }

        _media = null;
        PostUi(() =>
        {
            IsPlaying = false;
            Status = "已停止";
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            Player.Playing -= OnPlaying;
            Player.EncounteredError -= OnEncounteredError;
            Player.Stopped -= OnStopped;
        }
        catch
        {
            /* ignore */
        }

        lock (_gate)
        {
            StopInternalUnlocked();
            try
            {
                Player.Dispose();
            }
            catch
            {
                /* ignore */
            }
        }
    }
}

/// <summary>
/// 摄像头管理（Admin）：CRUD + 多选预览。离开页面时 Shell 会调用 StopPreviewsIfAny。
/// </summary>
public partial class CamerasViewModel : ViewModelBase, IDisposable
{
    private readonly CameraService _service;
    private readonly SemaphoreSlim _previewGate = new(1, 1);
    private bool _isNew;
    private bool _disposed;
    private HashSet<long> _previewSelectionIds = [];

    public CamerasViewModel(CameraService service)
    {
        _service = service;
        _ = RefreshCamerasAsync();
        _ = LibVlcHost.EnsureInitializedAsync();
    }

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _searchText = string.Empty;

    public ObservableCollection<CameraListItem> CameraListItems { get; } = [];

    [ObservableProperty]
    private CameraListItem? _selectedCamera;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private long _editId;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _editRtspUrl = string.Empty;

    [ObservableProperty]
    private string _editUsername = string.Empty;

    [ObservableProperty]
    private string _editPassword = string.Empty;

    [ObservableProperty]
    private string _editDescription = string.Empty;

    [ObservableProperty]
    private int _editSortOrder;

    [ObservableProperty]
    private bool _editIsEnabled = true;

    public ObservableCollection<CameraPreviewSlot> PreviewSlots { get; } = [];

    /// <summary>单路全宽；两路及以上两列铺满。</summary>
    [ObservableProperty]
    private int _previewGridColumns = 1;

    public int SelectedPreviewCount => CameraListItems.Count(x => x.IsSelectedForPreview && x.IsEnabled);

    private void RefreshPreviewLayout()
        => PreviewGridColumns = PreviewSlots.Count <= 1 ? 1 : 2;

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value != 1)
            _ = StopAllPreviewsAsync();
    }

    partial void OnSearchTextChanged(string value)
        => _ = RefreshCamerasAsync();

    [RelayCommand]
    private async Task RefreshCamerasAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            await RefreshCamerasCoreAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = "加载摄像头失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshCamerasCoreAsync()
    {
        RememberPreviewSelection();
        var name = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();
        var list = await _service.ListAsync(name);
        CameraListItems.Clear();
        foreach (var c in list.OrderBy(x => x.SortOrder).ThenBy(x => x.Id))
        {
            CameraListItems.Add(new CameraListItem
            {
                Id = c.Id,
                Name = c.Name,
                SourceSummary = FormatSourceSummary(c),
                IsEnabled = c.IsEnabled,
                SortOrder = c.SortOrder,
                Source = c,
                IsSelectedForPreview = c.IsEnabled && _previewSelectionIds.Contains(c.Id),
            });
        }

        StatusMessage = $"已加载 {CameraListItems.Count} 路摄像头";
        OnPropertyChanged(nameof(SelectedPreviewCount));
    }

    private void RememberPreviewSelection()
    {
        _previewSelectionIds = CameraListItems
            .Where(x => x.IsSelectedForPreview)
            .Select(x => x.Id)
            .ToHashSet();
    }

    [RelayCommand]
    private void NewCamera()
    {
        _isNew = true;
        EditId = 0;
        EditName = string.Empty;
        EditRtspUrl = "rtsp://";
        EditUsername = string.Empty;
        EditPassword = string.Empty;
        EditDescription = string.Empty;
        EditSortOrder = CameraListItems.Count == 0 ? 0 : CameraListItems.Max(x => x.SortOrder) + 1;
        EditIsEnabled = true;
        IsEditing = true;
        StatusMessage = "新建摄像头";
    }

    [RelayCommand]
    private void EditSelectedCamera()
    {
        if (SelectedCamera is null)
        {
            StatusMessage = "请先选择摄像头";
            return;
        }

        var c = SelectedCamera.Source;
        _isNew = false;
        EditId = c.Id;
        EditName = c.Name;
        EditRtspUrl = string.IsNullOrWhiteSpace(c.RtspUrl) ? "rtsp://" : c.RtspUrl;
        EditUsername = c.Username;
        EditPassword = c.Password;
        EditDescription = c.Description;
        EditSortOrder = c.SortOrder;
        EditIsEnabled = c.IsEnabled;
        IsEditing = true;
        StatusMessage = $"编辑摄像头 #{c.Id}";
    }

    private static string FormatSourceSummary(Camera c)
        => c.SourceType == CameraSourceType.Local
            ? $"本机/USB（不受支持）· {c.LocalDeviceName}"
            : c.RtspUrl;

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        StatusMessage = "已取消编辑";
    }

    [RelayCommand]
    private async Task SaveCameraAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            var camera = new Camera
            {
                Id = EditId,
                Name = EditName,
                SourceType = CameraSourceType.Network,
                RtspUrl = EditRtspUrl,
                LocalDeviceName = string.Empty,
                Username = EditUsername,
                Password = EditPassword,
                Description = EditDescription,
                SortOrder = EditSortOrder,
                IsEnabled = EditIsEnabled,
            };

            if (_isNew)
            {
                await _service.CreateAsync(camera);
                StatusMessage = $"已创建摄像头 #{camera.Id}";
            }
            else
            {
                await _service.UpdateAsync(camera);
                StatusMessage = $"已更新摄像头 #{camera.Id}";
            }

            IsEditing = false;
            await RefreshCamerasCoreAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = "保存失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedCameraAsync()
    {
        if (SelectedCamera is null)
        {
            StatusMessage = "请先选择摄像头";
            return;
        }

        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            var id = SelectedCamera.Id;
            await _service.DeleteAsync(id);
            IsEditing = false;
            SelectedCamera = null;
            _previewSelectionIds.Remove(id);
            StatusMessage = $"已删除摄像头 #{id}";
            await RefreshCamerasCoreAsync();
            if (SelectedTabIndex == 1)
                await StartPreviewAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = "删除失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ToggleSelectedEnabledAsync()
    {
        if (SelectedCamera is null)
        {
            StatusMessage = "请先选择摄像头";
            return;
        }

        try
        {
            IsBusy = true;
            var c = SelectedCamera.Source;
            c.IsEnabled = !c.IsEnabled;
            await _service.UpdateAsync(c);
            StatusMessage = c.IsEnabled ? $"已开启 #{c.Id}" : $"已关闭 #{c.Id}";
            await RefreshCamerasCoreAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = "切换失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void TogglePreviewPick(CameraListItem? item)
    {
        if (item is null)
            return;
        if (!item.IsEnabled)
        {
            StatusMessage = $"摄像头 #{item.Id} 未启用，请先在配置中开启";
            return;
        }

        item.IsSelectedForPreview = !item.IsSelectedForPreview;
        RememberPreviewSelection();
        OnPropertyChanged(nameof(SelectedPreviewCount));
        StatusMessage = item.IsSelectedForPreview
            ? $"已勾选预览：{item.Name}"
            : $"已取消预览：{item.Name}";
    }

    [RelayCommand]
    private void SelectAllEnabledForPreview()
    {
        foreach (var item in CameraListItems.Where(x => x.IsEnabled))
            item.IsSelectedForPreview = true;
        RememberPreviewSelection();
        OnPropertyChanged(nameof(SelectedPreviewCount));
        StatusMessage = $"已勾选 {SelectedPreviewCount} 路已启用摄像头";
    }

    [RelayCommand]
    private void ClearPreviewSelection()
    {
        foreach (var item in CameraListItems)
            item.IsSelectedForPreview = false;
        RememberPreviewSelection();
        OnPropertyChanged(nameof(SelectedPreviewCount));
        StatusMessage = "已清除预览勾选";
    }

    [RelayCommand]
    private async Task RefreshPreviewAsync()
        => await StartPreviewAsync();

    [RelayCommand]
    private async Task StopPreviewAsync()
    {
        await StopAllPreviewsAsync();
        StatusMessage = "已停止全部预览";
    }

    public void StopPreviewsIfAny()
        => _ = StopAllPreviewsAsync();

    private async Task StartPreviewAsync()
    {
        if (!await _previewGate.WaitAsync(0))
        {
            StatusMessage = "预览操作进行中，请稍候…";
            return;
        }

        try
        {
            RememberPreviewSelection();
            var selected = CameraListItems
                .Where(x => x.IsSelectedForPreview && x.IsEnabled)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .ToList();

            if (selected.Count == 0)
            {
                await StopAllPreviewsCoreAsync();
                StatusMessage = "请先勾选要预览的已启用摄像头，再点击「开始预览」";
                return;
            }

            await StopAllPreviewsCoreAsync();
            await LibVlcHost.EnsureInitializedAsync();

            var host = LibVlcHost.Instance;
            var playTasks = new List<Task>();

            foreach (var item in selected)
            {
                var slot = new CameraPreviewSlot(item.Id, item.Name, host.CreatePlayer());
                PreviewSlots.Add(slot);
                playTasks.Add(slot.PlayAsync(CameraService.BuildPlayRequest(item.Source)));
            }

            RefreshPreviewLayout();
            StatusMessage = $"正在连接 {PreviewSlots.Count} 路预览…";
            await Task.WhenAll(playTasks);
            StatusMessage = $"正在预览 {PreviewSlots.Count} 路摄像头";
        }
        catch (Exception ex)
        {
            StatusMessage = "启动预览失败：" + ex.Message;
        }
        finally
        {
            _previewGate.Release();
        }
    }

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

        // 先从界面移除，触发 VideoView 解绑
        PreviewSlots.Clear();
        RefreshPreviewLayout();

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var slot in slots)
                slot.NotifyDetaching();
        });

        // 等一帧，确保 HWND/VideoView 解绑完成后再 Stop/Dispose
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

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        // 避免在 UI 线程上 GetResult 等待 Dispatcher（会死锁）
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
