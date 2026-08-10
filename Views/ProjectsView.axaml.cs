using Avalonia.Controls;

namespace Lana.Views;

/// <summary>
/// 项目管理页面视图。
/// <para>展示项目卡片列表及进度、成员等摘要信息，绑定 <c>ProjectsViewModel</c>。</para>
/// <para>布局见 <c>ProjectsView.axaml</c>，本文件无额外 code-behind 逻辑。</para>
/// </summary>
public partial class ProjectsView : UserControl
{
    /// <summary>
    /// 初始化项目管理页面组件。
    /// </summary>
    public ProjectsView()
    {
        InitializeComponent();
    }
}
