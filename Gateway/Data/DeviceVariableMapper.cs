using Lana.Data.Sqlite;
using Lana.Gateway.Models;

namespace Lana.Gateway.Data;

/// <summary>
/// 物模型变量表 SQL Mapper。含定义页字段（ShowOnDefinedPage 等）。
/// </summary>
public sealed class DeviceVariableMapper
{
    /// <summary>SQLite 会话工厂。</summary>
    private readonly ISqliteSessionFactory _sessionFactory;

    /// <summary>
    /// 构造 Mapper。
    /// </summary>
    /// <param name="sessionFactory">SQLite 会话工厂。</param>
    public DeviceVariableMapper(ISqliteSessionFactory sessionFactory)
    {
        _sessionFactory = sessionFactory;
    }

    /// <summary>
    /// 按设备 Id 查询其全部变量。
    /// </summary>
    /// <param name="deviceId">设备 Id。</param>
    /// <returns>变量列表（按 Id 排序）。</returns>
    public async Task<IReadOnlyList<DeviceVariable>> GetByDeviceAsync(long deviceId)
    {
        await using var session = _sessionFactory.OpenSession();
        var rows = await session.QueryAsync<VariableRow>(Sql.GetByDevice, new { DeviceId = deviceId });
        return rows.Select(ToModel).ToList();
    }

    /// <summary>
    /// 按变量 Id 查询单条记录。
    /// </summary>
    /// <param name="id">变量 Id。</param>
    /// <returns>变量实体；不存在时返回 null。</returns>
    public async Task<DeviceVariable?> GetByIdAsync(long id)
    {
        await using var session = _sessionFactory.OpenSession();
        var row = await session.QueryFirstOrDefaultAsync<VariableRow>(Sql.GetById, new { Id = id });
        return row is null ? null : ToModel(row);
    }

    /// <summary>
    /// 插入新变量并返回自增 Id。
    /// </summary>
    /// <param name="variable">待插入的变量。</param>
    /// <returns>新插入行的 Id。</returns>
    public async Task<long> InsertAsync(DeviceVariable variable)
    {
        await using var session = _sessionFactory.OpenSession();
        await session.ExecuteAsync(Sql.Insert, ToParams(variable));
        return await session.ExecuteScalarAsync<long>(Sql.LastInsertRowId);
    }

