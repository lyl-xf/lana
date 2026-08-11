using Lana.Data.Sqlite;

namespace Lana.Gateway.Data;

/// <summary>
/// Gateway 表结构初始化与增量列迁移（Devices / DeviceVariables / MqttConfigs / 操作历史）。
/// <para>
/// 新增列模式：在 EnsureCoreAsync 中调用 EnsureColumnAsync(表, 列名, ALTER SQL)，
/// 再同步 Entity / Mapper / UI。勿直接改已有用户库的 CREATE TABLE（仅对新库生效）。
/// </para>
/// </summary>
public static class GatewaySchema
{
    /// <summary>
    /// 确保表结构存在并完成增量列迁移（使用已有会话）。
    /// </summary>
    /// <param name="session">SQLite 会话。</param>
    /// <returns>迁移任务。</returns>
    public static Task EnsureAsync(ISqliteSession session)
        => EnsureCoreAsync(session);

    /// <summary>
    /// 打开新会话并确保表结构存在。
    /// </summary>
    /// <param name="sessionFactory">SQLite 会话工厂。</param>
    /// <returns>迁移任务。</returns>
    public static async Task EnsureAsync(ISqliteSessionFactory sessionFactory)
    {
        await using var session = sessionFactory.OpenSession();
        await EnsureCoreAsync(session);
    }

    /// <summary>
    /// 核心迁移逻辑：建表 + 逐列 ALTER（兼容旧库）。
    /// </summary>
    /// <param name="session">SQLite 会话。</param>
    /// <returns>迁移任务。</returns>
    private static async Task EnsureCoreAsync(ISqliteSession session)
    {
        // 新库：CREATE TABLE IF NOT EXISTS
        await session.ExecuteAsync(Sql.Ensure);

        // 旧库增量列：PRAGMA 检查后 ALTER TABLE ADD COLUMN
        await EnsureColumnAsync(session, "MqttConfigs", "IsEnabled",
            "ALTER TABLE MqttConfigs ADD COLUMN IsEnabled INTEGER NOT NULL DEFAULT 1;");
        await EnsureColumnAsync(session, "DeviceVariables", "ShowOnDefinedPage",
            "ALTER TABLE DeviceVariables ADD COLUMN ShowOnDefinedPage INTEGER NOT NULL DEFAULT 0;");
        await EnsureColumnAsync(session, "DeviceVariables", "DefinedPageDisplayName",
            "ALTER TABLE DeviceVariables ADD COLUMN DefinedPageDisplayName TEXT NOT NULL DEFAULT '';");
        await EnsureColumnAsync(session, "DeviceVariables", "DefinedPageOperation",
            "ALTER TABLE DeviceVariables ADD COLUMN DefinedPageOperation INTEGER NOT NULL DEFAULT 0;");
        await EnsureColumnAsync(session, "DeviceVariables", "DefinedPageWriteValue",
            "ALTER TABLE DeviceVariables ADD COLUMN DefinedPageWriteValue TEXT NOT NULL DEFAULT '';");
        await EnsureColumnAsync(session, "MqttConfigs", "EnablePolling",
            "ALTER TABLE MqttConfigs ADD COLUMN EnablePolling INTEGER NOT NULL DEFAULT 1;");
        await EnsureColumnAsync(session, "MqttConfigs", "TelemetryPublishInterval",
            "ALTER TABLE MqttConfigs ADD COLUMN TelemetryPublishInterval INTEGER NOT NULL DEFAULT 0;");
        await EnsureColumnAsync(session, "DeviceVariables", "IncludeInPoll",
            "ALTER TABLE DeviceVariables ADD COLUMN IncludeInPoll INTEGER NOT NULL DEFAULT 1;");
        await EnsureColumnAsync(session, "DeviceVariables", "ShowInStatus",
            "ALTER TABLE DeviceVariables ADD COLUMN ShowInStatus INTEGER NOT NULL DEFAULT 1;");
        await EnsureColumnAsync(session, "DeviceVariables", "IncludeInTelemetry",
            "ALTER TABLE DeviceVariables ADD COLUMN IncludeInTelemetry INTEGER NOT NULL DEFAULT 1;");

        // 操作历史表（独立 Schema 类）
        await DeviceOperationLogSchema.EnsureAsync(session);
    }

