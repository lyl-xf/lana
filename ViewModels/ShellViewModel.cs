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
    /// <summary>身份认证服务（登出）。</summary>
    private readonly IAuthService _authService;

    /// <summary>登出回调（由 MainViewModel 注入）。</summary>
    private readonly Action _onLogout;

    /// <summary>导航键 → 页面 VM。键需与 SelectedNav / Navigate 一致。</summary>
    private readonly Dictionary<string, ViewModelBase> _pages;

    /// <summary>摄像头管理页 VM（Admin 才有；含预览资源）。</summary>
    private readonly CamerasViewModel? _camerasViewModel;

    /// <summary>定义页 VM（含预览与实时状态绑定）。</summary>
    private readonly DefinedPageViewModel _definedPageViewModel;

    /// <summary>是否已释放。</summary>
    private bool _disposed;

    /// <summary>当前显示的页面 ViewModel。</summary>
    [ObservableProperty]
    private ViewModelBase _currentPage;

    /// <summary>当前选中的导航键。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHomeSelected))]
    [NotifyPropertyChangedFor(nameof(IsDefinedPageSelected))]
    [NotifyPropertyChangedFor(nameof(IsDevicesSelected))]
    [NotifyPropertyChangedFor(nameof(IsCamerasSelected))]
    [NotifyPropertyChangedFor(nameof(IsHistorySelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsSelected))]
    [NotifyPropertyChangedFor(nameof(CurrentPageTitle))]
    private string _selectedNav = "Home";

    /// <summary>侧栏是否展开。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SidebarToggleText))]
    private bool _isSidebarExpanded = true;

    /// <summary>用户显示名。</summary>
    [ObservableProperty]
    private string _userDisplayName;

    /// <summary>用户角色。</summary>
    [ObservableProperty]
    private string _userRole;

    /// <summary>当前主题显示名称。</summary>
    [ObservableProperty]
    private string _themeDisplayName = ThemeManager.GetDisplayName(ThemeManager.CurrentTheme);

    /// <summary>是否为 Admin 角色（控制管理页可见性）。</summary>
    public bool IsAdmin => string.Equals(UserRole, "Admin", StringComparison.OrdinalIgnoreCase);

    /// <summary>首页导航是否选中。</summary>
    public bool IsHomeSelected => SelectedNav == "Home";

    /// <summary>定义页导航是否选中（页面标题为「手动操作」）。</summary>
    public bool IsDefinedPageSelected => SelectedNav == "DefinedPage";

    /// <summary>设备管理导航是否选中。</summary>
    public bool IsDevicesSelected => SelectedNav == "Devices";

    /// <summary>摄像头管理导航是否选中。</summary>
    public bool IsCamerasSelected => SelectedNav == "Cameras";

    /// <summary>历史数据导航是否选中。</summary>
    public bool IsHistorySelected => SelectedNav == "History";

    /// <summary>设置导航是否选中。</summary>
    public bool IsSettingsSelected => SelectedNav == "Settings";

    /// <summary>侧栏展开/收起按钮文案。</summary>
    public string SidebarToggleText => IsSidebarExpanded ? "收起菜单" : "展开菜单";

    /// <summary>当前页面标题（侧栏/顶栏显示）。</summary>
    public string CurrentPageTitle => SelectedNav switch
    {
        "Home" => "首页",
        "DefinedPage" => "手动操作",
        "Devices" => "设备管理",
        "Cameras" => "摄像头管理",
        "History" => "历史数据",
        "Settings" => "设置",
        _ => "工作台",
    };

    /// <summary>
    /// 构造主壳：注册页面字典，Admin 额外注册设备/摄像头管理。
    /// </summary>
    /// <param name="authService">身份认证服务。</param>
    /// <param name="settingsService">用户设置服务。</param>
    /// <param name="user">当前登录用户。</param>
    /// <param name="onLogout">登出回调。</param>
    /// <param name="deviceService">网关设备服务。</param>
    /// <param name="cameraService">摄像头服务。</param>
    /// <param name="debugApi">设备调试 API。</param>
    /// <param name="historyService">操作历史服务。</param>
    /// <param name="snapshotStore">共享实时状态，注入 Worker 与定义页。</param>
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

    /// <summary>
    /// 主题变更时同步顶栏显示名称。
    /// </summary>
    /// <param name="theme">新主题标识。</param>
    private void OnThemeChanged(string theme)
    {
        ThemeDisplayName = ThemeManager.GetDisplayName(theme);
    }

    /// <summary>导航到首页。</summary>
    [RelayCommand]
    private void NavigateHome() => Navigate("Home");

    /// <summary>导航到定义页。</summary>
    [RelayCommand]
    private void NavigateDefinedPage() => Navigate("DefinedPage");

    /// <summary>导航到设备管理（Admin）。</summary>
    [RelayCommand]
    private void NavigateDevices() => Navigate("Devices");

    /// <summary>导航到摄像头管理（Admin）。</summary>
    [RelayCommand]
    private void NavigateCameras() => Navigate("Cameras");

    /// <summary>导航到历史数据。</summary>
    [RelayCommand]
    private void NavigateHistory() => Navigate("History");

    /// <summary>导航到设置。</summary>
    [RelayCommand]
    private void NavigateSettings() => Navigate("Settings");

    /// <summary>
    /// 切换侧栏展开/收起状态。
    /// </summary>
    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarExpanded = !IsSidebarExpanded;
    }

    /// <summary>
    /// 切换页面。离开摄像头/定义页时停止预览；进入定义页/历史时触发刷新。
    /// </summary>
    /// <param name="key">导航键（Home / DefinedPage / Devices 等）。</param>
    private void Navigate(string key)
    {
        // 非 Admin 禁止进入管理页
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

        // 进入特定页时触发数据刷新
        if (key == "DefinedPage")
            _ = _definedPageViewModel.OnEnteredAsync();
        else if (key == "History" && CurrentPage is HistoryViewModel history)
            _ = history.RefreshCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// 登出：释放资源、清除会话并回调 MainViewModel。
    /// </summary>
    [RelayCommand]
    private void Logout()
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
        _camerasViewModel?.Dispose();
        _definedPageViewModel.Dispose();
        _authService.Logout();
        _onLogout();
    }

    /// <summary>
    /// 释放预览资源并取消主题订阅。
    /// </summary>
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
