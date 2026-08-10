using Lana.Data.Sqlite;

namespace Lana.Gateway.Data;

/// <summary>
/// 设备操作历史表结构初始化（DeviceOperationLogs）。
/// </summary>
public static class DeviceOperationLogSchema
{
    /// <summary>
    /// 确保操作历史表及索引存在。
    /// </summary>
    /// <param name="session">SQLite 会话。</param>
    /// <returns>执行 DDL 的任务。</returns>
    public static Task EnsureAsync(ISqliteSession session)
        => session.ExecuteAsync(Sql.Ensure);

    /// <summary>操作历史表 DDL 与索引 SQL 常量。</summary>
    public static class Sql
    {
        /// <summary>建表及索引语句。</summary>
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
