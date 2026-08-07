using Lana.Cameras.Services;
using Lana.Data.Sqlite;
using Lana.Gateway.Services;
using Lana.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lana.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IAuthService _authService;
    private readonly ISettingsService _settingsService;
    private readonly ISqliteSessionFactory _sessionFactory;
    private readonly GatewayDeviceService _deviceService;
    private readonly CameraService _cameraService;
    private readonly IDeviceDebugApi _debugApi;
    private readonly DeviceOperationHistoryService _historyService;
    private DataCollectionWorker? _worker;
    private ShellViewModel? _shell;

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

    /// <summary>供其他模块直接使用的设备调试 API。</summary>
    public IDeviceDebugApi DeviceDebugApi => _debugApi;

    private LoginViewModel CreateLogin()
        => new(_authService, _settingsService, OnLoginSucceeded);

    private void OnLoginSucceeded()
    {
        var user = _authService.CurrentUser
                   ?? throw new InvalidOperationException("登录成功后未找到用户会话");

        _worker = new DataCollectionWorker(
            new GatewayConfigStore(_sessionFactory),
            new ProtocolSessionFactory());
        _ = _worker.StartAsync();

        _shell = new ShellViewModel(
            _authService,
            _settingsService,
            user,
            OnLogout,
            _deviceService,
            _cameraService,
            _debugApi,
            _historyService);
        CurrentViewModel = _shell;
    }

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
