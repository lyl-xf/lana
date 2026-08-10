using Lana.Data;
using Lana.Models;
using Lana.Services;
using Lana.Themes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Lana.ViewModels;

/// <summary>设置页：主题、动画、改密。键名见 SettingKeys。</summary>
public partial class SettingsViewModel : ViewModelBase
{
    /// <summary>用户偏好设置服务。</summary>
    private readonly ISettingsService _settingsService;

    /// <summary>身份认证服务（改密）。</summary>
    private readonly IAuthService _authService;

    /// <summary>加载设置期间抑制自动持久化，避免重复写入。</summary>
    private bool _suppressPersist;

    /// <summary>当前用户显示名。</summary>
    [ObservableProperty]
    private string _displayName;

    /// <summary>当前用户名。</summary>
    [ObservableProperty]
    private string _username;

    /// <summary>当前用户角色。</summary>
    [ObservableProperty]
    private string _role;

    /// <summary>当前选中的主题标识（Aurora / Snow）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAuroraSelected))]
    [NotifyPropertyChangedFor(nameof(IsSnowSelected))]
    private string _selectedTheme = ThemeManager.Aurora;

    /// <summary>是否启用 UI 动效。</summary>
    [ObservableProperty]
    private bool _enableAnimations = true;

    /// <summary>改密：当前密码输入。</summary>
    [ObservableProperty]
    private string _currentPassword = string.Empty;

    /// <summary>改密：新密码输入。</summary>
    [ObservableProperty]
    private string _newPassword = string.Empty;

    /// <summary>改密：确认新密码输入。</summary>
    [ObservableProperty]
    private string _confirmNewPassword = string.Empty;

    /// <summary>改密结果提示信息。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPasswordError))]
    [NotifyPropertyChangedFor(nameof(HasPasswordSuccess))]
    private string _passwordMessage = string.Empty;

    /// <summary>改密结果是否为错误。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPasswordError))]
    [NotifyPropertyChangedFor(nameof(HasPasswordSuccess))]
    private bool _isPasswordError;

    /// <summary>是否显示改密错误提示。</summary>
    public bool HasPasswordError => IsPasswordError && !string.IsNullOrWhiteSpace(PasswordMessage);

    /// <summary>是否显示改密成功提示。</summary>
    public bool HasPasswordSuccess => !IsPasswordError && !string.IsNullOrWhiteSpace(PasswordMessage);

    /// <summary>是否正在提交改密请求。</summary>
    [ObservableProperty]
    private bool _isChangingPassword;

    /// <summary>底部状态栏提示信息。</summary>
    [ObservableProperty]
    private string _statusMessage = "设置已连接到 SQLite";

    /// <summary>是否选中 Aurora 主题。</summary>
    public bool IsAuroraSelected => !ThemeManager.IsSnow(SelectedTheme);

    /// <summary>是否选中 Snow 主题。</summary>
    public bool IsSnowSelected => ThemeManager.IsSnow(SelectedTheme);

    /// <summary>
    /// 构造设置页 ViewModel 并异步加载偏好。
    /// </summary>
    /// <param name="user">当前登录用户。</param>
    /// <param name="settingsService">设置服务。</param>
    /// <param name="authService">认证服务。</param>
    public SettingsViewModel(AppUser user, ISettingsService settingsService, IAuthService authService)
    {
        _settingsService = settingsService;
        _authService = authService;
        DisplayName = user.DisplayName;
        Username = user.Username;
        Role = user.Role;
        _ = LoadAsync();
    }

    /// <summary>
    /// 从 SQLite 加载主题与动效偏好并应用到 UI。
    /// </summary>
    /// <returns>表示加载完成的 Task。</returns>
    private async Task LoadAsync()
    {
        _suppressPersist = true;
        try
        {
            SelectedTheme = await _settingsService.GetStringAsync(SettingKeys.ThemeStyle, ThemeManager.Aurora);
            // 兼容旧版 DarkTheme 布尔键
            if (string.IsNullOrWhiteSpace(SelectedTheme))
            {
                var legacyDark = await _settingsService.GetBoolAsync(SettingKeys.DarkTheme, true);
                SelectedTheme = legacyDark ? ThemeManager.Aurora : ThemeManager.Snow;
            }

            EnableAnimations = await _settingsService.GetBoolAsync(SettingKeys.EnableAnimations, true);
            ThemeManager.Apply(SelectedTheme);
            StatusMessage = "已从 SQLite 加载设置";
        }
        finally
        {
            _suppressPersist = false;
        }
    }

