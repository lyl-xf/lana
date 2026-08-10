using Lana.Cameras.Services;
using Lana.Data.Sqlite;
using Lana.Gateway.Services;
using Lana.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lana.ViewModels;

/// <summary>
/// 根 ViewModel（登录前后切换）。兼作服务组合根（Composition Root）。
/// <para>
/// <b>无 DI</b>：在此构造并持有 Auth / Settings / Gateway / Camera / DebugApi / History。
/// 登录成功后启动 <see cref="DataCollectionWorker"/>，并创建 <see cref="ShellViewModel"/>。
/// </para>
/// <para>
/// <b>扩展自定义功能时：</b>
/// <list type="bullet">
/// <item>长寿命服务：在本类构造函数中 <c>new</c>，再传入 Shell / 各页面 ViewModel。</item>
/// <item>设备读写：优先注入/使用 <see cref="IDeviceDebugApi"/>（会记历史），勿直接绕过。</item>
/// <item>后台轮询：参考 <see cref="DataCollectionWorker"/>，并在登录/登出生命周期中 Start/Stop。</item>
/// </list>
/// </para>
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly IAuthService _authService;
    private readonly ISettingsService _settingsService;
    private readonly ISqliteSessionFactory _sessionFactory;
    private readonly GatewayDeviceService _deviceService;
    private readonly CameraService _cameraService;
    private readonly IDeviceDebugApi _debugApi;
    private readonly DeviceOperationHistoryService _historyService;
    private readonly DeviceDataSnapshotStore _snapshotStore = new();
    private DataCollectionWorker? _worker;
    private ShellViewModel? _shell;

    /// <summary>当前根内容：登录页或主壳。</summary>
    [ObservableProperty]
    private ViewModelBase _currentViewModel;

    public MainViewModel()
        : this(new SqliteSessionFactory())
    {
    }

    public MainViewModel(ISqliteSessionFactory sessionFactory)
        : this(new AuthService(sessionFactory), new SettingsService(sessionFactory), sessionFactory)
    {
    }

    public MainViewModel(
        IAuthService authService,
        ISettingsService settingsService,
        ISqliteSessionFactory sessionFactory)
    {
        _authService = authService;
        _settingsService = settingsService;
        _sessionFactory = sessionFactory;
        _deviceService = new GatewayDeviceService(sessionFactory);
        _cameraService = new CameraService(sessionFactory);
        _historyService = new DeviceOperationHistoryService(sessionFactory);
        _debugApi = new DeviceDebugApi(_deviceService, _historyService, authService);
        _currentViewModel = CreateLogin();
    }

    /// <summary>
    /// 供其他模块直接使用的设备调试 API（读/写/读全部，默认写操作历史）。
    /// </summary>
    public IDeviceDebugApi DeviceDebugApi => _debugApi;

    private LoginViewModel CreateLogin()
        => new(_authService, _settingsService, OnLoginSucceeded);

    /// <summary>登录成功：启动采集 Worker，进入主壳。</summary>
    private void OnLoginSucceeded()
    {
        var user = _authService.CurrentUser
                   ?? throw new InvalidOperationException("登录成功后未找到用户会话");

        _worker = new DataCollectionWorker(
            new GatewayConfigStore(_sessionFactory),
            new ProtocolSessionFactory(),
            _snapshotStore);
        _ = _worker.StartAsync();

        _shell = new ShellViewModel(
            _authService,
            _settingsService,
            user,
            OnLogout,
            _deviceService,
            _cameraService,
            _debugApi,
            _historyService,
            _snapshotStore);
        CurrentViewModel = _shell;
    }

    /// <summary>登出：释放壳、停止采集，回到登录页。</summary>
    private void OnLogout()
    {
        _shell?.Dispose();
        _shell = null;

        var worker = _worker;
        _worker = null;
        if (worker is not null)
            _ = StopWorkerAsync(worker);

        CurrentViewModel = CreateLogin();
    }

    private static async Task StopWorkerAsync(DataCollectionWorker worker)
    {
        try
        {
            await worker.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            await worker.DisposeAsync().ConfigureAwait(false);
        }
    }
}
