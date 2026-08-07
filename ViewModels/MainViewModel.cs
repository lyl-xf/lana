using Lana.Data.Sqlite;
using Lana.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lana.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IAuthService _authService;
    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    private ViewModelBase _currentViewModel;

    public MainViewModel()
        : this(new SqliteSessionFactory())
    {
    }

    public MainViewModel(ISqliteSessionFactory sessionFactory)
        : this(new AuthService(sessionFactory), new SettingsService(sessionFactory))
    {
    }

    public MainViewModel(IAuthService authService, ISettingsService settingsService)
    {
        _authService = authService;
        _settingsService = settingsService;
        _currentViewModel = CreateLogin();
    }

    private LoginViewModel CreateLogin()
        => new(_authService, _settingsService, OnLoginSucceeded);

    private void OnLoginSucceeded()
    {
        var user = _authService.CurrentUser
                   ?? throw new InvalidOperationException("登录成功后未找到用户会话");

        CurrentViewModel = new ShellViewModel(_authService, _settingsService, user, OnLogout);
    }

    private void OnLogout()
    {
        CurrentViewModel = CreateLogin();
    }
}
