namespace Lana.Models;

/// <summary>
/// 登录后会话用户（非数据库实体）。Role：Admin / Member。
/// </summary>
public sealed class AppUser
{
    /// <summary>用户主键，对应 Users 表 Id。</summary>
    public int Id { get; init; }

    /// <summary>登录用户名。</summary>
    public required string Username { get; init; }

    /// <summary>界面显示名称。</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Admin 可见设备管理与摄像头管理；注册用户默认为 Member。</summary>
    public string Role { get; init; } = "Member";
}
