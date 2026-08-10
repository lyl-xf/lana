using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lana.Data;
using Lana.Services;

namespace Lana.ViewModels;

/// <summary>
/// 登录/注册页。成功后回调 MainViewModel 进入 Shell 并启动采集 Worker。
/// </summary>
public partial class LoginViewModel : ViewModelBase
{
    /// <summary>身份认证服务。</summary>
    private readonly IAuthService _authService;

    /// <summary>用户偏好设置服务（记住我等）。</summary>
    private readonly ISettingsService _settingsService;

    /// <summary>登录成功后的回调（由 MainViewModel 注入）。</summary>
    private readonly Action _onLoginSucceeded;

    /// <summary>是否为注册模式（否则为登录模式）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PanelTitle))]
    [NotifyPropertyChangedFor(nameof(PrimaryButtonText))]
    [NotifyPropertyChangedFor(nameof(BusyButtonText))]
    [NotifyPropertyChangedFor(nameof(ModeSwitchHint))]
    [NotifyPropertyChangedFor(nameof(ModeSwitchActionText))]
    private bool _isRegisterMode;

    /// <summary>用户名输入。</summary>
    [ObservableProperty]
    private string _username = string.Empty;

    /// <summary>密码输入。</summary>
    [ObservableProperty]
    private string _password = string.Empty;

    /// <summary>确认密码（仅注册模式）。</summary>
    [ObservableProperty]
    private string _confirmPassword = string.Empty;

    /// <summary>显示名称（仅注册模式）。</summary>
    [ObservableProperty]
    private string _displayName = string.Empty;

    /// <summary>是否记住登录凭据。</summary>
    [ObservableProperty]
    private bool _rememberMe;

    /// <summary>错误提示信息。</summary>
    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>成功/提示信息。</summary>
    [ObservableProperty]
    private string _infoMessage = string.Empty;

    /// <summary>是否正在提交（登录/注册中）。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>面板标题（登录/注册切换）。</summary>
    public string PanelTitle => IsRegisterMode ? "创建账号" : "欢迎登录";

    /// <summary>主按钮文案。</summary>
    public string PrimaryButtonText => IsRegisterMode ? "注册并继续" : "进入工作台";

    /// <summary>忙碌状态下按钮文案。</summary>
    public string BusyButtonText => IsRegisterMode ? "注册中..." : "验证中...";

    /// <summary>模式切换提示文案。</summary>
    public string ModeSwitchHint => IsRegisterMode ? "已有账号？" : "还没有账号？";

    /// <summary>模式切换操作文案。</summary>
    public string ModeSwitchActionText => IsRegisterMode ? "返回登录" : "注册用户";

    /// <summary>
    /// 构造登录页 ViewModel。
    /// </summary>
    /// <param name="authService">身份认证服务。</param>
    /// <param name="settingsService">用户设置服务。</param>
    /// <param name="onLoginSucceeded">登录成功回调。</param>
    public LoginViewModel(
        IAuthService authService,
        ISettingsService settingsService,
        Action onLoginSucceeded)
    {
        _authService = authService;
        _settingsService = settingsService;
        _onLoginSucceeded = onLoginSucceeded;
        _ = LoadRememberedAsync();
    }

    /// <summary>
    /// 从 SQLite 加载「记住我」及已保存的凭据。
    /// </summary>
    /// <returns>表示加载完成的 Task。</returns>
    private async Task LoadRememberedAsync()
    {
        RememberMe = await _settingsService.GetBoolAsync(SettingKeys.RememberMe);
        if (!RememberMe)
        {
            return;
        }

        Username = await _settingsService.GetStringAsync(SettingKeys.RememberedUsername);
        var encoded = await _settingsService.GetStringAsync(SettingKeys.RememberedPassword);
        Password = DecodeCredential(encoded);
    }

    /// <summary>
    /// 切换登录/注册模式，并清空相关输入与提示。
    /// </summary>
    [RelayCommand]
    private void ToggleMode()
    {
        IsRegisterMode = !IsRegisterMode;
        ErrorMessage = string.Empty;
        InfoMessage = string.Empty;
        ConfirmPassword = string.Empty;
        DisplayName = string.Empty;
    }

    /// <summary>
    /// 提交表单：根据当前模式执行登录或注册。
    /// </summary>
    /// <returns>表示提交完成的 Task。</returns>
    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (IsBusy)
        {
            return;
        }

        // 按模式分支到登录或注册流程
        if (IsRegisterMode)
        {
            await RegisterAsync();
        }
        else
        {
            await LoginAsync();
        }
    }

    /// <summary>
    /// 执行登录并持久化「记住我」设置。
    /// </summary>
    /// <returns>表示登录流程完成的 Task。</returns>
    private async Task LoginAsync()
    {
        IsBusy = true;
        ErrorMessage = string.Empty;
        InfoMessage = string.Empty;

        try
        {
            var (success, message) = await _authService.LoginAsync(Username, Password);
            if (!success)
            {
                ErrorMessage = message;
                return;
            }

            await PersistRememberMeAsync();
            _onLoginSucceeded();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 执行注册；成功后切回登录模式并显示提示。
    /// </summary>
    /// <returns>表示注册流程完成的 Task。</returns>
    private async Task RegisterAsync()
    {
        // 客户端校验：两次密码必须一致
        if (!string.Equals(Password, ConfirmPassword, StringComparison.Ordinal))
        {
            ErrorMessage = "两次输入的密码不一致";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        InfoMessage = string.Empty;

        try
        {
            var (success, message) = await _authService.RegisterAsync(Username, Password, DisplayName);
            if (!success)
            {
                ErrorMessage = message;
                return;
            }

            // 注册成功：切回登录模式，保留用户名
            IsRegisterMode = false;
            ConfirmPassword = string.Empty;
            DisplayName = string.Empty;
            InfoMessage = message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 持久化「记住我」开关及凭据（Base64 编码存储）。
    /// </summary>
    /// <returns>表示持久化完成的 Task。</returns>
    private async Task PersistRememberMeAsync()
    {
        await _settingsService.SetBoolAsync(SettingKeys.RememberMe, RememberMe);
        if (RememberMe)
        {
            await _settingsService.SetStringAsync(SettingKeys.RememberedUsername, Username.Trim());
            await _settingsService.SetStringAsync(SettingKeys.RememberedPassword, EncodeCredential(Password));
            return;
        }

        // 取消记住我：清空已保存凭据
        await _settingsService.SetStringAsync(SettingKeys.RememberedUsername, string.Empty);
        await _settingsService.SetStringAsync(SettingKeys.RememberedPassword, string.Empty);
    }

    /// <summary>
    /// 将凭据编码为 Base64 字符串。
    /// </summary>
    /// <param name="value">原始凭据。</param>
    /// <returns>Base64 编码结果；空输入返回空字符串。</returns>
    private static string EncodeCredential(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

    /// <summary>
    /// 将 Base64 字符串解码为凭据。
    /// </summary>
    /// <param name="encoded">Base64 编码的凭据。</param>
    /// <returns>解码后的凭据；无效输入返回空字符串。</returns>
    private static string DecodeCredential(string encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return string.Empty;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch (FormatException)
        {
            // 历史数据损坏或非 Base64 格式时静默回退
            return string.Empty;
        }
    }
}
