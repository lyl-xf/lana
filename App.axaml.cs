using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Lana.Data;
using Lana.Data.Sqlite;
using Lana.Services;
using Lana.Themes;
using Lana.ViewModels;
using Lana.Views;

namespace Lana;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        var sessionFactory = new SqliteSessionFactory();
        await DbInitializer.InitializeAsync(sessionFactory);

        var settingsService = new SettingsService(sessionFactory);
        var theme = await ResolveThemeAsync(settingsService);
        ThemeManager.Apply(theme);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(sessionFactory),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task<string> ResolveThemeAsync(ISettingsService settingsService)
    {
        var theme = await settingsService.GetStringAsync(SettingKeys.ThemeStyle, string.Empty);
        if (!string.IsNullOrWhiteSpace(theme))
        {
            return theme;
        }

        var isDark = await settingsService.GetBoolAsync(SettingKeys.DarkTheme, true);
        theme = isDark ? ThemeManager.Aurora : ThemeManager.Snow;
        await settingsService.SetStringAsync(SettingKeys.ThemeStyle, theme);
        return theme;
    }
}
