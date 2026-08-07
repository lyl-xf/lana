using Avalonia;
using Avalonia.Styling;

namespace Lana.Themes;

public static class ThemeManager
{
    public const string Aurora = "Aurora";
    public const string Snow = "Snow";

    public static string CurrentTheme { get; private set; } = Aurora;

    public static event Action<string>? ThemeChanged;

    public static string GetDisplayName(string? theme)
        => IsSnow(theme) ? "Snow Light" : "Aurora Night";

    public static bool IsSnow(string? theme)
        => string.Equals(theme, Snow, StringComparison.OrdinalIgnoreCase);

    public static void Apply(string? theme)
    {
        var normalized = IsSnow(theme) ? Snow : Aurora;
        CurrentTheme = normalized;

        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeVariant =
                normalized == Snow ? ThemeVariant.Light : ThemeVariant.Dark;
        }

        ThemeChanged?.Invoke(normalized);
    }
}
