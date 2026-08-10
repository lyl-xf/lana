using Lana.Data.Sqlite;
using Lana.Gateway.Data;
using Lana.Gateway.Models;

namespace Lana.Gateway.Services;

/// <summary>
/// 设备操作历史写入与查询（历史数据页）。通常由 DeviceDebugApi 在读写后 Insert。
/// </summary>
public sealed class DeviceOperationHistoryService
{
    /// <summary>操作历史 Mapper。</summary>
    private readonly DeviceOperationLogMapper _logs;

    /// <summary>
    /// 构造服务。
    /// </summary>
    /// <param name="sessionFactory">SQLite 会话工厂。</param>
    public DeviceOperationHistoryService(ISqliteSessionFactory sessionFactory)
    {
        _logs = new DeviceOperationLogMapper(sessionFactory);
    }

    /// <summary>
    /// 插入一条操作历史。
    /// </summary>
    /// <param name="log">日志实体。</param>
    /// <returns>异步任务。</returns>
    public Task InsertAsync(DeviceOperationLog log)
        => _logs.InsertAsync(log);

    /// <summary>
    /// 分页查询操作历史。
    /// </summary>
    /// <param name="deviceId">可选设备 Id 过滤。</param>
    /// <param name="operation">可选操作类型过滤。</param>
    /// <param name="limit">返回条数上限。</param>
    /// <returns>日志列表。</returns>
    public Task<IReadOnlyList<DeviceOperationLog>> QueryAsync(
        long? deviceId = null,
        string? operation = null,
        int limit = 200)
        => _logs.QueryAsync(deviceId, operation, limit);

    /// <summary>
    /// 清空全部操作历史。
    /// </summary>
    /// <returns>删除的行数。</returns>
    public Task<int> ClearAsync()
        => _logs.ClearAsync();
}
