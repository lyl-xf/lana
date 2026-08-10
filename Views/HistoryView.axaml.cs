using Avalonia.Controls;

namespace Lana.Views;

/// <summary>
/// 历史记录页面视图。
/// <para>展示操作日志、告警与状态变更等历史条目，支持筛选与分页，绑定 <c>HistoryViewModel</c>。</para>
/// <para>底部状态栏显示加载进度与错误信息，布局见 <c>HistoryView.axaml</c>。</para>
/// </summary>
public partial class HistoryView : UserControl
{
    /// <summary>
    /// 初始化历史记录页面组件。
    /// </summary>
    public HistoryView()
    {
        InitializeComponent();
    }
}
