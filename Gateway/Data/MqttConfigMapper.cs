using Lana.Data;
using Lana.Data.Sqlite;
using Lana.Gateway.Models;

namespace Lana.Gateway.Data;

/// <summary>
/// MQTT 配置表 SQL Mapper（单行配置，取第一条）。
/// </summary>
public sealed class MqttConfigMapper
{
    /// <summary>SQLite 会话工厂。</summary>
    private readonly ISqliteSessionFactory _sessionFactory;

    /// <summary>
    /// 构造 Mapper。
    /// </summary>
    /// <param name="sessionFactory">SQLite 会话工厂。</param>
    public MqttConfigMapper(ISqliteSessionFactory sessionFactory)
    {
        _sessionFactory = sessionFactory;
    }

    /// <summary>
    /// 读取第一条 MQTT 配置（系统通常仅一行）。
    /// </summary>
    /// <returns>配置实体；无记录时返回 null。</returns>
    public async Task<MqttConfig?> GetAsync()
    {
        await using var session = _sessionFactory.OpenSession();
        var config = await session.QueryFirstOrDefaultAsync<MqttConfig>(Sql.GetFirst);
        if (config is not null)
            config.Password = LocalSecretProtector.Unprotect(config.Password);
        return config;
    }

    /// <summary>
    /// 插入或更新 MQTT 配置（无记录则 Insert，有则 Update）。
    /// </summary>
    /// <param name="config">待保存的配置。</param>
    /// <returns>异步任务。</returns>
    public async Task UpsertAsync(MqttConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var existing = await GetAsync();
        await using var session = _sessionFactory.OpenSession();

        if (existing is null)
        {
            // 首次保存：Insert 并忽略 Id（自增）
            await session.ExecuteAsync(Sql.Insert, ToParams(config));
            return;
        }

        // 已有记录：保留 Id 后 Update
        config.Id = existing.Id;
        await session.ExecuteAsync(Sql.Update, ToParams(config));
    }

    /// <summary>
    /// 将实体映射为 Dapper 参数对象（bool → 0/1）。
    /// </summary>
    /// <param name="config">MQTT 配置实体。</param>
    /// <returns>匿名参数对象。</returns>
    private static object ToParams(MqttConfig config) => new
    {
        config.Id,
        IsEnabled = config.IsEnabled ? 1 : 0,
        EnablePolling = config.EnablePolling ? 1 : 0,
        BrokerIp = config.BrokerIp ?? string.Empty,
        config.Port,
        ClientId = config.ClientId ?? string.Empty,
        Username = config.Username ?? string.Empty,
        Password = LocalSecretProtector.Protect(config.Password ?? string.Empty),
        PubTopic = config.PubTopic ?? string.Empty,
        SubTopic = config.SubTopic ?? string.Empty,
        OnlineStatusTopic = config.OnlineStatusTopic ?? string.Empty,
        config.OnlineStatusReportInterval,
        config.TelemetryPublishInterval,
    };

    /// <summary>MQTT 配置表 SQL 语句常量。</summary>
    public static class Sql
    {
        /// <summary>SELECT 列清单。</summary>
        public const string Columns = """
            Id, IsEnabled, EnablePolling, BrokerIp, Port, ClientId, Username, Password, PubTopic, SubTopic,
            OnlineStatusTopic, OnlineStatusReportInterval, TelemetryPublishInterval
            """;

        /// <summary>取第一条配置。</summary>
        public const string GetFirst = $"""
            SELECT {Columns}
            FROM MqttConfigs
            ORDER BY Id
            LIMIT 1;
            """;

        /// <summary>插入新配置。</summary>
        public const string Insert = """
            INSERT INTO MqttConfigs (
                IsEnabled, EnablePolling, BrokerIp, Port, ClientId, Username, Password, PubTopic, SubTopic,
                OnlineStatusTopic, OnlineStatusReportInterval, TelemetryPublishInterval
            ) VALUES (
                @IsEnabled, @EnablePolling, @BrokerIp, @Port, @ClientId, @Username, @Password, @PubTopic, @SubTopic,
                @OnlineStatusTopic, @OnlineStatusReportInterval, @TelemetryPublishInterval
            );
            """;

        /// <summary>按 Id 更新配置。</summary>
        public const string Update = """
            UPDATE MqttConfigs SET
                IsEnabled = @IsEnabled,
                EnablePolling = @EnablePolling,
                BrokerIp = @BrokerIp,
                Port = @Port,
                ClientId = @ClientId,
                Username = @Username,
                Password = @Password,
                PubTopic = @PubTopic,
                SubTopic = @SubTopic,
                OnlineStatusTopic = @OnlineStatusTopic,
                OnlineStatusReportInterval = @OnlineStatusReportInterval,
                TelemetryPublishInterval = @TelemetryPublishInterval
            WHERE Id = @Id;
            """;
    }
}
