using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Lana.ViewModels;

namespace Lana.Views;

/// <summary>
/// 摄像头管理页面视图。
/// <para>包含「摄像头配置」与「摄像头预览」两个 Tab：前者用于 CRUD 与 RTSP 参数编辑，后者用于勾选待预览设备。</para>
/// <para>预览勾选行点击由 <see cref="OnPreviewPickPressed"/> 处理，避免与 CheckBox 重复切换。</para>
/// </summary>
public partial class CamerasView : UserControl
{
    /// <summary>
    /// 初始化摄像头管理页面组件。
    /// </summary>
    public CamerasView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 预览 Tab 中点击摄像头行时切换勾选状态。
    /// <para>CheckBox 自身已处理点击，此处需排除以避免双重切换。</para>
    /// </summary>
    /// <param name="sender">被点击的 Border 容器。</param>
    /// <param name="e">指针按下事件参数。</param>
    private void OnPreviewPickPressed(object? sender, PointerPressedEventArgs e)
    {
        // 勾选框自己处理，避免点 CheckBox 时再切换一次
        if (e.Source is CheckBox || (e.Source as Control)?.TemplatedParent is CheckBox)
            return;

        // 从 Border 的 DataContext 取出列表项
        if (sender is not Border { DataContext: CameraListItem item })
            return;
        // 页面 ViewModel 负责实际切换逻辑
        if (DataContext is not CamerasViewModel vm)
            return;

        if (vm.TogglePreviewPickCommand.CanExecute(item))
            vm.TogglePreviewPickCommand.Execute(item);

        // 阻止事件继续冒泡，避免重复触发
        e.Handled = true;
    }
}
