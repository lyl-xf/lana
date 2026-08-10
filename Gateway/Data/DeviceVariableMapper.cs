using Lana.Data.Sqlite;
using Lana.Gateway.Models;

namespace Lana.Gateway.Data;

/// <summary>
/// 物模型变量表 SQL Mapper。含定义页字段（ShowOnDefinedPage 等）。
/// </summary>
public sealed class DeviceVariableMapper
{
    private readonly ISqliteSessionFactory _sessionFactory;

    public DeviceVariableMapper(ISqliteSessionFactory sessionFactory)
    {
        _sessionFactory = sessionFactory;
    }

    public async Task<IReadOnlyList<DeviceVariable>> GetByDeviceAsync(long deviceId)
    {
        await using var session = _sessionFactory.OpenSession();
        var rows = await session.QueryAsync<VariableRow>(Sql.GetByDevice, new { DeviceId = deviceId });
        return rows.Select(ToModel).ToList();
    }

    public async Task<DeviceVariable?> GetByIdAsync(long id)
    {
        await using var session = _sessionFactory.OpenSession();
        var row = await session.QueryFirstOrDefaultAsync<VariableRow>(Sql.GetById, new { Id = id });
        return row is null ? null : ToModel(row);
    }

    public async Task<long> InsertAsync(DeviceVariable variable)
    {
        await using var session = _sessionFactory.OpenSession();
        await session.ExecuteAsync(Sql.Insert, ToParams(variable));
        return await session.ExecuteScalarAsync<long>(Sql.LastInsertRowId);
    }

    public async Task<int> UpdateAsync(DeviceVariable variable)
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.ExecuteAsync(Sql.Update, ToParams(variable));
    }

    public async Task<int> DeleteAsync(long id)
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.ExecuteAsync(Sql.Delete, new { Id = id });
    }

    public async Task<int> DeleteByDeviceAsync(long deviceId)
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.ExecuteAsync(Sql.DeleteByDevice, new { DeviceId = deviceId });
    }

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

    public static class Sql
    {
        public const string Columns = """
            Id, DeviceId, Address, DataType, Alias, Description, ReadWrite,
            HttpKeyJsonPath, HttpValueJsonPath, ShowOnDefinedPage, DefinedPageDisplayName,
            DefinedPageOperation, DefinedPageWriteValue
            """;

        public const string GetByDevice = $"""
            SELECT {Columns}
            FROM DeviceVariables
            WHERE DeviceId = @DeviceId
            ORDER BY Id;
            """;

        public const string GetById = $"""
            SELECT {Columns}
            FROM DeviceVariables
            WHERE Id = @Id
            LIMIT 1;
            """;

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

        public const string Delete = """
            DELETE FROM DeviceVariables WHERE Id = @Id;
            """;

        public const string DeleteByDevice = """
            DELETE FROM DeviceVariables WHERE DeviceId = @DeviceId;
            """;

        public const string LastInsertRowId = """
            SELECT last_insert_rowid();
            """;
    }
}
