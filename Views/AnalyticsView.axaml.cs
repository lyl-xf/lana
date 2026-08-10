using Avalonia.Controls;

namespace Lana.Views;

/// <summary>
/// 数据分析页面视图。
/// <para>以指标卡片与图表形式展示业务统计数据，绑定 <c>AnalyticsViewModel</c> 的 Metrics 等集合。</para>
/// <para>具体布局见 <c>AnalyticsView.axaml</c>，本文件无额外 code-behind 逻辑。</para>
/// </summary>
public partial class AnalyticsView : UserControl
{
    /// <summary>
    /// 初始化数据分析页面组件。
    /// </summary>
    public AnalyticsView()
    {
        InitializeComponent();
    }
}
