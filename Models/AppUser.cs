namespace Lana.Models;

/// <summary>
/// 登录后会话用户（非数据库实体）。Role：Admin / Member。
/// </summary>
public sealed class AppUser
{
    public int Id { get; init; }
    public required string Username { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    /// <summary>Admin 可见设备管理与摄像头管理；注册用户默认为 Member。</summary>
    public string Role { get; init; } = "Member";
}
