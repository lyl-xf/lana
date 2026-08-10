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
    /// <summary>摄像头 Id。</summary>
    public long Id { get; init; }

    /// <summary>摄像头名称。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>来源摘要（RTSP URL 或本机设备名）。</summary>
    public string SourceSummary { get; init; } = string.Empty;

    /// <summary>是否已启用。</summary>
    public bool IsEnabled { get; init; }

    /// <summary>排序序号。</summary>
    public int SortOrder { get; init; }

    /// <summary>原始摄像头实体。</summary>
    public Camera Source { get; init; } = null!;

    /// <summary>是否勾选用于预览。</summary>
    [ObservableProperty]
    private bool _isSelectedForPreview;
}

/// <summary>
/// 单路预览槽：持有 MediaPlayer。停止前务必先触发 Detaching，让 VideoView 解绑，避免冻结。
/// 定义页与摄像头管理页共用本类型。
/// </summary>
public partial class CameraPreviewSlot : ObservableObject, IDisposable
{
    /// <summary>当前播放的 Media 实例。</summary>
    private Media? _media;

    /// <summary>是否已释放。</summary>
    private bool _disposed;

    /// <summary>播放/停止操作的线程锁。</summary>
    private readonly object _gate = new();

    /// <summary>关联的摄像头 Id。</summary>
    public long CameraId { get; }

    /// <summary>摄像头显示名称。</summary>
    public string Name { get; }

    /// <summary>LibVLC 媒体播放器实例。</summary>
    public MediaPlayer Player { get; }

    /// <summary>在停止/释放原生播放器之前触发，供 VideoView 先解绑。</summary>
    public event Action? Detaching;

    /// <summary>播放状态文案。</summary>
    [ObservableProperty]
    private string _status = "未播放";

    /// <summary>是否正在播放。</summary>
    [ObservableProperty]
    private bool _isPlaying;

    /// <summary>
    /// 构造预览槽并订阅播放器事件。
    /// </summary>
    /// <param name="cameraId">摄像头 Id。</param>
    /// <param name="name">显示名称。</param>
    /// <param name="player">LibVLC 播放器实例。</param>
    public CameraPreviewSlot(long cameraId, string name, MediaPlayer player)
    {
        CameraId = cameraId;
        Name = name;
        Player = player;
        Player.Playing += OnPlaying;
        Player.EncounteredError += OnEncounteredError;
        Player.Stopped += OnStopped;
    }

    /// <summary>
    /// 播放器进入播放状态时更新 UI。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnPlaying(object? sender, EventArgs e)
        => PostUi(() =>
        {
            IsPlaying = true;
            Status = "播放中";
        });

    /// <summary>
    /// 播放器遇到错误时更新 UI。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnEncounteredError(object? sender, EventArgs e)
        => PostUi(() =>
        {
            IsPlaying = false;
            Status = "播放失败";
        });

    /// <summary>
    /// 播放器停止时更新 UI。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnStopped(object? sender, EventArgs e)
        => PostUi(() =>
        {
            IsPlaying = false;
            Status = "已停止";
        });

    /// <summary>
    /// 将 Action 投递到 UI 线程执行。
    /// </summary>
    /// <param name="action">待执行的 UI 更新。</param>
    private static void PostUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    /// <summary>
    /// 通知 View 解绑 VideoView（停止/释放前调用）。
    /// </summary>
    public void NotifyDetaching()
    {
        try
        {
            Detaching?.Invoke();
        }
        catch
        {
            /* 订阅者异常不影响释放流程 */
        }
    }

    /// <summary>
    /// 异步开始播放指定摄像头的 RTSP 流。
    /// </summary>
    /// <param name="request">播放请求（URL、凭据等）。</param>
    /// <returns>表示播放启动完成的 Task。</returns>
    public Task PlayAsync(CameraPlayRequest request)
        => Task.Run(() =>
        {
            lock (_gate)
            {
                if (_disposed)
                    return;

                // 先停止旧流再创建新 Media
                StopInternalUnlocked();
                var host = LibVlcHost.Instance;
                _media = host.CreateMedia(request);
                Player.Play(_media);
            }

            PostUi(() => Status = "连接中…");
        });

    /// <summary>
    /// 异步停止当前播放。
    /// </summary>
    /// <returns>表示停止完成的 Task。</returns>
    public Task StopAsync()
        => Task.Run(() =>
        {
            lock (_gate)
            {
                StopInternalUnlocked();
            }
        });

