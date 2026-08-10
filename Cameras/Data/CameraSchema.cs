using Lana.Data.Sqlite;

namespace Lana.Cameras.Data;

/// <summary>
/// Cameras 表的初始化与列迁移逻辑。
/// 新增字段时：在此添加 <see cref="EnsureColumnAsync"/> 调用，并同步 Entity、Mapper、Service 层。
/// </summary>
public static class CameraSchema
{
    /// <summary>
    /// 确保 Cameras 表存在，并补齐历史版本缺失的列（向后兼容旧数据库）。
    /// </summary>
    /// <param name="session">SQLite 会话，用于执行 DDL。</param>
    public static async Task EnsureAsync(ISqliteSession session)
    {
        // 创建表及索引（IF NOT EXISTS，幂等）
        await session.ExecuteAsync(Sql.Ensure);

        // 逐列迁移：旧库可能缺少后续版本新增的字段
        await EnsureColumnAsync(session, "Cameras", "SourceType",
            "ALTER TABLE Cameras ADD COLUMN SourceType INTEGER NOT NULL DEFAULT 0;");
        await EnsureColumnAsync(session, "Cameras", "LocalDeviceName",
            "ALTER TABLE Cameras ADD COLUMN LocalDeviceName TEXT NOT NULL DEFAULT '';");
    }

    /// <summary>
    /// 检查指定表是否已有目标列；缺失时执行 ALTER TABLE 添加。
    /// </summary>
    /// <param name="session">SQLite 会话。</param>
    /// <param name="table">表名。</param>
    /// <param name="column">待检查的列名。</param>
    /// <param name="alterSql">列不存在时执行的 ALTER 语句。</param>
    private static async Task EnsureColumnAsync(
        ISqliteSession session, string table, string column, string alterSql)
    {
        var info = await session.QueryAsync<PragmaColumn>($"PRAGMA table_info({table});");
        // 列已存在则跳过，避免重复 ALTER 报错
        if (info.Any(c => string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase)))
            return;
        await session.ExecuteAsync(alterSql);
    }

    /// <summary>PRAGMA table_info 查询结果的行映射。</summary>
    private sealed class PragmaColumn
    {
        /// <summary>列名。</summary>
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>Cameras 表相关的 DDL 语句常量。</summary>
    public static class Sql
    {
        /// <summary>建表及排序索引的 DDL（幂等）。</summary>
        public const string Ensure = """
            CREATE TABLE IF NOT EXISTS Cameras (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                SourceType INTEGER NOT NULL DEFAULT 0,
                RtspUrl TEXT NOT NULL DEFAULT '',
                LocalDeviceName TEXT NOT NULL DEFAULT '',
                Username TEXT NOT NULL DEFAULT '',
                Password TEXT NOT NULL DEFAULT '',
                Description TEXT NOT NULL DEFAULT '',
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsEnabled INTEGER NOT NULL DEFAULT 1
            );

            CREATE INDEX IF NOT EXISTS IX_Cameras_SortOrder_Id ON Cameras(SortOrder, Id);
            """;
    }
}
