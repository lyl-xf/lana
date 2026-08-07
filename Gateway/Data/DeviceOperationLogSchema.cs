using Lana.Data.Sqlite;

namespace Lana.Gateway.Data;

public static class DeviceOperationLogSchema
{
    public static Task EnsureAsync(ISqliteSession session)
        => session.ExecuteAsync(Sql.Ensure);

    public static class Sql
    {
        public const string Ensure = """
            CREATE TABLE IF NOT EXISTS DeviceOperationLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OccurredAtUtc TEXT NOT NULL,
                UserId INTEGER NULL,
                Username TEXT NOT NULL DEFAULT '',
                Source TEXT NOT NULL DEFAULT '',
                DeviceId INTEGER NOT NULL,
                DeviceName TEXT NOT NULL DEFAULT '',
                VariableId INTEGER NULL,
                VariableAlias TEXT NOT NULL DEFAULT '',
                Address TEXT NOT NULL DEFAULT '',
                Operation TEXT NOT NULL,
                DataType TEXT NOT NULL DEFAULT '',
                Value TEXT NULL,
                Success INTEGER NOT NULL,
                Error TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_DeviceOperationLogs_OccurredAtUtc
                ON DeviceOperationLogs(OccurredAtUtc DESC);
            CREATE INDEX IF NOT EXISTS IX_DeviceOperationLogs_DeviceId
                ON DeviceOperationLogs(DeviceId);
            """;
    }
}
