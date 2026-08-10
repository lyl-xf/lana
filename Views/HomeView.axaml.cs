using Avalonia.Controls;

namespace Lana.Views;

/// <summary>
/// 首页仪表盘视图。
/// <para>展示欢迎横幅、今日焦点、快捷入口与概览卡片，绑定 <c>HomeViewModel</c>。</para>
/// <para>作为 Shell 导航的默认 landing 页，布局见 <c>HomeView.axaml</c>。</para>
/// </summary>
public partial class HomeView : UserControl
{
    /// <summary>
    /// 初始化首页仪表盘组件。
    /// </summary>
    public HomeView()
    {
        InitializeComponent();
    }
}
