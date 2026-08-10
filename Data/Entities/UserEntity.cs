namespace Lana.Data.Entities;

/// <summary>
/// 用户表（Users）的持久化实体，包含认证凭据与角色信息。
/// </summary>
public sealed class UserEntity
{
    /// <summary>主键，自增。</summary>
    public int Id { get; set; }

    /// <summary>登录用户名，不区分大小写（数据库 COLLATE NOCASE）。</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>密码哈希（SHA256 十六进制，见 <see cref="Lana.Data.PasswordHasher"/>）。</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>界面显示名称。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>角色标识：Admin / Member。</summary>
    public string Role { get; set; } = "Member";

    /// <summary>账户创建时间（UTC）。</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>最近一次成功登录时间（UTC），未登录过为 null。</summary>
    public DateTime? LastLoginAtUtc { get; set; }
}
