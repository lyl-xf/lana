using Lana.Data.Sqlite;
using Lana.Gateway.Models;

namespace Lana.Gateway.Data;

public sealed class DeviceOperationLogMapper
{
    private readonly ISqliteSessionFactory _sessionFactory;

    public DeviceOperationLogMapper(ISqliteSessionFactory sessionFactory)
    {
        _sessionFactory = sessionFactory;
    }

    public async Task InsertAsync(DeviceOperationLog log)
    {
        await using var session = _sessionFactory.OpenSession();
        await session.ExecuteAsync(Sql.Insert, new
        {
            OccurredAtUtc = log.OccurredAtUtc.ToUniversalTime().ToString("O"),
            log.UserId,
            Username = log.Username ?? string.Empty,
            Source = log.Source ?? string.Empty,
            log.DeviceId,
            DeviceName = log.DeviceName ?? string.Empty,
            log.VariableId,
            VariableAlias = log.VariableAlias ?? string.Empty,
            Address = log.Address ?? string.Empty,
            Operation = log.Operation ?? string.Empty,
            DataType = log.DataType ?? string.Empty,
            log.Value,
            Success = log.Success ? 1 : 0,
            log.Error,
        });
    }

    public async Task<IReadOnlyList<DeviceOperationLog>> QueryAsync(
        long? deviceId = null,
        string? operation = null,
        int limit = 200)
    {
        await using var session = _sessionFactory.OpenSession();
        var rows = await session.QueryAsync<LogRow>(Sql.Query, new
        {
            DeviceId = deviceId,
            Operation = string.IsNullOrWhiteSpace(operation) ? null : operation.Trim(),
            Limit = Math.Clamp(limit, 1, 2000),
        });

        return rows.Select(ToModel).ToList();
    }

    public async Task<int> ClearAsync()
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.ExecuteAsync(Sql.Clear);
    }

    private static DeviceOperationLog ToModel(LogRow row)
    {
        _ = DateTime.TryParse(row.OccurredAtUtc, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out var at);
        return new DeviceOperationLog
        {
            Id = row.Id,
            OccurredAtUtc = at == default ? DateTime.UtcNow : at.ToUniversalTime(),
            UserId = row.UserId,
            Username = row.Username ?? string.Empty,
            Source = row.Source ?? string.Empty,
            DeviceId = row.DeviceId,
            DeviceName = row.DeviceName ?? string.Empty,
            VariableId = row.VariableId,
            VariableAlias = row.VariableAlias ?? string.Empty,
            Address = row.Address ?? string.Empty,
            Operation = row.Operation ?? string.Empty,
            DataType = row.DataType ?? string.Empty,
            Value = row.Value,
            Success = row.Success != 0,
            Error = row.Error,
        };
    }

    private sealed class LogRow
    {
        public long Id { get; set; }
        public string OccurredAtUtc { get; set; } = string.Empty;
        public long? UserId { get; set; }
        public string? Username { get; set; }
        public string? Source { get; set; }
        public long DeviceId { get; set; }
        public string? DeviceName { get; set; }
        public long? VariableId { get; set; }
        public string? VariableAlias { get; set; }
        public string? Address { get; set; }
        public string? Operation { get; set; }
        public string? DataType { get; set; }
        public string? Value { get; set; }
        public int Success { get; set; }
        public string? Error { get; set; }
    }

    public static class Sql
    {
        public const string Insert = """
            INSERT INTO DeviceOperationLogs (
                OccurredAtUtc, UserId, Username, Source, DeviceId, DeviceName,
                VariableId, VariableAlias, Address, Operation, DataType, Value, Success, Error
            ) VALUES (
                @OccurredAtUtc, @UserId, @Username, @Source, @DeviceId, @DeviceName,
                @VariableId, @VariableAlias, @Address, @Operation, @DataType, @Value, @Success, @Error
            );
            """;

        public const string Query = """
            SELECT Id, OccurredAtUtc, UserId, Username, Source, DeviceId, DeviceName,
                   VariableId, VariableAlias, Address, Operation, DataType, Value, Success, Error
            FROM DeviceOperationLogs
            WHERE (@DeviceId IS NULL OR DeviceId = @DeviceId)
              AND (@Operation IS NULL OR Operation = @Operation)
            ORDER BY OccurredAtUtc DESC, Id DESC
            LIMIT @Limit;
            """;

        public const string Clear = "DELETE FROM DeviceOperationLogs;";
    }
}
