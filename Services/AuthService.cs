using Lana.Data;
using Lana.Data.Entities;
using Lana.Data.Mappers;
using Lana.Data.Sqlite;
using Lana.Models;

namespace Lana.Services;

/// <summary>基于 Users 表的认证实现；密码为 SHA256 十六进制（见 <see cref="PasswordHasher"/>）。</summary>
public sealed class AuthService : IAuthService
{
    /// <summary>密码最小长度限制。</summary>
    private const int MinPasswordLength = 4;

    /// <summary>用户表数据访问。</summary>
    private readonly UserMapper _userMapper;

    /// <summary>
    /// 通过会话工厂创建默认 <see cref="UserMapper"/>。
    /// </summary>
    /// <param name="sessionFactory">数据库会话工厂。</param>
    public AuthService(ISqliteSessionFactory sessionFactory)
        : this(new UserMapper(sessionFactory))
    {
    }

    /// <summary>
    /// 使用已有 Mapper 实例（便于测试或自定义注入）。
    /// </summary>
    /// <param name="userMapper">用户表 Mapper。</param>
    public AuthService(UserMapper userMapper)
    {
        _userMapper = userMapper;
    }

    /// <inheritdoc />
    public AppUser? CurrentUser { get; private set; }

    /// <inheritdoc />
    public bool IsAuthenticated => CurrentUser is not null;

    /// <inheritdoc />
    public async Task<(bool Success, string Message)> LoginAsync(string username, string password)
    {
        // 基本非空校验
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return (false, "请输入用户名和密码");
        }

        var normalized = username.Trim();
        var user = await _userMapper.FindByUsernameAsync(normalized);

        // 模拟网络延迟，改善登录反馈体验
        await Task.Delay(400);

        // 用户不存在或密码不匹配
        if (user is null || !PasswordHasher.Verify(password, user.PasswordHash))
        {
            return (false, "用户名或密码不正确");
        }

        await _userMapper.UpdateLastLoginAsync(user.Id, DateTime.UtcNow);

        // 映射为会话用户（不含密码哈希）
        CurrentUser = new AppUser
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            Role = user.Role,
        };

        return (true, "登录成功");
    }

    /// <inheritdoc />
    public async Task<(bool Success, string Message)> RegisterAsync(string username, string password, string displayName)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return (false, "请输入用户名和密码");
        }

        var normalized = username.Trim();
        if (normalized.Length < 2 || normalized.Length > 32)
        {
            return (false, "用户名长度需在 2–32 个字符之间");
        }

        if (password.Length < MinPasswordLength)
        {
            return (false, $"密码至少 {MinPasswordLength} 位");
        }

        // 用户名唯一性检查
        var existing = await _userMapper.FindByUsernameAsync(normalized);
        if (existing is not null)
        {
            return (false, "该用户名已被注册");
        }

        var name = string.IsNullOrWhiteSpace(displayName) ? normalized : displayName.Trim();
        await _userMapper.InsertAsync(new UserEntity
        {
            Username = normalized,
            PasswordHash = PasswordHasher.Hash(password),
            DisplayName = name,
            Role = "Member",
            CreatedAtUtc = DateTime.UtcNow,
        });

        return (true, "注册成功，请登录");
    }

    /// <inheritdoc />
    public async Task<(bool Success, string Message)> ChangePasswordAsync(string currentPassword, string newPassword)
    {
        if (CurrentUser is null)
        {
            return (false, "请先登录");
        }

        if (string.IsNullOrWhiteSpace(currentPassword) || string.IsNullOrWhiteSpace(newPassword))
        {
            return (false, "请填写当前密码和新密码");
        }

        if (newPassword.Length < MinPasswordLength)
        {
            return (false, $"新密码至少 {MinPasswordLength} 位");
        }

        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
        {
            return (false, "新密码不能与当前密码相同");
        }

        var user = await _userMapper.FindByIdAsync(CurrentUser.Id);
        if (user is null)
        {
            return (false, "用户不存在");
        }

        // 验证当前密码正确性
        if (!PasswordHasher.Verify(currentPassword, user.PasswordHash))
        {
            return (false, "当前密码不正确");
        }

        await _userMapper.UpdatePasswordAsync(user.Id, PasswordHasher.Hash(newPassword));
        return (true, "密码已更新");
    }

    /// <inheritdoc />
    public void Logout()
    {
        CurrentUser = null;
    }
}
