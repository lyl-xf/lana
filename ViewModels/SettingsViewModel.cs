using AvaloniaUse.Data;
using AvaloniaUse.Models;
using AvaloniaUse.Services;
using AvaloniaUse.Themes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AvaloniaUse.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
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
    private string _statusMessage = "设置已连接到 SQLite";

    [ObservableProperty]
    private string _databasePath = string.Empty;

    public bool IsAuroraSelected => !ThemeManager.IsSnow(SelectedTheme);

    public bool IsSnowSelected => ThemeManager.IsSnow(SelectedTheme);

    public SettingsViewModel(AppUser user, ISettingsService settingsService)
    {
        _settingsService = settingsService;
        DisplayName = user.DisplayName;
        Username = user.Username;
        Role = user.Role;
        DatabasePath = settingsService.DatabasePath;
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
