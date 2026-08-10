using Avalonia.Controls;
using Avalonia.Threading;
using Lana.ViewModels;
using LibVLCSharp.Avalonia;

namespace Lana.Views.Controls;

/// <summary>
/// 摄像头预览瓦片控件。
/// <para>绑定 <see cref="CameraPreviewSlot"/>，将 LibVLC 的 <c>MediaPlayer</c> 挂载到 <c>VideoView</c>。</para>
/// <para>在 DataContext 变更或 Detaching 时先解绑 VideoView 再停播，避免 native 资源泄漏。</para>
/// </summary>
public partial class CameraVideoTile : UserControl
{
    /// <summary>当前已订阅 Detaching 事件的预览槽位。</summary>
    private CameraPreviewSlot? _boundSlot;

    /// <summary>
    /// 初始化预览瓦片，注册 DataContext 变更与视觉树分离事件。
    /// </summary>
    public CameraVideoTile()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        // 控件从可视树移除时立即解绑播放器，防止后台继续渲染
        DetachedFromVisualTree += (_, _) => DetachPlayer();
    }

    /// <summary>
    /// DataContext 切换为新 <see cref="CameraPreviewSlot"/> 时重新订阅并挂载播放器。
    /// </summary>
    /// <param name="sender">本控件。</param>
    /// <param name="e">事件参数。</param>
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // 取消旧槽位的 Detaching 订阅，避免重复回调
        if (_boundSlot is not null)
            _boundSlot.Detaching -= OnSlotDetaching;

        _boundSlot = DataContext as CameraPreviewSlot;
        if (_boundSlot is not null)
            _boundSlot.Detaching += OnSlotDetaching;

        // 推迟到 UI 线程下一帧再挂载，确保 VideoHost 已完成布局
        Dispatcher.UIThread.Post(AttachPlayer);
    }

    /// <summary>
    /// 槽位即将销毁时的回调：提前解绑 VideoView，避免 native 层访问已释放的句柄。
    /// </summary>
    private void OnSlotDetaching()
        => DetachPlayer();

    /// <summary>
    /// 将当前槽位的 MediaPlayer 绑定到 VideoView，或在没有有效槽位时清空。
    /// </summary>
    private void AttachPlayer()
    {
        if (VideoHost is null)
            return;

        if (DataContext is CameraPreviewSlot slot)
            VideoHost.MediaPlayer = slot.Player;
        else
            // 无有效 DataContext 时断开渲染目标
            VideoHost.MediaPlayer = null;
    }

    /// <summary>
    /// 解除 VideoView 与 MediaPlayer 的绑定，停止向该控件输出视频帧。
    /// </summary>
    private void DetachPlayer()
    {
        if (VideoHost is not null)
            VideoHost.MediaPlayer = null;
    }
}
