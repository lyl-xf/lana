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
    private readonly ISettingsService _settingsService;
    private readonly IAuthService _authService;
    private bool _suppressPersist;

    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private string _username;

    [ObservableProperty]
    private string _role;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAuroraSelected))]
    [NotifyPropertyChangedFor(nameof(IsSnowSelected))]
    private string _selectedTheme = ThemeManager.Aurora;

    [ObservableProperty]
    private bool _enableAnimations = true;

    [ObservableProperty]
    private string _currentPassword = string.Empty;

    [ObservableProperty]
    private string _newPassword = string.Empty;

    [ObservableProperty]
    private string _confirmNewPassword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPasswordError))]
    [NotifyPropertyChangedFor(nameof(HasPasswordSuccess))]
    private string _passwordMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPasswordError))]
    [NotifyPropertyChangedFor(nameof(HasPasswordSuccess))]
    private bool _isPasswordError;

    public bool HasPasswordError => IsPasswordError && !string.IsNullOrWhiteSpace(PasswordMessage);

    public bool HasPasswordSuccess => !IsPasswordError && !string.IsNullOrWhiteSpace(PasswordMessage);

    [ObservableProperty]
    private bool _isChangingPassword;

    [ObservableProperty]
    private string _statusMessage = "设置已连接到 SQLite";

    public bool IsAuroraSelected => !ThemeManager.IsSnow(SelectedTheme);

    public bool IsSnowSelected => ThemeManager.IsSnow(SelectedTheme);

    public SettingsViewModel(AppUser user, ISettingsService settingsService, IAuthService authService)
    {
        _settingsService = settingsService;
        _authService = authService;
        DisplayName = user.DisplayName;
        Username = user.Username;
        Role = user.Role;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        _suppressPersist = true;
        try
        {
            SelectedTheme = await _settingsService.GetStringAsync(SettingKeys.ThemeStyle, ThemeManager.Aurora);
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

    [RelayCommand]
    private Task SelectAuroraThemeAsync()
        => SelectThemeAsync(ThemeManager.Aurora);

    [RelayCommand]
    private Task SelectSnowThemeAsync()
        => SelectThemeAsync(ThemeManager.Snow);

    private async Task SelectThemeAsync(string theme)
    {
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

    partial void OnEnableAnimationsChanged(bool value)
    {
        if (_suppressPersist)
        {
            return;
        }

        _ = PersistBoolAsync(SettingKeys.EnableAnimations, value, value ? "已开启动效并写入 SQLite" : "已关闭动效并写入 SQLite");
    }

    [RelayCommand]
    private async Task ChangePasswordAsync()
    {
        if (IsChangingPassword)
        {
            return;
        }

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

            CurrentPassword = string.Empty;
            NewPassword = string.Empty;
            ConfirmNewPassword = string.Empty;

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

    private async Task PersistBoolAsync(string key, bool value, string message)
    {
        await _settingsService.SetBoolAsync(key, value);
        StatusMessage = message;
    }
}
