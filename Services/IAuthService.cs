using Lana.Models;

namespace Lana.Services;

/// <summary>
/// 认证服务：登录/注册/改密/登出；维护内存中的 <see cref="CurrentUser"/>。
/// <para>角色：Admin（可见设备/摄像头管理）、Member（注册默认角色）。</para>
/// </summary>
public interface IAuthService
{
    /// <summary>当前会话用户；未登录为 null。</summary>
    AppUser? CurrentUser { get; }

    bool IsAuthenticated { get; }

    Task<(bool Success, string Message)> LoginAsync(string username, string password);

    /// <summary>注册新用户，角色固定为 Member。</summary>
    Task<(bool Success, string Message)> RegisterAsync(string username, string password, string displayName);

    Task<(bool Success, string Message)> ChangePasswordAsync(string currentPassword, string newPassword);

    void Logout();
}
