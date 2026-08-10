using Avalonia.Controls;

namespace Lana.Views;

/// <summary>
/// 关于页面视图。
/// <para>展示应用版本、运行时、UI 框架、数据库与视频引擎等元信息，以及开源许可与第三方组件声明。</para>
/// <para>布局与数据绑定见 <c>AboutView.axaml</c>，ViewModel 为 <c>AboutViewModel</c>。</para>
/// </summary>
public partial class AboutView : UserControl
{
    /// <summary>
    /// 初始化关于页面组件。
    /// </summary>
    public AboutView()
    {
        InitializeComponent();
    }
}