    /// <summary>
    /// 选择 Aurora 主题并持久化。
    /// </summary>
    /// <returns>表示切换完成的 Task。</returns>
    [RelayCommand]
    private Task SelectAuroraThemeAsync()
        => SelectThemeAsync(ThemeManager.Aurora);

    /// <summary>
    /// 选择 Snow 主题并持久化。
    /// </summary>
    /// <returns>表示切换完成的 Task。</returns>
    [RelayCommand]
    private Task SelectSnowThemeAsync()
        => SelectThemeAsync(ThemeManager.Snow);

    /// <summary>
    /// 切换主题：立即应用到 UI，非加载期间写入 SQLite。
    /// </summary>
    /// <param name="theme">主题标识。</param>
    /// <returns>表示切换与持久化完成的 Task。</returns>
    private async Task SelectThemeAsync(string theme)
    {
        // 已选中同一主题时仍确保 Apply（修复外部状态不一致）
        if (string.Equals(SelectedTheme, theme, StringComparison.OrdinalIgnoreCase) && !_suppressPersist)
        {
            ThemeManager.Apply(theme);
            return;
        }

        SelectedTheme = theme;
        ThemeManager.Apply(theme);

        if (_suppressPersist)
        {
            return;
        }

        await _settingsService.SetStringAsync(SettingKeys.ThemeStyle, theme);
        StatusMessage = $"已切换到 {ThemeManager.GetDisplayName(theme)} 并写入 SQLite";
    }

    /// <summary>
    /// 动效开关变更时自动持久化。
    /// </summary>
    /// <param name="value">新的动效开关值。</param>
    partial void OnEnableAnimationsChanged(bool value)
    {
        if (_suppressPersist)
        {
            return;
        }

        _ = PersistBoolAsync(SettingKeys.EnableAnimations, value, value ? "已开启动效并写入 SQLite" : "已关闭动效并写入 SQLite");
    }

    /// <summary>
    /// 修改当前用户密码；若开启记住我会同步更新已保存凭据。
    /// </summary>
    /// <returns>表示改密流程完成的 Task。</returns>
    [RelayCommand]
    private async Task ChangePasswordAsync()
    {
        if (IsChangingPassword)
        {
            return;
        }

        // 客户端校验：两次新密码必须一致
        if (!string.Equals(NewPassword, ConfirmNewPassword, StringComparison.Ordinal))
        {
            PasswordMessage = "两次输入的新密码不一致";
            IsPasswordError = true;
            return;
        }

        IsChangingPassword = true;
        PasswordMessage = string.Empty;
        IsPasswordError = false;

        try
        {
            var updatedPassword = NewPassword;
            var (success, message) = await _authService.ChangePasswordAsync(CurrentPassword, updatedPassword);
            PasswordMessage = message;
            IsPasswordError = !success;
            if (!success)
            {
                return;
            }

            // 改密成功：清空输入框
            CurrentPassword = string.Empty;
            NewPassword = string.Empty;
            ConfirmNewPassword = string.Empty;

            // 若记住我且用户名匹配，同步更新已保存密码
            if (await _settingsService.GetBoolAsync(SettingKeys.RememberMe)
                && string.Equals(
                    await _settingsService.GetStringAsync(SettingKeys.RememberedUsername),
                    Username,
                    StringComparison.OrdinalIgnoreCase))
            {
                var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(updatedPassword));
                await _settingsService.SetStringAsync(SettingKeys.RememberedPassword, encoded);
            }
        }
        finally
        {
            IsChangingPassword = false;
        }
    }

    /// <summary>
    /// 恢复默认主题与动效设置并写入 SQLite。
    /// </summary>
    /// <returns>表示重置完成的 Task。</returns>
    [RelayCommand]
    private async Task ResetDemoAsync()
    {
        _suppressPersist = true;
        try
        {
            SelectedTheme = ThemeManager.Aurora;
            EnableAnimations = true;
            ThemeManager.Apply(ThemeManager.Aurora);
        }
        finally
        {
            _suppressPersist = false;
        }

        await _settingsService.SetStringAsync(SettingKeys.ThemeStyle, ThemeManager.Aurora);
        await _settingsService.SetBoolAsync(SettingKeys.EnableAnimations, true);
        StatusMessage = "已恢复默认设置并写入 SQLite";
    }

    /// <summary>
    /// 持久化布尔设置并更新状态栏。
    /// </summary>
    /// <param name="key">设置键名。</param>
    /// <param name="value">布尔值。</param>
    /// <param name="message">成功提示文案。</param>
    /// <returns>表示持久化完成的 Task。</returns>
    private async Task PersistBoolAsync(string key, bool value, string message)
    {
        await _settingsService.SetBoolAsync(key, value);
        StatusMessage = message;
    }
}
