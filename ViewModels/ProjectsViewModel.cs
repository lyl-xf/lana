using CommunityToolkit.Mvvm.ComponentModel;

namespace AvaloniaUse.ViewModels;

public partial class ProjectsViewModel : ViewModelBase
{
    public string Title => "项目";

    public string Subtitle => "管理跨平台应用相关项目与交付进度";

    public IReadOnlyList<ProjectItem> Projects { get; } =
    [
        new("AvaloniaUse Desktop", "进行中", "桌面端骨架与主题系统", "85%"),
        new("Auth Module", "已完成", "SQLite 登录认证与会话", "100%"),
        new("Publish Pipeline", "规划中", "Windows / Linux / macOS 发布脚本", "20%"),
        new("Plugin Hub", "待启动", "后续扩展插件市场能力", "0%"),
    ];
}

public sealed record ProjectItem(string Name, string Status, string Description, string Progress);
