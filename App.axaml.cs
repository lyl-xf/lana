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

/// <summary>
/// Avalonia 应用对象：加载 XAML、初始化数据库与主题，并创建主窗口。
/// <para>
/// 本项目未使用 DI 容器。长寿命服务在此处创建see cref="SqliteSessionFactory"/>，
/// 业务服务图在 <see cref="MainViewModel"/> 构造函数中手工组装。
/// </para>
/// <para>
/// 新增持久化模块时：在 <see cref="DbInitializer"/> 中调用对应 Schema.EnsureAsync。
/// </para>
/// </summary>
public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        // 1) 数据库会话工厂（全进程共享）
        var sessionFactory = new SqliteSessionFactory();
        // 2) 建表 / 迁移 / 种子账号与默认设置
        await DbInitializer.InitializeAsync(sessionFactory);

        // 3) 主题（可在登录前先应用，避免闪屏）
        var settingsService = new SettingsService(sessionFactory);
        var theme = await ResolveThemeAsync(settingsService);
        ThemeManager.Apply(theme);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 4) 主窗口 + 根 ViewModel（内部再组装 Gateway / Camera / DebugApi 等）
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(sessionFactory),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>读取主题设置；兼容旧版 DarkTheme 布尔开关。</summary>
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
