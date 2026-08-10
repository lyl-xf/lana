using Avalonia;
using System;

namespace Lana;

/// <summary>
/// 应用程序入口。
/// <para>
/// 启动链路：<see cref="Main"/> → Avalonia 桌面生命周期 → <see cref="App"/> →
/// SQLite 初始化 → <see cref="ViewModels.MainViewModel"/>（登录）→
/// <see cref="ViewModels.ShellViewModel"/>（主壳）。
/// </para>
/// </summary>
sealed class Program
{
    /// <summary>
    /// 进程入口。在 AppMain 之前不要使用 Avalonia、第三方 API 或依赖 SynchronizationContext 的代码。
    /// </summary>
    /// <param name="args">命令行参数。</param>
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    /// <summary>
    /// 构建并配置 Avalonia 应用（设计器也会调用，请勿删除）。
    /// </summary>
    /// <returns>已配置平台检测、字体与日志的 <see cref="AppBuilder"/>。</returns>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