    /// <summary>
    /// 更新已有变量。
    /// </summary>
    /// <param name="variable">待更新的变量（需含 Id）。</param>
    /// <returns>受影响行数。</returns>
    public async Task<int> UpdateAsync(DeviceVariable variable)
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.ExecuteAsync(Sql.Update, ToParams(variable));
    }

    /// <summary>
    /// 按 Id 删除单条变量。
    /// </summary>
    /// <param name="id">变量 Id。</param>
    /// <returns>受影响行数。</returns>
    public async Task<int> DeleteAsync(long id)
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.ExecuteAsync(Sql.Delete, new { Id = id });
    }

    /// <summary>
    /// 删除指定设备下的全部变量。
    /// </summary>
    /// <param name="deviceId">设备 Id。</param>
    /// <returns>受影响行数。</returns>
    public async Task<int> DeleteByDeviceAsync(long deviceId)
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.ExecuteAsync(Sql.DeleteByDevice, new { DeviceId = deviceId });
    }

    /// <summary>
    /// 将数据库行映射为领域模型（枚举/布尔转换）。
    /// </summary>
    /// <param name="row">Dapper 查询行。</param>
    /// <returns>DeviceVariable 实体。</returns>
    private static DeviceVariable ToModel(VariableRow row) => new()
    {
        Id = row.Id,
        DeviceId = row.DeviceId,
        Address = row.Address ?? string.Empty,
        DataType = (DataType)row.DataType,
        Alias = row.Alias ?? string.Empty,
        Description = row.Description ?? string.Empty,
        ReadWrite = (ReadWriteAccess)row.ReadWrite,
        HttpKeyJsonPath = row.HttpKeyJsonPath ?? string.Empty,
        HttpValueJsonPath = row.HttpValueJsonPath ?? string.Empty,
        ShowOnDefinedPage = row.ShowOnDefinedPage != 0,
        DefinedPageDisplayName = row.DefinedPageDisplayName ?? string.Empty,
        DefinedPageOperation = (DefinedPageOperation)row.DefinedPageOperation,
        DefinedPageWriteValue = row.DefinedPageWriteValue ?? string.Empty,
    };

    /// <summary>
    /// 将领域模型映射为 Dapper 参数（枚举/布尔 → 整型）。
    /// </summary>
    /// <param name="variable">变量实体。</param>
    /// <returns>匿名参数对象。</returns>
    private static object ToParams(DeviceVariable variable) => new
    {
        variable.Id,
        variable.DeviceId,
        Address = variable.Address ?? string.Empty,
        DataType = (int)variable.DataType,
        Alias = variable.Alias ?? string.Empty,
        Description = variable.Description ?? string.Empty,
        ReadWrite = (int)variable.ReadWrite,
        HttpKeyJsonPath = variable.HttpKeyJsonPath ?? string.Empty,
        HttpValueJsonPath = variable.HttpValueJsonPath ?? string.Empty,
        ShowOnDefinedPage = variable.ShowOnDefinedPage ? 1 : 0,
        DefinedPageDisplayName = variable.DefinedPageDisplayName ?? string.Empty,
        DefinedPageOperation = (int)variable.DefinedPageOperation,
        DefinedPageWriteValue = variable.DefinedPageWriteValue ?? string.Empty,
    };

    /// <summary>Dapper 映射用的内部行类型。</summary>
    private sealed class VariableRow
    {
        public long Id { get; set; }
        public long DeviceId { get; set; }
        public string? Address { get; set; }
        public int DataType { get; set; }
        public string? Alias { get; set; }
        public string? Description { get; set; }
        public int ReadWrite { get; set; }
        public string? HttpKeyJsonPath { get; set; }
        public string? HttpValueJsonPath { get; set; }
        public int ShowOnDefinedPage { get; set; }
        public string? DefinedPageDisplayName { get; set; }
        public int DefinedPageOperation { get; set; }
        public string? DefinedPageWriteValue { get; set; }
    }

    /// <summary>变量表 SQL 语句常量。</summary>
    public static class Sql
    {
        /// <summary>SELECT 列清单。</summary>
        public const string Columns = """
            Id, DeviceId, Address, DataType, Alias, Description, ReadWrite,
            HttpKeyJsonPath, HttpValueJsonPath, ShowOnDefinedPage, DefinedPageDisplayName,
            DefinedPageOperation, DefinedPageWriteValue
            """;

        /// <summary>按设备 Id 查询变量。</summary>
        public const string GetByDevice = $"""
            SELECT {Columns}
            FROM DeviceVariables
            WHERE DeviceId = @DeviceId
            ORDER BY Id;
            """;

        /// <summary>按 Id 查询单条变量。</summary>
        public const string GetById = $"""
            SELECT {Columns}
            FROM DeviceVariables
            WHERE Id = @Id
            LIMIT 1;
            """;

        /// <summary>插入新变量。</summary>
        public const string Insert = """
            INSERT INTO DeviceVariables (
                DeviceId, Address, DataType, Alias, Description, ReadWrite,
                HttpKeyJsonPath, HttpValueJsonPath, ShowOnDefinedPage, DefinedPageDisplayName,
                DefinedPageOperation, DefinedPageWriteValue
            ) VALUES (
                @DeviceId, @Address, @DataType, @Alias, @Description, @ReadWrite,
                @HttpKeyJsonPath, @HttpValueJsonPath, @ShowOnDefinedPage, @DefinedPageDisplayName,
                @DefinedPageOperation, @DefinedPageWriteValue
            );
            """;

        /// <summary>按 Id 更新变量。</summary>
        public const string Update = """
            UPDATE DeviceVariables SET
                DeviceId = @DeviceId,
                Address = @Address,
                DataType = @DataType,
                Alias = @Alias,
                Description = @Description,
                ReadWrite = @ReadWrite,
                HttpKeyJsonPath = @HttpKeyJsonPath,
                HttpValueJsonPath = @HttpValueJsonPath,
                ShowOnDefinedPage = @ShowOnDefinedPage,
                DefinedPageDisplayName = @DefinedPageDisplayName,
                DefinedPageOperation = @DefinedPageOperation,
                DefinedPageWriteValue = @DefinedPageWriteValue
            WHERE Id = @Id;
            """;

        /// <summary>按 Id 删除变量。</summary>
        public const string Delete = """
            DELETE FROM DeviceVariables WHERE Id = @Id;
            """;

        /// <summary>按设备 Id 删除全部变量。</summary>
        public const string DeleteByDevice = """
            DELETE FROM DeviceVariables WHERE DeviceId = @DeviceId;
            """;

        /// <summary>获取最后插入行的 Id。</summary>
        public const string LastInsertRowId = """
            SELECT last_insert_rowid();
            """;
    }
}
