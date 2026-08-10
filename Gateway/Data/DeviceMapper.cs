using System.Data;
using Dapper;
using Lana.Data.Sqlite;
using Lana.Gateway.Models;

namespace Lana.Gateway.Data;

/// <summary>
/// 设备表 SQL Mapper（Dapper）。新增列时同步 Sql 常量与参数对象，并配合 GatewaySchema 迁移。
/// </summary>
public sealed class DeviceMapper
{
    /// <summary>SQLite 会话工厂。</summary>
    private readonly ISqliteSessionFactory _sessionFactory;

    /// <summary>
    /// 构造 Mapper。
    /// </summary>
    /// <param name="sessionFactory">SQLite 会话工厂。</param>
    public DeviceMapper(ISqliteSessionFactory sessionFactory)
    {
        _sessionFactory = sessionFactory;
    }

    /// <summary>
    /// 查询全部设备（可按名称模糊过滤）。
    /// </summary>
    /// <param name="name">可选名称关键字（LIKE 匹配）。</param>
    /// <returns>设备列表（按 SortOrder、Id 排序）。</returns>
    public async Task<IReadOnlyList<Device>> GetAllAsync(string? name = null)
    {
        await using var session = _sessionFactory.OpenSession();
        var filter = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        return await session.QueryAsync<Device>(Sql.GetAll, new { Name = filter });
    }

