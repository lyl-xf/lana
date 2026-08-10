namespace Lana.ViewModels;

/// <summary>遗留「消息」页占位，当前未挂到 Shell 导航。</summary>
public partial class MessagesViewModel : ViewModelBase
{
    /// <summary>页面标题。</summary>
    public string Title => "消息";

    /// <summary>页面副标题。</summary>
    public string Subtitle => "系统通知与协作消息中心";

    /// <summary>消息列表（静态演示数据）。</summary>
    public IReadOnlyList<MessageItem> Messages { get; } =
    [
        new("系统", "主题偏好已同步到 SQLite", "2 分钟前", true),
        new("安全", "admin 账号完成登录验证", "今天 10:20", true),
        new("发布", "linux-x64 发布配置已就绪", "昨天", false),
        new("协作", "欢迎体验 Lana 工作台", "本周", false),
    ];
}

/// <summary>单条消息项。</summary>
/// <param name="Category">消息分类（系统/安全等）。</param>
/// <param name="Content">消息正文。</param>
/// <param name="Time">时间描述。</param>
/// <param name="Unread">是否未读。</param>
public sealed record MessageItem(string Category, string Content, string Time, bool Unread);
