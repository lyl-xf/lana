using Lana.Cameras.Services;
using Lana.Gateway.Services;
using Lana.Models;
using Lana.Services;
using Lana.Themes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Lana.ViewModels;

public partial class ShellViewModel : ViewModelBase, IDisposable
{
    private readonly IAuthService _authService;
    private readonly Action _onLogout;
    private readonly Dictionary<string, ViewModelBase> _pages;
    private readonly CamerasViewModel? _camerasViewModel;
    private readonly DefinedPageViewModel _definedPageViewModel;
    private bool _disposed;

    [ObservableProperty]
    private ViewModelBase _currentPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHomeSelected))]
    [NotifyPropertyChangedFor(nameof(IsDefinedPageSelected))]
    [NotifyPropertyChangedFor(nameof(IsDevicesSelected))]
    [NotifyPropertyChangedFor(nameof(IsCamerasSelected))]
    [NotifyPropertyChangedFor(nameof(IsHistorySelected))]
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

    public bool IsAdmin => string.Equals(UserRole, "Admin", StringComparison.OrdinalIgnoreCase);

    public bool IsHomeSelected => SelectedNav == "Home";
    public bool IsDefinedPageSelected => SelectedNav == "DefinedPage";
    public bool IsDevicesSelected => SelectedNav == "Devices";
    public bool IsCamerasSelected => SelectedNav == "Cameras";
    public bool IsHistorySelected => SelectedNav == "History";
    public bool IsSettingsSelected => SelectedNav == "Settings";

    public string SidebarToggleText => IsSidebarExpanded ? "收起菜单" : "展开菜单";

    public string CurrentPageTitle => SelectedNav switch
    {
        "Home" => "首页",
        "DefinedPage" => "定义页面",
        "Devices" => "设备管理",
        "Cameras" => "摄像头管理",
        "History" => "历史数据",
        "Settings" => "设置",
        _ => "工作台",
    };

    public ShellViewModel(
        IAuthService authService,
        ISettingsService settingsService,
        AppUser user,
        Action onLogout,
        GatewayDeviceService deviceService,
        CameraService cameraService,
        IDeviceDebugApi debugApi,
        DeviceOperationHistoryService historyService)
    {
        _authService = authService;
        _onLogout = onLogout;
        _userDisplayName = user.DisplayName;
        _userRole = user.Role;
        _definedPageViewModel = new DefinedPageViewModel(deviceService, debugApi, cameraService);

        _pages = new Dictionary<string, ViewModelBase>
        {
            ["Home"] = new HomeViewModel(user),
            ["DefinedPage"] = _definedPageViewModel,
            ["History"] = new HistoryViewModel(historyService, deviceService),
            ["Settings"] = new SettingsViewModel(user, settingsService, authService),
        };

        if (IsAdmin)
        {
            _camerasViewModel = new CamerasViewModel(cameraService);
            _pages["Devices"] = new DevicesViewModel(deviceService, debugApi);
            _pages["Cameras"] = _camerasViewModel;
        }

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
    private void NavigateDefinedPage() => Navigate("DefinedPage");

    [RelayCommand]
    private void NavigateDevices() => Navigate("Devices");

    [RelayCommand]
    private void NavigateCameras() => Navigate("Cameras");

    [RelayCommand]
    private void NavigateHistory() => Navigate("History");

    [RelayCommand]
    private void NavigateSettings() => Navigate("Settings");

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarExpanded = !IsSidebarExpanded;
    }

    private void Navigate(string key)
    {
        if ((key is "Devices" or "Cameras") && !IsAdmin)
            return;

        if (!_pages.ContainsKey(key))
            return;

        if (SelectedNav == "Cameras" && key != "Cameras")
            _camerasViewModel?.StopPreviewsIfAny();

        if (SelectedNav == "DefinedPage" && key != "DefinedPage")
            _definedPageViewModel.StopPreviewsIfAny();

        SelectedNav = key;
        CurrentPage = _pages[key];

        if (key == "DefinedPage")
            _ = _definedPageViewModel.OnEnteredAsync();
        else if (key == "History" && CurrentPage is HistoryViewModel history)
            _ = history.RefreshCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void Logout()
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
        _camerasViewModel?.Dispose();
        _definedPageViewModel.Dispose();
        _authService.Logout();
        _onLogout();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        ThemeManager.ThemeChanged -= OnThemeChanged;
        _camerasViewModel?.Dispose();
        _definedPageViewModel.Dispose();
    }
}
