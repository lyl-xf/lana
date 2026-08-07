using Lana.Data.Sqlite;
using Lana.Gateway.Data;
using Lana.Gateway.Models;

namespace Lana.Gateway.Services;

/// <summary>
/// 设备操作历史查询 / 清理。
/// </summary>
public sealed class DeviceOperationHistoryService
{
    private readonly DeviceOperationLogMapper _logs;

    public DeviceOperationHistoryService(ISqliteSessionFactory sessionFactory)
    {
        _logs = new DeviceOperationLogMapper(sessionFactory);
    }

    public Task InsertAsync(DeviceOperationLog log)
        => _logs.InsertAsync(log);

    public Task<IReadOnlyList<DeviceOperationLog>> QueryAsync(
        long? deviceId = null,
        string? operation = null,
        int limit = 200)
        => _logs.QueryAsync(deviceId, operation, limit);

    public Task<int> ClearAsync()
        => _logs.ClearAsync();
}
