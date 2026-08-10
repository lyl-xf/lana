using Lana.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lana.ViewModels;

/// <summary>首页欢迎与概览卡片（展示用，可按需改成真实统计）。</summary>
public partial class HomeViewModel : ViewModelBase
{
    /// <summary>欢迎标题（含用户显示名）。</summary>
    [ObservableProperty]
    private string _welcomeTitle = "欢迎回来";

    /// <summary>欢迎副标题（角色与工作台名称）。</summary>
    [ObservableProperty]
    private string _welcomeSubtitle = string.Empty;

    /// <summary>今日焦点提示文案。</summary>
    [ObservableProperty]
    private string _todayFocus = "跨平台桌面体验已就绪，从这里开始构建你的产品。";

    /// <summary>概览统计卡片列表。</summary>
    public IReadOnlyList<StatCard> Stats { get; }

    /// <summary>最近活动列表。</summary>
    public IReadOnlyList<ActivityItem> Activities { get; }

    /// <summary>
    /// 构造首页 ViewModel，根据当前用户填充欢迎语与静态卡片数据。
    /// </summary>
    /// <param name="user">已登录用户。</param>
    public HomeViewModel(AppUser user)
    {
        WelcomeTitle = $"你好，{user.DisplayName}";
        WelcomeSubtitle = $"{user.Role} · Lana 工作台";

        // 静态演示卡片，后续可替换为真实统计
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

/// <summary>首页统计卡片项。</summary>
/// <param name="Title">卡片标题。</param>
/// <param name="Value">主要数值。</param>
/// <param name="Hint">附加说明。</param>
public sealed record StatCard(string Title, string Value, string Hint);

/// <summary>首页活动动态项。</summary>
/// <param name="Category">活动分类。</param>
/// <param name="Title">活动标题。</param>
/// <param name="Time">时间描述。</param>
public sealed record ActivityItem(string Category, string Title, string Time);