    /// <summary>
    /// 查询全部启用（IsActive=1）的设备。
    /// </summary>
    /// <returns>活跃设备列表。</returns>
    public async Task<IReadOnlyList<Device>> GetActiveAsync()
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.QueryAsync<Device>(Sql.GetActive);
    }

    /// <summary>
    /// 按 Id 查询单台设备。
    /// </summary>
    /// <param name="id">设备 Id。</param>
    /// <returns>设备实体；不存在时返回 null。</returns>
    public async Task<Device?> GetByIdAsync(long id)
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.QueryFirstOrDefaultAsync<Device>(Sql.GetById, new { Id = id });
    }

    /// <summary>
    /// 检查设备 Id 是否已存在。
    /// </summary>
    /// <param name="id">设备 Id。</param>
    /// <returns>存在返回 true。</returns>
    public async Task<bool> ExistsAsync(long id)
    {
        await using var session = _sessionFactory.OpenSession();
        var count = await session.ExecuteScalarAsync<int>(Sql.Exists, new { Id = id });
        return count > 0;
    }

    /// <summary>
    /// 插入新设备。
    /// </summary>
    /// <param name="device">待插入的设备（含指定 Id）。</param>
    /// <returns>受影响行数。</returns>
    public async Task<int> InsertAsync(Device device)
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.ExecuteAsync(Sql.Insert, ToParams(device));
    }

    /// <summary>
    /// 更新已有设备。
    /// </summary>
    /// <param name="device">待更新的设备。</param>
    /// <returns>受影响行数。</returns>
    public async Task<int> UpdateAsync(Device device)
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.ExecuteAsync(Sql.Update, ToParams(device));
    }

    /// <summary>
    /// 删除设备及其变量（应用层级联，同一事务）。
    /// </summary>
    /// <param name="id">设备 Id。</param>
    /// <returns>删除的设备行数。</returns>
    public async Task<int> DeleteAsync(long id)
    {
        await using var session = _sessionFactory.OpenSession();
        var tx = await session.BeginTransactionAsync();
        try
        {
            // 先删变量，再删设备（外键逻辑在应用层维护）
            await session.Connection.ExecuteAsync(
                DeviceVariableMapper.Sql.DeleteByDevice,
                new { DeviceId = id },
                tx);
            var affected = await session.Connection.ExecuteAsync(Sql.Delete, new { Id = id }, tx);
            tx.Commit();
            return affected;
        }
        catch
        {
            TryRollback(tx);
            throw;
        }
        finally
        {
            tx.Dispose();
        }
    }

    /// <summary>
    /// 变更设备主键并重映射变量 DeviceId；在同一事务中完成。
    /// </summary>
    /// <param name="oldId">原设备 Id。</param>
    /// <param name="device">含新 Id 的设备实体。</param>
    /// <returns>异步任务。</returns>
    public async Task UpdateDeviceIdMigrateAsync(long oldId, Device device)
    {
        ArgumentNullException.ThrowIfNull(device);

        // Id 未变：普通 Update 即可
        if (oldId == device.Id)
        {
            await UpdateAsync(device);
            return;
        }

        if (await ExistsAsync(device.Id))
            throw new InvalidOperationException($"设备 Id {device.Id} 已存在，无法迁移。");

        await using var session = _sessionFactory.OpenSession();
        var tx = await session.BeginTransactionAsync();
        try
        {
            // 先插入新主键行，再重映射变量，最后删除旧行（避免 SQLite 更新 PK 的歧义）
            await session.Connection.ExecuteAsync(Sql.Insert, ToParams(device), tx);
            await session.Connection.ExecuteAsync(
                Sql.RemapVariables,
                new { OldId = oldId, NewId = device.Id },
                tx);
            await session.Connection.ExecuteAsync(Sql.Delete, new { Id = oldId }, tx);
            tx.Commit();
        }
        catch
        {
            TryRollback(tx);
            throw;
        }
        finally
        {
            tx.Dispose();
        }
    }

    /// <summary>
    /// 清空全部设备与变量（备份 replaceAll 导入前使用）。
    /// </summary>
    /// <returns>异步任务。</returns>
    public async Task DeleteAllAsync()
    {
        await using var session = _sessionFactory.OpenSession();
        var tx = await session.BeginTransactionAsync();
        try
        {
            await session.Connection.ExecuteAsync(Sql.DeleteAllVariables, transaction: tx);
            await session.Connection.ExecuteAsync(Sql.DeleteAll, transaction: tx);
            tx.Commit();
        }
        catch
        {
            TryRollback(tx);
            throw;
        }
        finally
        {
            tx.Dispose();
        }
    }

    /// <summary>
    /// 将领域模型映射为 Dapper 参数（枚举/bool → 整型）。
    /// </summary>
    /// <param name="device">设备实体。</param>
    /// <returns>匿名参数对象。</returns>
    private static object ToParams(Device device) => new
    {
        device.Id,
        device.Name,
        device.Ip,
        device.Port,
        ProtocolType = (int)device.ProtocolType,
        PortName = device.PortName ?? string.Empty,
        device.BaudRate,
        device.DataBits,
        device.StopBits,
        device.Parity,
        PlcVersion = device.PlcVersion ?? string.Empty,
        PluginConfigJson = device.PluginConfigJson ?? string.Empty,
        device.SortOrder,
        device.PollInterval,
        IsActive = device.IsActive ? 1 : 0,
    };

    /// <summary>
    /// 安全回滚事务（忽略回滚本身的异常）。
    /// </summary>
    /// <param name="tx">待回滚的事务。</param>
    private static void TryRollback(IDbTransaction tx)
    {
        try
        {
            tx.Rollback();
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>设备表 SQL 语句常量。</summary>
    public static class Sql
    {
        /// <summary>SELECT 列清单。</summary>
        public const string Columns = """
            Id, Name, Ip, Port, ProtocolType, PortName, BaudRate, DataBits, StopBits, Parity,
            PlcVersion, PluginConfigJson, SortOrder, PollInterval, IsActive
            """;

        /// <summary>查询全部设备（可选名称过滤）。</summary>
        public const string GetAll = $"""
            SELECT {Columns}
            FROM Devices
            WHERE (@Name IS NULL OR Name LIKE '%' || @Name || '%')
            ORDER BY SortOrder, Id;
            """;

        /// <summary>查询活跃设备。</summary>
        public const string GetActive = $"""
            SELECT {Columns}
            FROM Devices
            WHERE IsActive = 1
            ORDER BY SortOrder, Id;
            """;

        /// <summary>按 Id 查询单台设备。</summary>
        public const string GetById = $"""
            SELECT {Columns}
            FROM Devices
            WHERE Id = @Id
            LIMIT 1;
            """;

        /// <summary>检查 Id 是否存在。</summary>
        public const string Exists = """
            SELECT COUNT(1) FROM Devices WHERE Id = @Id;
            """;

        /// <summary>插入新设备。</summary>
        public const string Insert = """
            INSERT INTO Devices (
                Id, Name, Ip, Port, ProtocolType, PortName, BaudRate, DataBits, StopBits, Parity,
                PlcVersion, PluginConfigJson, SortOrder, PollInterval, IsActive
            ) VALUES (
                @Id, @Name, @Ip, @Port, @ProtocolType, @PortName, @BaudRate, @DataBits, @StopBits, @Parity,
                @PlcVersion, @PluginConfigJson, @SortOrder, @PollInterval, @IsActive
            );
            """;

        /// <summary>按 Id 更新设备。</summary>
        public const string Update = """
            UPDATE Devices SET
                Name = @Name,
                Ip = @Ip,
                Port = @Port,
                ProtocolType = @ProtocolType,
                PortName = @PortName,
                BaudRate = @BaudRate,
                DataBits = @DataBits,
                StopBits = @StopBits,
                Parity = @Parity,
                PlcVersion = @PlcVersion,
                PluginConfigJson = @PluginConfigJson,
                SortOrder = @SortOrder,
                PollInterval = @PollInterval,
                IsActive = @IsActive
            WHERE Id = @Id;
            """;

        /// <summary>按 Id 删除设备。</summary>
        public const string Delete = """
            DELETE FROM Devices WHERE Id = @Id;
            """;

        /// <summary>迁移主键时重映射变量的 DeviceId。</summary>
        public const string RemapVariables = """
            UPDATE DeviceVariables SET DeviceId = @NewId WHERE DeviceId = @OldId;
            """;

        /// <summary>清空全部变量。</summary>
        public const string DeleteAllVariables = """
            DELETE FROM DeviceVariables;
            """;

        /// <summary>清空全部设备。</summary>
        public const string DeleteAll = """
            DELETE FROM Devices;
            """;
    }
}
