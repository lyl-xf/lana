using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Lana.ViewModels;

/// <summary>开源组件说明项（关于页）。</summary>
public sealed class OpenSourceComponent
{
    /// <summary>组件名称。</summary>
    public required string Name { get; init; }

    /// <summary>许可证类型。</summary>
    public required string License { get; init; }

    /// <summary>源码仓库 URL。</summary>
    public required string SourceUrl { get; init; }

    /// <summary>使用说明备注。</summary>
    public required string Notes { get; init; }
}

/// <summary>关于页（含开源许可说明）；当前未挂到 Shell，可按需重新接入。</summary>
public partial class AboutViewModel : ViewModelBase
{
    /// <summary>页面标题。</summary>
    public string Title => "关于";

    /// <summary>应用副标题。</summary>
    public string Subtitle => "Lana 跨平台桌面应用";

    /// <summary>应用版本号。</summary>
    public string Version => "1.0.0";

    /// <summary>运行时版本。</summary>
    public string Runtime => ".NET 9";

    /// <summary>UI 框架名称。</summary>
    public string UiFramework => "Avalonia 12";

    /// <summary>数据库技术栈。</summary>
    public string Database => "SQLite + Dapper";

    /// <summary>视频引擎说明。</summary>
    public string VideoEngine => "LibVLC / LibVLCSharp (LGPL-2.1+)";

    /// <summary>功能特性列表。</summary>
    public IReadOnlyList<string> Features { get; } =
    [
        "认证登录、注册与记住我",
        "设备管理 / 物模型 / MQTT / 调试 / 备份",
        "摄像头管理与 RTSP 预览（LibVLC）",
        "Aurora Night / Snow Light 双主题",
        "Dapper 轻量 SQL Mapper",
        "本地 SQLite 持久化",
        "Windows / Linux / macOS 跨平台发布",
    ];

    /// <summary>开源组件列表。</summary>
    public IReadOnlyList<OpenSourceComponent> OpenSourceComponents { get; } =
    [
        new()
        {
            Name = "LibVLCSharp / LibVLCSharp.Avalonia",
            License = "LGPL-2.1+",
            SourceUrl = "https://code.videolan.org/videolan/LibVLCSharp",
            Notes = "摄像头预览托管控件；本应用未修改其源码。",
        },
        new()
        {
            Name = "LibVLC（VideoLAN.LibVLC.Windows / Mac）",
            License = "LGPL-2.1+",
            SourceUrl = "https://code.videolan.org/videolan/vlc",
            Notes = "随应用分发的原生动态库，可被替换；Linux 可使用系统 LibVLC。",
        },
    ];

    /// <summary>LGPL 合规摘要说明。</summary>
    public string LgplSummary { get; } =
        "本应用以动态库方式使用 LibVLC / LibVLCSharp，未修改其源码。"
        + "根据 LGPL-2.1，您有权获取对应版本的源代码，并可用兼容的 LibVLC 动态库替换本应用自带的库后继续运行。"
        + "完整许可文本见应用程序目录下 Licenses/LGPL-2.1.txt；组件说明见 Licenses/NOTICE-LibVLC.txt。";

    /// <summary>LGPL 完整许可文本（从文件加载）。</summary>
    [ObservableProperty]
    private string _licenseText = "正在加载许可文本…";

    /// <summary>LibVLC 组件 NOTICE 文本（从文件加载）。</summary>
    [ObservableProperty]
    private string _noticeText = string.Empty;

    /// <summary>
    /// 构造关于页 ViewModel，并加载许可文件。
    /// </summary>
    public AboutViewModel()
    {
        LoadLicenseFiles();
    }

    /// <summary>
    /// 从应用程序目录加载 LGPL 与 NOTICE 文本文件。
    /// </summary>
    private void LoadLicenseFiles()
    {
        try
        {
            var root = AppContext.BaseDirectory;
            var noticePath = Path.Combine(root, "Licenses", "NOTICE-LibVLC.txt");
            var lgplPath = Path.Combine(root, "Licenses", "LGPL-2.1.txt");

            // 分别加载 NOTICE 与 LGPL 全文，缺失时给出友好提示
            NoticeText = File.Exists(noticePath)
                ? File.ReadAllText(noticePath)
                : "未找到 Licenses/NOTICE-LibVLC.txt";

            LicenseText = File.Exists(lgplPath)
                ? File.ReadAllText(lgplPath)
                : "未找到 Licenses/LGPL-2.1.txt。请访问 https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html 查看全文。";
        }
        catch (Exception ex)
        {
            LicenseText = "加载许可文件失败：" + ex.Message;
        }
    }

    /// <summary>
    /// 在浏览器中打开 LibVLCSharp 源码仓库。
    /// </summary>
    [RelayCommand]
    private void OpenLibVlcSharpSource()
        => OpenUrl("https://code.videolan.org/videolan/LibVLCSharp");

    /// <summary>
    /// 在浏览器中打开 LibVLC 源码仓库。
    /// </summary>
    [RelayCommand]
    private void OpenLibVlcSource()
        => OpenUrl("https://code.videolan.org/videolan/vlc");

    /// <summary>
    /// 在浏览器中打开 LGPL-2.1 在线全文。
    /// </summary>
    [RelayCommand]
    private void OpenLgplOnline()
        => OpenUrl("https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html");

    /// <summary>
    /// 使用系统默认浏览器打开 URL。
    /// </summary>
    /// <param name="url">目标 URL。</param>
    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch
        {
            /* 无法启动浏览器时静默忽略 */
        }
    }
}
