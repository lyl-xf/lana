using AvaloniaUse.Data.Entities;
using AvaloniaUse.Data.Sqlite;

namespace AvaloniaUse.Data.Mappers;

/// <summary>
/// 用户表 SQL Mapper（MyBatis 风格：SQL 与代码分离）。
/// </summary>
public sealed class UserMapper
{
    private readonly ISqliteSessionFactory _sessionFactory;

    public UserMapper(ISqliteSessionFactory sessionFactory)
    {
        _sessionFactory = sessionFactory;
    }

    public async Task<UserEntity?> FindByUsernameAsync(string username)
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.QueryFirstOrDefaultAsync<UserEntity>(Sql.FindByUsername, new { Username = username });
    }

    public async Task<int> CountAsync()
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.ExecuteScalarAsync<int>(Sql.Count);
    }

    public async Task<int> InsertAsync(UserEntity user)
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.ExecuteAsync(Sql.Insert, user);
    }

    public async Task<int> UpdateLastLoginAsync(int id, DateTime lastLoginAtUtc)
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.ExecuteAsync(Sql.UpdateLastLogin, new { Id = id, LastLoginAtUtc = lastLoginAtUtc });
    }

    public static class Sql
    {
        public const string FindByUsername = """
            SELECT Id, Username, PasswordHash, DisplayName, Role, CreatedAtUtc, LastLoginAtUtc
            FROM Users
            WHERE Username = @Username
            LIMIT 1;
            """;

        public const string Count = """
            SELECT COUNT(1) FROM Users;
            """;

        public const string Insert = """
            INSERT INTO Users (Username, PasswordHash, DisplayName, Role, CreatedAtUtc, LastLoginAtUtc)
            VALUES (@Username, @PasswordHash, @DisplayName, @Role, @CreatedAtUtc, @LastLoginAtUtc);
            """;

        public const string UpdateLastLogin = """
            UPDATE Users
            SET LastLoginAtUtc = @LastLoginAtUtc
            WHERE Id = @Id;
            """;
    }
}
