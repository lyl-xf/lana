using Lana.Models;
using Lana.Services;
using Lana.Themes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Lana.ViewModels;

public partial class ShellViewModel : ViewModelBase
{
    private readonly IAuthService _authService;
    private readonly Action _onLogout;
    private readonly Dictionary<string, ViewModelBase> _pages;

    [ObservableProperty]
    private ViewModelBase _currentPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHomeSelected))]
    [NotifyPropertyChangedFor(nameof(IsProjectsSelected))]
    [NotifyPropertyChangedFor(nameof(IsMessagesSelected))]
    [NotifyPropertyChangedFor(nameof(IsAnalyticsSelected))]
    [NotifyPropertyChangedFor(nameof(IsAboutSelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsSelected))]
    [NotifyPropertyChangedFor(nameof(CurrentPageTitle))]
    private string _selectedNav = "Home";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SidebarToggleText))]
    private bool _isSidebarExpanded = true;

    [ObservableProperty]
    private string _userDisplayName;

    [ObservableProperty]
    private string _userRole;

    [ObservableProperty]
    private string _themeDisplayName = ThemeManager.GetDisplayName(ThemeManager.CurrentTheme);

    public bool IsHomeSelected => SelectedNav == "Home";
    public bool IsProjectsSelected => SelectedNav == "Projects";
    public bool IsMessagesSelected => SelectedNav == "Messages";
    public bool IsAnalyticsSelected => SelectedNav == "Analytics";
    public bool IsAboutSelected => SelectedNav == "About";
    public bool IsSettingsSelected => SelectedNav == "Settings";

    public string SidebarToggleText => IsSidebarExpanded ? "收起菜单" : "展开菜单";

    public string CurrentPageTitle => SelectedNav switch
    {
        "Home" => "首页",
        "Projects" => "项目",
        "Messages" => "消息",
        "Analytics" => "数据",
        "About" => "关于",
        "Settings" => "设置",
        _ => "工作台",
    };

    public ShellViewModel(
        IAuthService authService,
        ISettingsService settingsService,
        AppUser user,
        Action onLogout)
    {
        _authService = authService;
        _onLogout = onLogout;
        _userDisplayName = user.DisplayName;
        _userRole = user.Role;

        _pages = new Dictionary<string, ViewModelBase>
        {
            ["Home"] = new HomeViewModel(user),
            ["Projects"] = new ProjectsViewModel(),
            ["Messages"] = new MessagesViewModel(),
            ["Analytics"] = new AnalyticsViewModel(),
            ["About"] = new AboutViewModel(),
            ["Settings"] = new SettingsViewModel(user, settingsService, authService),
        };

        _currentPage = _pages["Home"];
        ThemeManager.ThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged(string theme)
    {
        ThemeDisplayName = ThemeManager.GetDisplayName(theme);
    }

    [RelayCommand]
    private void NavigateHome() => Navigate("Home");

    [RelayCommand]
    private void NavigateProjects() => Navigate("Projects");

    [RelayCommand]
    private void NavigateMessages() => Navigate("Messages");

    [RelayCommand]
    private void NavigateAnalytics() => Navigate("Analytics");

    [RelayCommand]
    private void NavigateAbout() => Navigate("About");

    [RelayCommand]
    private void NavigateSettings() => Navigate("Settings");

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarExpanded = !IsSidebarExpanded;
    }

    private void Navigate(string key)
    {
        SelectedNav = key;
        CurrentPage = _pages[key];
    }

    [RelayCommand]
    private void Logout()
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
        _authService.Logout();
        _onLogout();
    }
}
