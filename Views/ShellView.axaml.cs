using Avalonia.Controls;

namespace Lana.Views;

/// <summary>
/// 主壳视图：侧栏导航与内容区容器。
/// <para>左侧导航栏绑定 <c>ShellViewModel</c> 的 Navigate* 命令，右侧 ContentControl 承载各功能页。</para>
/// <para>布局与路由切换见 <c>ShellView.axaml</c>，本文件无额外 code-behind 逻辑。</para>
/// </summary>
public partial class ShellView : UserControl
{
    /// <summary>
    /// 初始化主壳组件（侧栏 + 内容区）。
    /// </summary>
    public ShellView()
    {
        InitializeComponent();
    }
}