    /// <summary>
    /// 在已持有锁的情况下停止播放并释放 Media（调用方须持有 _gate）。
    /// </summary>
    private void StopInternalUnlocked()
    {
        try
        {
            Player.Stop();
        }
        catch
        {
            /* 已停止或原生层异常时忽略 */
        }

        try
        {
            _media?.Dispose();
        }
        catch
        {
            /* Media 释放异常时忽略 */
        }

        _media = null;
        PostUi(() =>
        {
            IsPlaying = false;
            Status = "已停止";
        });
    }

    /// <summary>
    /// 释放播放器、Media 及事件订阅。
    /// </summary>
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
            /* 取消订阅异常时忽略 */
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
                /* 原生播放器释放异常时忽略 */
            }
        }
    }
}

/// <summary>
/// 摄像头管理（Admin）：CRUD + 多选预览。离开页面时 Shell 会调用 StopPreviewsIfAny。
/// </summary>
public partial class CamerasViewModel : ViewModelBase, IDisposable
{
    /// <summary>摄像头 CRUD 服务。</summary>
    private readonly CameraService _service;

    /// <summary>预览启停串行化信号量。</summary>
    private readonly SemaphoreSlim _previewGate = new(1, 1);

    /// <summary>当前编辑是否为新建模式。</summary>
    private bool _isNew;

    /// <summary>是否已释放。</summary>
    private bool _disposed;

    /// <summary>刷新列表前记住的预览勾选 Id 集合。</summary>
    private HashSet<long> _previewSelectionIds = [];

    /// <summary>
    /// 构造摄像头管理页 VM，并异步加载列表与初始化 LibVLC。
    /// </summary>
    /// <param name="service">摄像头服务。</param>
    public CamerasViewModel(CameraService service)
    {
        _service = service;
        _ = RefreshCamerasAsync();
        _ = LibVlcHost.EnsureInitializedAsync();
    }

    /// <summary>当前 Tab 索引（列表/预览）。</summary>
    [ObservableProperty]
    private int _selectedTabIndex;

    /// <summary>底部状态栏提示信息。</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>是否正在执行异步操作。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>搜索关键字。</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>摄像头列表项集合。</summary>
    public ObservableCollection<CameraListItem> CameraListItems { get; } = [];

    /// <summary>当前选中的列表项。</summary>
    [ObservableProperty]
    private CameraListItem? _selectedCamera;

    /// <summary>是否处于编辑面板。</summary>
    [ObservableProperty]
    private bool _isEditing;

    /// <summary>编辑中的摄像头 Id（新建时为 0）。</summary>
    [ObservableProperty]
    private long _editId;

    /// <summary>编辑：名称。</summary>
    [ObservableProperty]
    private string _editName = string.Empty;

    /// <summary>编辑：RTSP URL。</summary>
    [ObservableProperty]
    private string _editRtspUrl = string.Empty;

    /// <summary>编辑：用户名。</summary>
    [ObservableProperty]
    private string _editUsername = string.Empty;

    /// <summary>编辑：密码。</summary>
    [ObservableProperty]
    private string _editPassword = string.Empty;

    /// <summary>编辑：描述。</summary>
    [ObservableProperty]
    private string _editDescription = string.Empty;

    /// <summary>编辑：排序序号。</summary>
    [ObservableProperty]
    private int _editSortOrder;

    /// <summary>编辑：是否启用。</summary>
    [ObservableProperty]
    private bool _editIsEnabled = true;

    /// <summary>预览槽位集合。</summary>
    public ObservableCollection<CameraPreviewSlot> PreviewSlots { get; } = [];

    /// <summary>单路全宽；两路及以上两列铺满。</summary>
    [ObservableProperty]
    private int _previewGridColumns = 1;

    /// <summary>当前勾选预览的已启用摄像头数量。</summary>
    public int SelectedPreviewCount => CameraListItems.Count(x => x.IsSelectedForPreview && x.IsEnabled);

    /// <summary>
    /// 根据预览槽数量更新网格列数。
    /// </summary>
    private void RefreshPreviewLayout()
        => PreviewGridColumns = PreviewSlots.Count <= 1 ? 1 : 2;

