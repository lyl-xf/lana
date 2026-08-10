using Avalonia.Controls;

namespace Lana.Views;

/// <summary>
/// 登录页面视图。
/// <para>提供用户名/密码输入、记住登录与错误提示，绑定 <c>LoginViewModel</c> 完成本地 SQLite 身份验证。</para>
/// <para>登录成功后由 Shell 导航替换为工作区，布局见 <c>LoginView.axaml</c>。</para>
/// </summary>
public partial class LoginView : UserControl
{
    /// <summary>
    /// 初始化登录页面组件。
    /// </summary>
    public LoginView()
    {
        InitializeComponent();
    }
}
