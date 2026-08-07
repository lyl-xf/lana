namespace AvaloniaUse.ViewModels;

public partial class AboutViewModel : ViewModelBase
{
    public string Title => "关于";

    public string Subtitle => "AvaloniaUse 跨平台桌面应用骨架";

    public string Version => "1.0.0";

    public string Runtime => ".NET 9";

    public string UiFramework => "Avalonia 12";

    public string Database => "SQLite + Dapper";

    public IReadOnlyList<string> Features { get; } =
    [
        "认证登录与会话管理",
        "Aurora Night / Snow Light 双主题",
        "Dapper 轻量 SQL Mapper（类 MyBatis）",
        "本地 SQLite 持久化",
        "多菜单页面导航",
        "侧栏导航与收起展开",
        "Windows / Linux / macOS 跨平台发布",
    ];
}
