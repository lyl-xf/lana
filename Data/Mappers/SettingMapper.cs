using Lana.Data.Entities;
using Lana.Data.Sqlite;

namespace Lana.Data.Mappers;

/// <summary>
/// 设置表 SQL Mapper（MyBatis 风格：SQL 与代码分离）。
/// </summary>
public sealed class SettingMapper
{
    /// <summary>用于每次操作打开短生命周期会话。</summary>
    private readonly ISqliteSessionFactory _sessionFactory;

    /// <summary>
    /// 创建设置表 Mapper。
    /// </summary>
    /// <param name="sessionFactory">数据库会话工厂。</param>
    public SettingMapper(ISqliteSessionFactory sessionFactory)
    {
        _sessionFactory = sessionFactory;
    }

    /// <summary>
    /// 按键名查询设置项。
    /// </summary>
    /// <param name="key">设置键名。</param>
    /// <returns>匹配的实体，不存在时返回 null。</returns>
    public async Task<AppSettingEntity?> FindByKeyAsync(string key)
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.QueryFirstOrDefaultAsync<AppSettingEntity>(Sql.FindByKey, new { Key = key });
    }

    /// <summary>
    /// 插入新的键值设置。
    /// </summary>
    /// <param name="key">设置键名。</param>
    /// <param name="value">设置值。</param>
    /// <returns>受影响的行数。</returns>
    public async Task<int> InsertAsync(string key, string value)
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.ExecuteAsync(Sql.Insert, new { Key = key, Value = value });
    }

    /// <summary>
    /// 更新已有键的值。
    /// </summary>
    /// <param name="key">设置键名。</param>
    /// <param name="value">新值。</param>
    /// <returns>受影响的行数。</returns>
    public async Task<int> UpdateValueAsync(string key, string value)
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.ExecuteAsync(Sql.UpdateValue, new { Key = key, Value = value });
    }

    /// <summary>
    /// 插入或更新设置项（存在则更新，不存在则插入）。
    /// </summary>
    /// <param name="key">设置键名。</param>
    /// <param name="value">设置值。</param>
    public async Task UpsertAsync(string key, string value)
    {
        var existing = await FindByKeyAsync(key);
        if (existing is null)
        {
            // 键不存在，直接插入
            await InsertAsync(key, value);
            return;
        }

        // 键已存在，更新值
        await UpdateValueAsync(key, value);
    }

    /// <summary>Settings 表 SQL 语句常量。</summary>
    public static class Sql
    {
        /// <summary>按 Key 查询单条设置。</summary>
        public const string FindByKey = """
            SELECT Id, Key, Value
            FROM Settings
            WHERE Key = @Key
            LIMIT 1;
            """;

        /// <summary>插入新设置项。</summary>
        public const string Insert = """
            INSERT INTO Settings (Key, Value)
            VALUES (@Key, @Value);
            """;

        /// <summary>按 Key 更新 Value。</summary>
        public const string UpdateValue = """
            UPDATE Settings
            SET Value = @Value
            WHERE Key = @Key;
            """;
    }
}
