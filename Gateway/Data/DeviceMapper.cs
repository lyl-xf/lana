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
    private readonly ISqliteSessionFactory _sessionFactory;

    public DeviceMapper(ISqliteSessionFactory sessionFactory)
    {
        _sessionFactory = sessionFactory;
    }

    public async Task<IReadOnlyList<Device>> GetAllAsync(string? name = null)
    {
        await using var session = _sessionFactory.OpenSession();
        var filter = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        return await session.QueryAsync<Device>(Sql.GetAll, new { Name = filter });
    }

    public async Task<IReadOnlyList<Device>> GetActiveAsync()
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.QueryAsync<Device>(Sql.GetActive);
    }

    public async Task<Device?> GetByIdAsync(long id)
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.QueryFirstOrDefaultAsync<Device>(Sql.GetById, new { Id = id });
    }

    public async Task<bool> ExistsAsync(long id)
    {
        await using var session = _sessionFactory.OpenSession();
        var count = await session.ExecuteScalarAsync<int>(Sql.Exists, new { Id = id });
        return count > 0;
    }

    public async Task<int> InsertAsync(Device device)
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.ExecuteAsync(Sql.Insert, ToParams(device));
    }

    public async Task<int> UpdateAsync(Device device)
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.ExecuteAsync(Sql.Update, ToParams(device));
    }

    /// <summary>
    /// 删除设备及其变量（应用层级联，同一事务）。
    /// </summary>
    public async Task<int> DeleteAsync(long id)
    {
        await using var session = _sessionFactory.OpenSession();
        var tx = await session.BeginTransactionAsync();
        try
        {
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
    public async Task UpdateDeviceIdMigrateAsync(long oldId, Device device)
    {
        ArgumentNullException.ThrowIfNull(device);

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
            // 先插入新主键行，再重映射变量，最后删除旧行（避免更新 PK 的歧义）。
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

    public static class Sql
    {
        public const string Columns = """
            Id, Name, Ip, Port, ProtocolType, PortName, BaudRate, DataBits, StopBits, Parity,
            PlcVersion, PluginConfigJson, SortOrder, PollInterval, IsActive
            """;

        public const string GetAll = $"""
            SELECT {Columns}
            FROM Devices
            WHERE (@Name IS NULL OR Name LIKE '%' || @Name || '%')
            ORDER BY SortOrder, Id;
            """;

        public const string GetActive = $"""
            SELECT {Columns}
            FROM Devices
            WHERE IsActive = 1
            ORDER BY SortOrder, Id;
            """;

        public const string GetById = $"""
            SELECT {Columns}
            FROM Devices
            WHERE Id = @Id
            LIMIT 1;
            """;

        public const string Exists = """
            SELECT COUNT(1) FROM Devices WHERE Id = @Id;
            """;

        public const string Insert = """
            INSERT INTO Devices (
                Id, Name, Ip, Port, ProtocolType, PortName, BaudRate, DataBits, StopBits, Parity,
                PlcVersion, PluginConfigJson, SortOrder, PollInterval, IsActive
            ) VALUES (
                @Id, @Name, @Ip, @Port, @ProtocolType, @PortName, @BaudRate, @DataBits, @StopBits, @Parity,
                @PlcVersion, @PluginConfigJson, @SortOrder, @PollInterval, @IsActive
            );
            """;

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

        public const string Delete = """
            DELETE FROM Devices WHERE Id = @Id;
            """;

        public const string RemapVariables = """
            UPDATE DeviceVariables SET DeviceId = @NewId WHERE DeviceId = @OldId;
            """;

        public const string DeleteAllVariables = """
            DELETE FROM DeviceVariables;
            """;

        public const string DeleteAll = """
            DELETE FROM Devices;
            """;
    }
}
