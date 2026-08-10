using Lana.Cameras.Services;
using Lana.Gateway.Services;
using Lana.Models;
using Lana.Services;
using Lana.Themes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Lana.ViewModels;

/// <summary>
/// 主壳：侧栏导航 + 当前页面内容。
/// <para>
/// 页面注册在构造函数的 <c>_pages</c> 字典中；侧栏按钮绑定 Navigate* 命令（见 ShellView.axaml）。
/// Admin 才注册「设备管理」「摄像头管理」。
/// </para>
/// <para>
/// <b>新增自定义页面步骤：</b>
/// <list type="number">
/// <item>新建 XxxViewModel : ViewModelBase、XxxView（命名遵循 ViewLocator）。</item>
/// <item>在本类构造函数中 <c>new</c> 并放入 <c>_pages</c>；需要的服务从 MainViewModel 经本构造函数传入。</item>
/// <item>增加 SelectedNav 相关属性、CurrentPageTitle 分支、NavigateXxx 命令。</item>
/// <item>在 ShellView.axaml 增加侧栏按钮；若需权限，参考 Devices/Cameras 的 IsAdmin 判断。</item>
/// <item>离开页面若需释放资源（如预览），在 <see cref="Navigate"/> 中补充 Stop 逻辑。</item>
/// </list>
/// </para>
/// </summary>
public partial class ShellViewModel : ViewModelBase, IDisposable
{
    private readonly IAuthService _authService;
    private readonly Action _onLogout;
    /// <summary>导航键 → 页面 VM。键需与 SelectedNav / Navigate 一致。</summary>
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
        DeviceOperationHistoryService historyService,
        IDeviceDataSnapshotStore snapshotStore)
    {
        _authService = authService;
        _onLogout = onLogout;
        _userDisplayName = user.DisplayName;
        _userRole = user.Role;
        _definedPageViewModel = new DefinedPageViewModel(deviceService, debugApi, cameraService, snapshotStore);

        // 全员可见页面
        _pages = new Dictionary<string, ViewModelBase>
        {
            ["Home"] = new HomeViewModel(user),
            ["DefinedPage"] = _definedPageViewModel,
            ["History"] = new HistoryViewModel(historyService, deviceService),
            ["Settings"] = new SettingsViewModel(user, settingsService, authService),
        };

        // 管理页：仅 Admin 注册，Member 侧栏按钮应绑定 IsVisible=IsAdmin
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

    /// <summary>
    /// 切换页面。离开摄像头/定义页时停止预览；进入定义页/历史时触发刷新。
    /// </summary>
    private void Navigate(string key)
    {
        if ((key is "Devices" or "Cameras") && !IsAdmin)
            return;

        if (!_pages.ContainsKey(key))
            return;

        // 离开含预览的页面时释放 LibVLC 资源
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
