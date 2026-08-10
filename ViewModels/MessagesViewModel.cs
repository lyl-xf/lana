namespace Lana.ViewModels;

/// <summary>遗留「消息」页占位，当前未挂到 Shell 导航。</summary>
public partial class MessagesViewModel : ViewModelBase
{
    public string Title => "消息";

    public string Subtitle => "系统通知与协作消息中心";

    public IReadOnlyList<MessageItem> Messages { get; } =
    [
        new("系统", "主题偏好已同步到 SQLite", "2 分钟前", true),
        new("安全", "admin 账号完成登录验证", "今天 10:20", true),
        new("发布", "linux-x64 发布配置已就绪", "昨天", false),
        new("协作", "欢迎体验 Lana 工作台", "本周", false),
    ];
}

public sealed record MessageItem(string Category, string Content, string Time, bool Unread);
