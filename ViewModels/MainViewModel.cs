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
/// <item>实时状态：使用单例 <see cref="DeviceDataSnapshotStore"/>，UI 绑定 Groups，勿整表刷新。</item>
/// </list>
/// </para>
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    /// <summary>身份认证服务（登录/登出/会话）。</summary>
    private readonly IAuthService _authService;

    /// <summary>用户偏好设置服务（主题、记住我等）。</summary>
    private readonly ISettingsService _settingsService;

    /// <summary>SQLite 会话工厂，供各子服务共享。</summary>
    private readonly ISqliteSessionFactory _sessionFactory;

    /// <summary>网关设备 CRUD 与物模型服务。</summary>
    private readonly GatewayDeviceService _deviceService;

    /// <summary>摄像头 CRUD 与播放请求构建。</summary>
    private readonly CameraService _cameraService;

    /// <summary>设备调试 API（读/写/读全部，默认写操作历史）。</summary>
    private readonly IDeviceDebugApi _debugApi;

    /// <summary>设备操作历史持久化服务。</summary>
    private readonly DeviceOperationHistoryService _historyService;

    /// <summary>轮询实时状态单例：Worker 写入、手动操作页绑定 Groups。登录期间保持同一实例。</summary>
    private readonly DeviceDataSnapshotStore _snapshotStore = new();

    /// <summary>后台数据采集 Worker；登录后启动，登出后停止并释放。</summary>
    private DataCollectionWorker? _worker;

    /// <summary>主壳 ViewModel；登录成功后创建，登出时释放。</summary>
    private ShellViewModel? _shell;

    /// <summary>当前根内容：登录页或主壳。</summary>
    [ObservableProperty]
    private ViewModelBase _currentViewModel;

    /// <summary>
    /// 默认构造：使用内置 <see cref="SqliteSessionFactory"/>。
    /// </summary>
    public MainViewModel()
        : this(new SqliteSessionFactory())
    {
    }

    /// <summary>
    /// 注入 SQLite 会话工厂的构造重载。
    /// </summary>
    /// <param name="sessionFactory">数据库会话工厂。</param>
    public MainViewModel(ISqliteSessionFactory sessionFactory)
        : this(new AuthService(sessionFactory), new SettingsService(sessionFactory), sessionFactory)
    {
    }

    /// <summary>
    /// 完整构造：注入认证与设置服务，并在此组合其余子服务。
    /// </summary>
    /// <param name="authService">身份认证服务。</param>
    /// <param name="settingsService">用户设置服务。</param>
    /// <param name="sessionFactory">数据库会话工厂。</param>
    public MainViewModel(
        IAuthService authService,
        ISettingsService settingsService,
        ISqliteSessionFactory sessionFactory)
    {
        _authService = authService;
        _settingsService = settingsService;
        _sessionFactory = sessionFactory;
        // 组合根：在此 new 各长寿命服务，再传入 Shell / 页面 VM
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

    /// <summary>
    /// 创建登录页 ViewModel，并绑定登录成功回调。
    /// </summary>
    /// <returns>配置好的 <see cref="LoginViewModel"/> 实例。</returns>
    private LoginViewModel CreateLogin()
        => new(_authService, _settingsService, OnLoginSucceeded);

    /// <summary>
    /// 登录成功：启动采集 Worker，进入主壳。
    /// </summary>
    private void OnLoginSucceeded()
    {
        // 校验会话：登录成功但无 CurrentUser 属于异常状态
        var user = _authService.CurrentUser
                   ?? throw new InvalidOperationException("登录成功后未找到用户会话");

        // 启动后台轮询 Worker，写入共享快照 Store
        _worker = new DataCollectionWorker(
            new GatewayConfigStore(_sessionFactory),
            new ProtocolSessionFactory(),
            _snapshotStore);
        _ = _worker.StartAsync();

        // 创建主壳并切换根内容为 Shell
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

    /// <summary>
    /// 登出：释放壳、停止采集，回到登录页。
    /// </summary>
    private void OnLogout()
    {
        // 先释放 Shell（含预览等资源）
        _shell?.Dispose();
        _shell = null;

        // 停止并释放 Worker，避免后台继续轮询
        var worker = _worker;
        _worker = null;
        if (worker is not null)
            _ = StopWorkerAsync(worker);

        CurrentViewModel = CreateLogin();
    }

    /// <summary>
    /// 异步停止并释放数据采集 Worker。
    /// </summary>
    /// <param name="worker">待停止的 Worker 实例。</param>
    /// <returns>表示停止与释放完成的 Task。</returns>
    private static async Task StopWorkerAsync(DataCollectionWorker worker)
    {
        try
        {
            await worker.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            // 无论 Stop 是否成功，都确保释放原生/网络资源
            await worker.DisposeAsync().ConfigureAwait(false);
        }
    }
}
