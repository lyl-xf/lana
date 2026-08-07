using Lana.Data.Sqlite;

namespace Lana.Cameras.Data;

public static class CameraSchema
{
    public static Task EnsureAsync(ISqliteSession session)
        => session.ExecuteAsync(Sql.Ensure);

    public static class Sql
    {
        public const string Ensure = """
            CREATE TABLE IF NOT EXISTS Cameras (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                RtspUrl TEXT NOT NULL,
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
