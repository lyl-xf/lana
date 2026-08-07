using Lana.Data.Sqlite;
using Lana.Gateway.Models;

namespace Lana.Gateway.Data;

/// <summary>
/// MQTT 配置表 SQL Mapper（单行配置，取第一条）。
/// </summary>
public sealed class MqttConfigMapper
{
    private readonly ISqliteSessionFactory _sessionFactory;

    public MqttConfigMapper(ISqliteSessionFactory sessionFactory)
    {
        _sessionFactory = sessionFactory;
    }

    public async Task<MqttConfig?> GetAsync()
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.QueryFirstOrDefaultAsync<MqttConfig>(Sql.GetFirst);
    }

    public async Task UpsertAsync(MqttConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var existing = await GetAsync();
        await using var session = _sessionFactory.OpenSession();

        if (existing is null)
        {
            await session.ExecuteAsync(Sql.Insert, ToParams(config));
            return;
        }

        config.Id = existing.Id;
        await session.ExecuteAsync(Sql.Update, ToParams(config));
    }

    private static object ToParams(MqttConfig config) => new
    {
        config.Id,
        IsEnabled = config.IsEnabled ? 1 : 0,
        BrokerIp = config.BrokerIp ?? string.Empty,
        config.Port,
        ClientId = config.ClientId ?? string.Empty,
        Username = config.Username ?? string.Empty,
        Password = config.Password ?? string.Empty,
        PubTopic = config.PubTopic ?? string.Empty,
        SubTopic = config.SubTopic ?? string.Empty,
        OnlineStatusTopic = config.OnlineStatusTopic ?? string.Empty,
        config.OnlineStatusReportInterval,
    };

    public static class Sql
    {
        public const string Columns = """
            Id, IsEnabled, BrokerIp, Port, ClientId, Username, Password, PubTopic, SubTopic,
            OnlineStatusTopic, OnlineStatusReportInterval
            """;

        public const string GetFirst = $"""
            SELECT {Columns}
            FROM MqttConfigs
            ORDER BY Id
            LIMIT 1;
            """;

        public const string Insert = """
            INSERT INTO MqttConfigs (
                IsEnabled, BrokerIp, Port, ClientId, Username, Password, PubTopic, SubTopic,
                OnlineStatusTopic, OnlineStatusReportInterval
            ) VALUES (
                @IsEnabled, @BrokerIp, @Port, @ClientId, @Username, @Password, @PubTopic, @SubTopic,
                @OnlineStatusTopic, @OnlineStatusReportInterval
            );
            """;

        public const string Update = """
            UPDATE MqttConfigs SET
                IsEnabled = @IsEnabled,
                BrokerIp = @BrokerIp,
                Port = @Port,
                ClientId = @ClientId,
                Username = @Username,
                Password = @Password,
                PubTopic = @PubTopic,
                SubTopic = @SubTopic,
                OnlineStatusTopic = @OnlineStatusTopic,
                OnlineStatusReportInterval = @OnlineStatusReportInterval
            WHERE Id = @Id;
            """;
    }
}
