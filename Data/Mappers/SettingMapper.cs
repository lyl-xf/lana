using Lana.Data.Entities;
using Lana.Data.Sqlite;

namespace Lana.Data.Mappers;

/// <summary>
/// 设置表 SQL Mapper（MyBatis 风格：SQL 与代码分离）。
/// </summary>
public sealed class SettingMapper
{
    private readonly ISqliteSessionFactory _sessionFactory;

    public SettingMapper(ISqliteSessionFactory sessionFactory)
    {
        _sessionFactory = sessionFactory;
    }

    public async Task<AppSettingEntity?> FindByKeyAsync(string key)
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.QueryFirstOrDefaultAsync<AppSettingEntity>(Sql.FindByKey, new { Key = key });
    }

    public async Task<int> InsertAsync(string key, string value)
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.ExecuteAsync(Sql.Insert, new { Key = key, Value = value });
    }

    public async Task<int> UpdateValueAsync(string key, string value)
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.ExecuteAsync(Sql.UpdateValue, new { Key = key, Value = value });
    }

    public async Task UpsertAsync(string key, string value)
    {
        var existing = await FindByKeyAsync(key);
        if (existing is null)
        {
            await InsertAsync(key, value);
            return;
        }

        await UpdateValueAsync(key, value);
    }

    public static class Sql
    {
        public const string FindByKey = """
            SELECT Id, Key, Value
            FROM Settings
            WHERE Key = @Key
            LIMIT 1;
            """;

        public const string Insert = """
            INSERT INTO Settings (Key, Value)
            VALUES (@Key, @Value);
            """;

        public const string UpdateValue = """
            UPDATE Settings
            SET Value = @Value
            WHERE Key = @Key;
            """;
    }
}
