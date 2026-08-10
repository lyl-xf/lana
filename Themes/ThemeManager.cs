using Avalonia;
using Avalonia.Styling;

namespace Lana.Themes;

/// <summary>
/// 主题切换：Aurora（暗）/ Snow（亮）。刷子资源在 AppTheme.axaml；
/// 切换时设置 RequestedThemeVariant 并触发 ThemeChanged。
/// </summary>
public static class ThemeManager
{
    /// <summary>Aurora 暗色主题标识。</summary>
    public const string Aurora = "Aurora";

    /// <summary>Snow 亮色主题标识。</summary>
    public const string Snow = "Snow";

    /// <summary>当前生效的主题名（Aurora 或 Snow）。</summary>
    public static string CurrentTheme { get; private set; } = Aurora;

    /// <summary>主题切换后触发，参数为新主题名。</summary>
    public static event Action<string>? ThemeChanged;

    /// <summary>
    /// 获取主题的界面展示名称。
    /// </summary>
    /// <param name="theme">主题标识，null 或非 Snow 视为 Aurora。</param>
    /// <returns>本地化展示字符串。</returns>
    public static string GetDisplayName(string? theme)
        => IsSnow(theme) ? "Snow Light" : "Aurora Night";

    /// <summary>
    /// 判断是否为 Snow 亮色主题。
    /// </summary>
    /// <param name="theme">主题标识。</param>
    /// <returns>Snow 时返回 true。</returns>
    public static bool IsSnow(string? theme)
        => string.Equals(theme, Snow, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 应用指定主题：更新当前值、Avalonia 变体并通知订阅者。
    /// </summary>
    /// <param name="theme">主题标识，无效值回退为 Aurora。</param>
    public static void Apply(string? theme)
    {
        // 非 Snow 一律视为 Aurora
        var normalized = IsSnow(theme) ? Snow : Aurora;
        CurrentTheme = normalized;

        // 同步 Avalonia 全局主题变体
        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeVariant =
                normalized == Snow ? ThemeVariant.Light : ThemeVariant.Dark;
        }

        ThemeChanged?.Invoke(normalized);
    }
}
