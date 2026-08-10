using Lana.Data.Sqlite;
using Lana.Gateway.Models;

namespace Lana.Gateway.Data;

/// <summary>
/// 设备操作历史表 SQL Mapper（写入与分页查询）。
/// </summary>
public sealed class DeviceOperationLogMapper
{
    /// <summary>SQLite 会话工厂。</summary>
    private readonly ISqliteSessionFactory _sessionFactory;

    /// <summary>
    /// 构造 Mapper。
    /// </summary>
    /// <param name="sessionFactory">SQLite 会话工厂。</param>
    public DeviceOperationLogMapper(ISqliteSessionFactory sessionFactory)
    {
        _sessionFactory = sessionFactory;
    }

    /// <summary>
    /// 插入一条操作历史记录。
    /// </summary>
    /// <param name="log">操作日志实体。</param>
    /// <returns>异步任务。</returns>
    public async Task InsertAsync(DeviceOperationLog log)
    {
        await using var session = _sessionFactory.OpenSession();
        await session.ExecuteAsync(Sql.Insert, new
        {
            // 时间统一 ISO8601 UTC 格式入库
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

    /// <summary>
    /// 分页查询操作历史（按时间倒序）。
    /// </summary>
    /// <param name="deviceId">可选：按设备 Id 过滤。</param>
    /// <param name="operation">可选：按操作类型过滤（Read/Write/ReadAll）。</param>
    /// <param name="limit">返回条数上限（1–2000，默认 200）。</param>
    /// <returns>日志列表。</returns>
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

    /// <summary>
    /// 清空全部操作历史。
    /// </summary>
    /// <returns>删除的行数。</returns>
    public async Task<int> ClearAsync()
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.ExecuteAsync(Sql.Clear);
    }

    /// <summary>
    /// 将数据库行映射为领域模型。
    /// </summary>
    /// <param name="row">Dapper 查询行。</param>
    /// <returns>DeviceOperationLog 实体。</returns>
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

    /// <summary>Dapper 映射用的内部行类型。</summary>
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

    /// <summary>操作历史表 SQL 语句常量。</summary>
    public static class Sql
    {
        /// <summary>插入日志。</summary>
        public const string Insert = """
            INSERT INTO DeviceOperationLogs (
                OccurredAtUtc, UserId, Username, Source, DeviceId, DeviceName,
                VariableId, VariableAlias, Address, Operation, DataType, Value, Success, Error
            ) VALUES (
                @OccurredAtUtc, @UserId, @Username, @Source, @DeviceId, @DeviceName,
                @VariableId, @VariableAlias, @Address, @Operation, @DataType, @Value, @Success, @Error
            );
            """;

        /// <summary>条件查询（设备/操作类型可选）。</summary>
        public const string Query = """
            SELECT Id, OccurredAtUtc, UserId, Username, Source, DeviceId, DeviceName,
                   VariableId, VariableAlias, Address, Operation, DataType, Value, Success, Error
            FROM DeviceOperationLogs
            WHERE (@DeviceId IS NULL OR DeviceId = @DeviceId)
              AND (@Operation IS NULL OR Operation = @Operation)
            ORDER BY OccurredAtUtc DESC, Id DESC
            LIMIT @Limit;
            """;

        /// <summary>清空全部日志。</summary>
        public const string Clear = "DELETE FROM DeviceOperationLogs;";
    }
}
