using Lana.Data.Entities;
using Lana.Data.Sqlite;

namespace Lana.Data.Mappers;

/// <summary>
/// 用户表 SQL Mapper（MyBatis 风格：SQL 与代码分离）。
/// </summary>
public sealed class UserMapper
{
    /// <summary>用于每次操作打开短生命周期会话。</summary>
    private readonly ISqliteSessionFactory _sessionFactory;

    /// <summary>
    /// 创建用户表 Mapper。
    /// </summary>
    /// <param name="sessionFactory">数据库会话工厂。</param>
    public UserMapper(ISqliteSessionFactory sessionFactory)
    {
        _sessionFactory = sessionFactory;
    }

    /// <summary>
    /// 按用户名查询用户（不区分大小写）。
    /// </summary>
    /// <param name="username">登录用户名。</param>
    /// <returns>匹配的用户实体，不存在时返回 null。</returns>
    public async Task<UserEntity?> FindByUsernameAsync(string username)
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.QueryFirstOrDefaultAsync<UserEntity>(Sql.FindByUsername, new { Username = username });
    }

    /// <summary>
    /// 按主键 ID 查询用户。
    /// </summary>
    /// <param name="id">用户 ID。</param>
    /// <returns>匹配的用户实体，不存在时返回 null。</returns>
    public async Task<UserEntity?> FindByIdAsync(int id)
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.QueryFirstOrDefaultAsync<UserEntity>(Sql.FindById, new { Id = id });
    }

    /// <summary>
    /// 统计用户总数，用于判断是否需写入种子数据。
    /// </summary>
    /// <returns>Users 表行数。</returns>
    public async Task<int> CountAsync()
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.ExecuteScalarAsync<int>(Sql.Count);
    }

    /// <summary>
    /// 插入新用户记录。
    /// </summary>
    /// <param name="user">待插入的用户实体。</param>
    /// <returns>受影响的行数。</returns>
    public async Task<int> InsertAsync(UserEntity user)
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.ExecuteAsync(Sql.Insert, user);
    }

    /// <summary>
    /// 更新用户最近登录时间。
    /// </summary>
    /// <param name="id">用户 ID。</param>
    /// <param name="lastLoginAtUtc">登录时间（UTC）。</param>
    /// <returns>受影响的行数。</returns>
    public async Task<int> UpdateLastLoginAsync(int id, DateTime lastLoginAtUtc)
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.ExecuteAsync(Sql.UpdateLastLogin, new { Id = id, LastLoginAtUtc = lastLoginAtUtc });
    }

    /// <summary>
    /// 更新用户密码哈希。
    /// </summary>
    /// <param name="id">用户 ID。</param>
    /// <param name="passwordHash">新密码的哈希值。</param>
    /// <returns>受影响的行数。</returns>
    public async Task<int> UpdatePasswordAsync(int id, string passwordHash)
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.ExecuteAsync(Sql.UpdatePassword, new { Id = id, PasswordHash = passwordHash });
    }

    /// <summary>Users 表 SQL 语句常量。</summary>
    public static class Sql
    {
        /// <summary>按用户名查询单条用户。</summary>
        public const string FindByUsername = """
            SELECT Id, Username, PasswordHash, DisplayName, Role, CreatedAtUtc, LastLoginAtUtc
            FROM Users
            WHERE Username = @Username
            LIMIT 1;
            """;

        /// <summary>按 ID 查询单条用户。</summary>
        public const string FindById = """
            SELECT Id, Username, PasswordHash, DisplayName, Role, CreatedAtUtc, LastLoginAtUtc
            FROM Users
            WHERE Id = @Id
            LIMIT 1;
            """;

        /// <summary>统计用户总数。</summary>
        public const string Count = """
            SELECT COUNT(1) FROM Users;
            """;

        /// <summary>插入新用户。</summary>
        public const string Insert = """
            INSERT INTO Users (Username, PasswordHash, DisplayName, Role, CreatedAtUtc, LastLoginAtUtc)
            VALUES (@Username, @PasswordHash, @DisplayName, @Role, @CreatedAtUtc, @LastLoginAtUtc);
            """;

        /// <summary>更新最近登录时间。</summary>
        public const string UpdateLastLogin = """
            UPDATE Users
            SET LastLoginAtUtc = @LastLoginAtUtc
            WHERE Id = @Id;
            """;

        /// <summary>更新密码哈希。</summary>
        public const string UpdatePassword = """
            UPDATE Users
            SET PasswordHash = @PasswordHash
            WHERE Id = @Id;
            """;
    }
}
