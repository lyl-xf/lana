using Avalonia.Controls;
using Avalonia.Input;
using Lana.ViewModels;

namespace Lana.Views;

/// <summary>
/// 设置页面视图。
/// <para>提供账号信息展示、修改密码、主题切换（Aurora / Snow）等功能，绑定 <c>SettingsViewModel</c>。</para>
/// <para>主题卡片使用 Border + PointerPressed 而非 Button，以便自定义视觉样式。</para>
/// </summary>
public partial class SettingsView : UserControl
{
    /// <summary>
    /// 初始化设置页面组件。
    /// </summary>
    public SettingsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 点击 Aurora 主题卡片时切换为 Aurora 配色方案。
    /// </summary>
    /// <param name="sender">被点击的主题 Border。</param>
    /// <param name="e">指针按下事件参数。</param>
    private void OnAuroraThemePressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            // 通过 ViewModel 命令持久化主题并刷新资源字典
            vm.SelectAuroraThemeCommand.Execute(null);
        }
    }

    /// <summary>
    /// 点击 Snow 主题卡片时切换为 Snow 配色方案。
    /// </summary>
    /// <param name="sender">被点击的主题 Border。</param>
    /// <param name="e">指针按下事件参数。</param>
    private void OnSnowThemePressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            // 通过 ViewModel 命令持久化主题并刷新资源字典
            vm.SelectSnowThemeCommand.Execute(null);
        }
    }
}
