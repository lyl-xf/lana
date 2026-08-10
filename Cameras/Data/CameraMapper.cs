using Lana.Cameras.Models;
using Lana.Data.Sqlite;

namespace Lana.Cameras.Data;

/// <summary>摄像头表 Dapper 访问；SQL 集中在嵌套类 Sql。</summary>
public sealed class CameraMapper
{
    private readonly ISqliteSessionFactory _sessionFactory;

    public CameraMapper(ISqliteSessionFactory sessionFactory)
    {
        _sessionFactory = sessionFactory;
    }

    public async Task<IReadOnlyList<Camera>> GetAllAsync(string? name = null)
    {
        await using var session = _sessionFactory.OpenSession();
        var rows = string.IsNullOrWhiteSpace(name)
            ? await session.QueryAsync<CameraRow>(Sql.GetAll)
            : await session.QueryAsync<CameraRow>(Sql.GetByName, new { Name = $"%{name.Trim()}%" });
        return rows.Select(ToModel).ToList();
    }

    public async Task<IReadOnlyList<Camera>> GetEnabledAsync()
    {
        await using var session = _sessionFactory.OpenSession();
        var rows = await session.QueryAsync<CameraRow>(Sql.GetEnabled);
        return rows.Select(ToModel).ToList();
    }

    public async Task<Camera?> GetByIdAsync(long id)
    {
        await using var session = _sessionFactory.OpenSession();
        var row = await session.QueryFirstOrDefaultAsync<CameraRow>(Sql.GetById, new { Id = id });
        return row is null ? null : ToModel(row);
    }

    public async Task<long> InsertAsync(Camera camera)
    {
        await using var session = _sessionFactory.OpenSession();
        await session.ExecuteAsync(Sql.Insert, ToParams(camera));
        return await session.ExecuteScalarAsync<long>("SELECT last_insert_rowid();");
    }

    public async Task UpdateAsync(Camera camera)
    {
        await using var session = _sessionFactory.OpenSession();
        await session.ExecuteAsync(Sql.Update, ToParams(camera));
    }

    public async Task DeleteAsync(long id)
    {
        await using var session = _sessionFactory.OpenSession();
        await session.ExecuteAsync(Sql.Delete, new { Id = id });
    }

    private static Camera ToModel(CameraRow row) => new()
    {
        Id = row.Id,
        Name = row.Name ?? string.Empty,
        SourceType = (CameraSourceType)row.SourceType,
        RtspUrl = row.RtspUrl ?? string.Empty,
        LocalDeviceName = row.LocalDeviceName ?? string.Empty,
        Username = row.Username ?? string.Empty,
        Password = row.Password ?? string.Empty,
        Description = row.Description ?? string.Empty,
        SortOrder = row.SortOrder,
        IsEnabled = row.IsEnabled != 0,
    };

    private static object ToParams(Camera camera) => new
    {
        camera.Id,
        Name = camera.Name ?? string.Empty,
        SourceType = (int)camera.SourceType,
        RtspUrl = camera.RtspUrl ?? string.Empty,
        LocalDeviceName = camera.LocalDeviceName ?? string.Empty,
        Username = camera.Username ?? string.Empty,
        Password = camera.Password ?? string.Empty,
        Description = camera.Description ?? string.Empty,
        camera.SortOrder,
        IsEnabled = camera.IsEnabled ? 1 : 0,
    };

    private sealed class CameraRow
    {
        public long Id { get; set; }
        public string? Name { get; set; }
        public int SourceType { get; set; }
        public string? RtspUrl { get; set; }
        public string? LocalDeviceName { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? Description { get; set; }
        public int SortOrder { get; set; }
        public int IsEnabled { get; set; }
    }

    public static class Sql
    {
        public const string Columns = """
            Id, Name, SourceType, RtspUrl, LocalDeviceName, Username, Password, Description, SortOrder, IsEnabled
            """;

        public const string GetAll = $"""
            SELECT {Columns} FROM Cameras
            ORDER BY SortOrder, Id;
            """;

        public const string GetEnabled = $"""
            SELECT {Columns} FROM Cameras
            WHERE IsEnabled = 1
            ORDER BY SortOrder, Id;
            """;

        public const string GetByName = $"""
            SELECT {Columns} FROM Cameras
            WHERE Name LIKE @Name
            ORDER BY SortOrder, Id;
            """;

        public const string GetById = $"""
            SELECT {Columns} FROM Cameras
            WHERE Id = @Id
            LIMIT 1;
            """;

        public const string Insert = """
            INSERT INTO Cameras (
                Name, SourceType, RtspUrl, LocalDeviceName, Username, Password, Description, SortOrder, IsEnabled
            ) VALUES (
                @Name, @SourceType, @RtspUrl, @LocalDeviceName, @Username, @Password, @Description, @SortOrder, @IsEnabled
            );
            """;

        public const string Update = """
            UPDATE Cameras SET
                Name = @Name,
                SourceType = @SourceType,
                RtspUrl = @RtspUrl,
                LocalDeviceName = @LocalDeviceName,
                Username = @Username,
                Password = @Password,
                Description = @Description,
                SortOrder = @SortOrder,
                IsEnabled = @IsEnabled
            WHERE Id = @Id;
            """;

        public const string Delete = """
            DELETE FROM Cameras WHERE Id = @Id;
            """;
    }
}
