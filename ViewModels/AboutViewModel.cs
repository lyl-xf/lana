using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Lana.ViewModels;

public sealed class OpenSourceComponent
{
    public required string Name { get; init; }
    public required string License { get; init; }
    public required string SourceUrl { get; init; }
    public required string Notes { get; init; }
}

public partial class AboutViewModel : ViewModelBase
{
    public string Title => "关于";

    public string Subtitle => "Lana 跨平台桌面应用";

    public string Version => "1.0.0";

    public string Runtime => ".NET 9";

    public string UiFramework => "Avalonia 12";

    public string Database => "SQLite + Dapper";

    public string VideoEngine => "LibVLC / LibVLCSharp (LGPL-2.1+)";

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

    public string LgplSummary { get; } =
        "本应用以动态库方式使用 LibVLC / LibVLCSharp，未修改其源码。"
        + "根据 LGPL-2.1，您有权获取对应版本的源代码，并可用兼容的 LibVLC 动态库替换本应用自带的库后继续运行。"
        + "完整许可文本见应用程序目录下 Licenses/LGPL-2.1.txt；组件说明见 Licenses/NOTICE-LibVLC.txt。";

    [ObservableProperty]
    private string _licenseText = "正在加载许可文本…";

    [ObservableProperty]
    private string _noticeText = string.Empty;

    public AboutViewModel()
    {
        LoadLicenseFiles();
    }

    private void LoadLicenseFiles()
    {
        try
        {
            var root = AppContext.BaseDirectory;
            var noticePath = Path.Combine(root, "Licenses", "NOTICE-LibVLC.txt");
            var lgplPath = Path.Combine(root, "Licenses", "LGPL-2.1.txt");

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

    [RelayCommand]
    private void OpenLibVlcSharpSource()
        => OpenUrl("https://code.videolan.org/videolan/LibVLCSharp");

    [RelayCommand]
    private void OpenLibVlcSource()
        => OpenUrl("https://code.videolan.org/videolan/vlc");

    [RelayCommand]
    private void OpenLgplOnline()
        => OpenUrl("https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html");

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
            /* ignore */
        }
    }
}
