using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lana.Data;
using Lana.Services;

namespace Lana.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    private readonly IAuthService _authService;
    private readonly ISettingsService _settingsService;
    private readonly Action _onLoginSucceeded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PanelTitle))]
    [NotifyPropertyChangedFor(nameof(PrimaryButtonText))]
    [NotifyPropertyChangedFor(nameof(BusyButtonText))]
    [NotifyPropertyChangedFor(nameof(ModeSwitchHint))]
    [NotifyPropertyChangedFor(nameof(ModeSwitchActionText))]
    private bool _isRegisterMode;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _confirmPassword = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private bool _rememberMe;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _infoMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public string PanelTitle => IsRegisterMode ? "创建账号" : "欢迎登录";

    public string PrimaryButtonText => IsRegisterMode ? "注册并继续" : "进入工作台";

    public string BusyButtonText => IsRegisterMode ? "注册中..." : "验证中...";

    public string ModeSwitchHint => IsRegisterMode ? "已有账号？" : "还没有账号？";

    public string ModeSwitchActionText => IsRegisterMode ? "返回登录" : "注册用户";

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

    [RelayCommand]
    private void ToggleMode()
    {
        IsRegisterMode = !IsRegisterMode;
        ErrorMessage = string.Empty;
        InfoMessage = string.Empty;
        ConfirmPassword = string.Empty;
        DisplayName = string.Empty;
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (IsRegisterMode)
        {
            await RegisterAsync();
        }
        else
        {
            await LoginAsync();
        }
    }

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

    private async Task RegisterAsync()
    {
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

    private async Task PersistRememberMeAsync()
    {
        await _settingsService.SetBoolAsync(SettingKeys.RememberMe, RememberMe);
        if (RememberMe)
        {
            await _settingsService.SetStringAsync(SettingKeys.RememberedUsername, Username.Trim());
            await _settingsService.SetStringAsync(SettingKeys.RememberedPassword, EncodeCredential(Password));
            return;
        }

        await _settingsService.SetStringAsync(SettingKeys.RememberedUsername, string.Empty);
        await _settingsService.SetStringAsync(SettingKeys.RememberedPassword, string.Empty);
    }

    private static string EncodeCredential(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

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
            return string.Empty;
        }
    }
}
