using Lana.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lana.ViewModels;

/// <summary>首页欢迎与概览卡片（展示用，可按需改成真实统计）。</summary>
public partial class HomeViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _welcomeTitle = "欢迎回来";

    [ObservableProperty]
    private string _welcomeSubtitle = string.Empty;

    [ObservableProperty]
    private string _todayFocus = "跨平台桌面体验已就绪，从这里开始构建你的产品。";

    public IReadOnlyList<StatCard> Stats { get; }

    public IReadOnlyList<ActivityItem> Activities { get; }

    public HomeViewModel(AppUser user)
    {
        WelcomeTitle = $"你好，{user.DisplayName}";
        WelcomeSubtitle = $"{user.Role} · Lana 工作台";

        Stats =
        [
            new("平台覆盖", "3+", "Windows / Linux / macOS"),
            new("数据层", "SQLite", "本地持久化"),
            new("主题风格", "双主题", "Aurora / Snow"),
            new("账号状态", "在线", user.Username),
        ];

        Activities =
        [
            new("系统", "已完成 SQLite 身份认证", "刚刚"),
            new("工作台", "首页模块加载完成", "1 分钟前"),
            new("设置", "偏好可写入本地数据库", "随时"),
        ];
    }
}

public sealed record StatCard(string Title, string Value, string Hint);

public sealed record ActivityItem(string Category, string Title, string Time);
