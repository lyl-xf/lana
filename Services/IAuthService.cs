using Lana.Models;

namespace Lana.Services;

public interface IAuthService
{
    AppUser? CurrentUser { get; }
    bool IsAuthenticated { get; }
    Task<(bool Success, string Message)> LoginAsync(string username, string password);
    Task<(bool Success, string Message)> RegisterAsync(string username, string password, string displayName);
    Task<(bool Success, string Message)> ChangePasswordAsync(string currentPassword, string newPassword);
    void Logout();
}
