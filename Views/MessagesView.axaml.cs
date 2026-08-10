using Avalonia.Controls;

namespace Lana.Views;

/// <summary>
/// 消息通知页面视图。
/// <para>以列表形式展示系统消息、告警与未读标记，绑定 <c>MessagesViewModel</c> 的消息集合。</para>
/// <para>布局见 <c>MessagesView.axaml</c>，本文件无额外 code-behind 逻辑。</para>
/// </summary>
public partial class MessagesView : UserControl
{
    /// <summary>
    /// 初始化消息通知页面组件。
    /// </summary>
    public MessagesView()
    {
        InitializeComponent();
    }
}