    /// <summary>
    /// 离开预览 Tab 时自动停止全部预览。
    /// </summary>
    /// <param name="value">新 Tab 索引。</param>
    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value != 1)
            _ = StopAllPreviewsAsync();
    }

    /// <summary>
    /// 搜索文本变更时重新加载列表。
    /// </summary>
    /// <param name="value">新搜索文本。</param>
    partial void OnSearchTextChanged(string value)
        => _ = RefreshCamerasAsync();

    /// <summary>
    /// 刷新摄像头列表（带忙碌状态与异常处理）。
    /// </summary>
    /// <returns>表示刷新完成的 Task。</returns>
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

    /// <summary>
    /// 从服务加载摄像头列表并恢复预览勾选状态。
    /// </summary>
    /// <returns>表示加载完成的 Task。</returns>
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

    /// <summary>
    /// 将当前预览勾选 Id 保存到内存，供刷新后恢复。
    /// </summary>
    private void RememberPreviewSelection()
    {
        _previewSelectionIds = CameraListItems
            .Where(x => x.IsSelectedForPreview)
            .Select(x => x.Id)
            .ToHashSet();
    }

    /// <summary>
    /// 进入新建摄像头编辑面板。
    /// </summary>
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

    /// <summary>
    /// 进入编辑当前选中摄像头。
    /// </summary>
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

    /// <summary>
    /// 格式化摄像头来源摘要文本。
    /// </summary>
    /// <param name="c">摄像头实体。</param>
    /// <returns>来源摘要字符串。</returns>
    private static string FormatSourceSummary(Camera c)
        => c.SourceType == CameraSourceType.Local
            ? $"本机/USB（不受支持）· {c.LocalDeviceName}"
            : c.RtspUrl;

    /// <summary>
    /// 取消编辑并关闭编辑面板。
    /// </summary>
    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        StatusMessage = "已取消编辑";
    }

    /// <summary>
    /// 保存新建或编辑的摄像头。
    /// </summary>
    /// <returns>表示保存完成的 Task。</returns>
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

    /// <summary>
    /// 删除当前选中的摄像头。
    /// </summary>
    /// <returns>表示删除完成的 Task。</returns>
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
            // 若当前在预览 Tab，删除后重新启动预览
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

    /// <summary>
    /// 切换当前选中摄像头的启用状态。
    /// </summary>
    /// <returns>表示切换完成的 Task。</returns>
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

    /// <summary>
    /// 切换指定列表项的预览勾选状态。
    /// </summary>
    /// <param name="item">目标列表项。</param>
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

    /// <summary>
    /// 勾选全部已启用摄像头用于预览。
    /// </summary>
    [RelayCommand]
    private void SelectAllEnabledForPreview()
    {
        foreach (var item in CameraListItems.Where(x => x.IsEnabled))
            item.IsSelectedForPreview = true;
        RememberPreviewSelection();
        OnPropertyChanged(nameof(SelectedPreviewCount));
        StatusMessage = $"已勾选 {SelectedPreviewCount} 路已启用摄像头";
    }

    /// <summary>
    /// 清除全部预览勾选。
    /// </summary>
    [RelayCommand]
    private void ClearPreviewSelection()
    {
        foreach (var item in CameraListItems)
            item.IsSelectedForPreview = false;
        RememberPreviewSelection();
        OnPropertyChanged(nameof(SelectedPreviewCount));
        StatusMessage = "已清除预览勾选";
    }

    /// <summary>
    /// 刷新预览（重新启动全部勾选摄像头）。
    /// </summary>
    /// <returns>表示预览启动完成的 Task。</returns>
    [RelayCommand]
    private async Task RefreshPreviewAsync()
        => await StartPreviewAsync();

    /// <summary>
    /// 停止全部预览。
    /// </summary>
    /// <returns>表示停止完成的 Task。</returns>
    [RelayCommand]
    private async Task StopPreviewAsync()
    {
        await StopAllPreviewsAsync();
        StatusMessage = "已停止全部预览";
    }

    /// <summary>
    /// 供 Shell 离开页面时调用：若有预览则停止。
    /// </summary>
    public void StopPreviewsIfAny()
        => _ = StopAllPreviewsAsync();

    /// <summary>
    /// 启动全部勾选摄像头的预览（非阻塞获取锁）。
    /// </summary>
    /// <returns>表示预览流程完成的 Task。</returns>
    private async Task StartPreviewAsync()
    {
        // 非阻塞获取锁：已有预览操作进行中则提示用户
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

            // 先清空旧预览再并行启动新流
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
                    /* 单个槽释放失败不影响其余 */
                }
            }
        });
    }

    /// <summary>
    /// 释放预览资源与信号量。
    /// </summary>
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
                /* 释放异常时忽略 */
            }
        }

        _previewGate.Dispose();
    }
}
