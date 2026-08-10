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

    /// <summary>是否已登录（<see cref="CurrentUser"/> 非 null）。</summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// 验证凭据并建立会话。
    /// </summary>
    /// <param name="username">用户名。</param>
    /// <param name="password">明文密码。</param>
    /// <returns>元组：(是否成功, 提示消息)。</returns>
    Task<(bool Success, string Message)> LoginAsync(string username, string password);

    /// <summary>
    /// 注册新用户，角色固定为 Member。
    /// </summary>
    /// <param name="username">用户名（2–32 字符）。</param>
    /// <param name="password">明文密码。</param>
    /// <param name="displayName">显示名称，为空则使用用户名。</param>
    /// <returns>元组：(是否成功, 提示消息)。</returns>
    Task<(bool Success, string Message)> RegisterAsync(string username, string password, string displayName);

    /// <summary>
    /// 修改当前登录用户的密码。
    /// </summary>
    /// <param name="currentPassword">当前明文密码。</param>
    /// <param name="newPassword">新明文密码。</param>
    /// <returns>元组：(是否成功, 提示消息)。</returns>
    Task<(bool Success, string Message)> ChangePasswordAsync(string currentPassword, string newPassword);

    /// <summary>清除当前会话用户。</summary>
    void Logout();
}
