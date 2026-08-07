using Lana.Data;
using Lana.Data.Entities;
using Lana.Data.Mappers;
using Lana.Data.Sqlite;
using Lana.Models;

namespace Lana.Services;

public sealed class AuthService : IAuthService
{
    private const int MinPasswordLength = 4;

    private readonly UserMapper _userMapper;

    public AuthService(ISqliteSessionFactory sessionFactory)
        : this(new UserMapper(sessionFactory))
    {
    }

    public AuthService(UserMapper userMapper)
    {
        _userMapper = userMapper;
    }

    public AppUser? CurrentUser { get; private set; }

    public bool IsAuthenticated => CurrentUser is not null;

    public async Task<(bool Success, string Message)> LoginAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return (false, "请输入用户名和密码");
        }

        var normalized = username.Trim();
        var user = await _userMapper.FindByUsernameAsync(normalized);

        await Task.Delay(400);

        if (user is null || !PasswordHasher.Verify(password, user.PasswordHash))
        {
            return (false, "用户名或密码不正确");
        }

        await _userMapper.UpdateLastLoginAsync(user.Id, DateTime.UtcNow);

        CurrentUser = new AppUser
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            Role = user.Role,
        };

        return (true, "登录成功");
    }

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

        if (!PasswordHasher.Verify(currentPassword, user.PasswordHash))
        {
            return (false, "当前密码不正确");
        }

        await _userMapper.UpdatePasswordAsync(user.Id, PasswordHasher.Hash(newPassword));
        return (true, "密码已更新");
    }

    public void Logout()
    {
        CurrentUser = null;
    }
}
