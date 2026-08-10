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
    public static Task EnsureAsync(ISqliteSession session)
        => EnsureCoreAsync(session);

    public static async Task EnsureAsync(ISqliteSessionFactory sessionFactory)
    {
        await using var session = sessionFactory.OpenSession();
        await EnsureCoreAsync(session);
    }

    private static async Task EnsureCoreAsync(ISqliteSession session)
    {
        await session.ExecuteAsync(Sql.Ensure);
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
        await DeviceOperationLogSchema.EnsureAsync(session);
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
                DefinedPageWriteValue TEXT NOT NULL DEFAULT ''
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
