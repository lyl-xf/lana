using CommunityToolkit.Mvvm.ComponentModel;

namespace Lana.ViewModels;

/// <summary>遗留「项目」页占位，当前未挂到 Shell 导航。</summary>
public partial class ProjectsViewModel : ViewModelBase
{
    /// <summary>页面标题。</summary>
    public string Title => "项目";

    /// <summary>页面副标题。</summary>
    public string Subtitle => "管理跨平台应用相关项目与交付进度";

    /// <summary>项目列表（静态演示数据）。</summary>
    public IReadOnlyList<ProjectItem> Projects { get; } =
    [
        new("Lana Desktop", "进行中", "桌面端骨架与主题系统", "85%"),
        new("Auth Module", "已完成", "SQLite 登录认证与会话", "100%"),
        new("Publish Pipeline", "规划中", "Windows / Linux / macOS 发布脚本", "20%"),
        new("Plugin Hub", "待启动", "后续扩展插件市场能力", "0%"),
    ];
}

/// <summary>单个项目卡片项。</summary>
/// <param name="Name">项目名称。</param>
/// <param name="Status">当前状态。</param>
/// <param name="Description">项目描述。</param>
/// <param name="Progress">进度百分比文本。</param>
public sealed record ProjectItem(string Name, string Status, string Description, string Progress);
