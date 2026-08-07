using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lana.Services;

namespace Lana.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    private readonly IAuthService _authService;
    private readonly Action _onLoginSucceeded;

    [ObservableProperty]
    private string _username = "admin";

    [ObservableProperty]
    private string _password = "123456";

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public LoginViewModel(IAuthService authService, Action onLoginSucceeded)
    {
        _authService = authService;
        _onLoginSucceeded = onLoginSucceeded;
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            var (success, message) = await _authService.LoginAsync(Username, Password);
            if (!success)
            {
                ErrorMessage = message;
                return;
            }

            _onLoginSucceeded();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
