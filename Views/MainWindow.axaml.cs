using Avalonia.Controls;

namespace Lana.Views;

/// <summary>
/// 应用程序主窗口。
/// <para>承载根内容区域（登录页或 <see cref="ShellView"/>），负责窗口级尺寸、标题与主题资源。</para>
/// <para>窗口 chrome 与 DataContext 注入见 <c>MainWindow.axaml</c> 及 App 启动逻辑。</para>
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// 初始化主窗口并加载 XAML 组件树。
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
    }
}
