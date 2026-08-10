using Lana.Data.Sqlite;

namespace Lana.Cameras.Data;

/// <summary>
/// Cameras 表初始化与列迁移。新增字段：EnsureColumnAsync + Entity + Mapper + Service。
/// </summary>
public static class CameraSchema
{
    public static async Task EnsureAsync(ISqliteSession session)
    {
        await session.ExecuteAsync(Sql.Ensure);
        await EnsureColumnAsync(session, "Cameras", "SourceType",
            "ALTER TABLE Cameras ADD COLUMN SourceType INTEGER NOT NULL DEFAULT 0;");
        await EnsureColumnAsync(session, "Cameras", "LocalDeviceName",
            "ALTER TABLE Cameras ADD COLUMN LocalDeviceName TEXT NOT NULL DEFAULT '';");
    }

    private static async Task EnsureColumnAsync(
        ISqliteSession session, string table, string column, string alterSql)
    {
        var info = await session.QueryAsync<PragmaColumn>($"PRAGMA table_info({table});");
        if (info.Any(c => string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase)))
            return;
        await session.ExecuteAsync(alterSql);
    }

    private sealed class PragmaColumn
    {
        public string Name { get; set; } = string.Empty;
    }

    public static class Sql
    {
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
