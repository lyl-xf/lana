using Lana.Data;
using Lana.Data.Mappers;
using Lana.Data.Sqlite;
using Lana.Models;

namespace Lana.Services;

public sealed class AuthService : IAuthService
{
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

    public void Logout()
    {
        CurrentUser = null;
    }
}