    /// <summary>
    /// 若表中不存在指定列则执行 ALTER 语句（幂等迁移）。
    /// </summary>
    /// <param name="session">SQLite 会话。</param>
    /// <param name="table">表名。</param>
    /// <param name="column">列名。</param>
    /// <param name="alterSql">ALTER TABLE ADD COLUMN 语句。</param>
    /// <returns>迁移任务。</returns>
    private static async Task EnsureColumnAsync(
        ISqliteSession session, string table, string column, string alterSql)
    {
        var info = await session.QueryAsync<PragmaColumn>($"PRAGMA table_info({table});");
        if (info.Any(c => string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase)))
            return;

        await session.ExecuteAsync(alterSql);
    }

    /// <summary>PRAGMA table_info 返回的列元数据。</summary>
    private sealed class PragmaColumn
    {
        /// <summary>列名。</summary>
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>Gateway 核心表 DDL 常量。</summary>
    public static class Sql
    {
        /// <summary>Devices / DeviceVariables / MqttConfigs 建表及索引。</summary>
        public const string Ensure = """
            CREATE TABLE IF NOT EXISTS Devices (
                Id INTEGER NOT NULL PRIMARY KEY,
                Name TEXT NOT NULL,
                Ip TEXT NOT NULL,
                Port INTEGER NOT NULL,
                ProtocolType INTEGER NOT NULL,
                PortName TEXT NOT NULL DEFAULT '',
                BaudRate INTEGER NOT NULL DEFAULT 9600,
                DataBits INTEGER NOT NULL DEFAULT 8,
                StopBits INTEGER NOT NULL DEFAULT 1,
                Parity INTEGER NOT NULL DEFAULT 0,
                PlcVersion TEXT NOT NULL DEFAULT '',
                PluginConfigJson TEXT NOT NULL DEFAULT '',
                SortOrder INTEGER NOT NULL DEFAULT 0,
                PollInterval INTEGER NOT NULL DEFAULT 1000,
                IsActive INTEGER NOT NULL DEFAULT 1
            );

            CREATE INDEX IF NOT EXISTS IX_Devices_SortOrder_Id ON Devices(SortOrder, Id);
            CREATE INDEX IF NOT EXISTS IX_Devices_Name ON Devices(Name);
            CREATE INDEX IF NOT EXISTS IX_Devices_IsActive ON Devices(IsActive);

            CREATE TABLE IF NOT EXISTS DeviceVariables (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DeviceId INTEGER NOT NULL,
                Address TEXT NOT NULL,
                DataType INTEGER NOT NULL,
                Alias TEXT NOT NULL DEFAULT '',
                Description TEXT NOT NULL DEFAULT '',
                ReadWrite INTEGER NOT NULL DEFAULT 2,
                HttpKeyJsonPath TEXT NOT NULL DEFAULT '',
                HttpValueJsonPath TEXT NOT NULL DEFAULT '',
                ShowOnDefinedPage INTEGER NOT NULL DEFAULT 0,
                DefinedPageDisplayName TEXT NOT NULL DEFAULT '',
                DefinedPageOperation INTEGER NOT NULL DEFAULT 0,
                DefinedPageWriteValue TEXT NOT NULL DEFAULT '',
                IncludeInPoll INTEGER NOT NULL DEFAULT 1,
                ShowInStatus INTEGER NOT NULL DEFAULT 1,
                IncludeInTelemetry INTEGER NOT NULL DEFAULT 1
            );

            CREATE INDEX IF NOT EXISTS IX_DeviceVariables_DeviceId ON DeviceVariables(DeviceId);

            CREATE TABLE IF NOT EXISTS MqttConfigs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                BrokerIp TEXT NOT NULL DEFAULT '',
                Port INTEGER NOT NULL DEFAULT 1883,
                ClientId TEXT NOT NULL DEFAULT '',
                Username TEXT NOT NULL DEFAULT '',
                Password TEXT NOT NULL DEFAULT '',
                PubTopic TEXT NOT NULL DEFAULT '',
                SubTopic TEXT NOT NULL DEFAULT '',
                OnlineStatusTopic TEXT NOT NULL DEFAULT '',
                OnlineStatusReportInterval INTEGER NOT NULL DEFAULT 30000,
                EnablePolling INTEGER NOT NULL DEFAULT 1,
                TelemetryPublishInterval INTEGER NOT NULL DEFAULT 0
            );
            """;
    }
}
