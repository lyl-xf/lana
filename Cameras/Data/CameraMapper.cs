using Lana.Cameras.Models;
using Lana.Data.Sqlite;

namespace Lana.Cameras.Data;

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
        if (string.IsNullOrWhiteSpace(name))
            return await session.QueryAsync<Camera>(Sql.GetAll);

        return await session.QueryAsync<Camera>(Sql.GetByName, new { Name = $"%{name.Trim()}%" });
    }

    public async Task<IReadOnlyList<Camera>> GetEnabledAsync()
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.QueryAsync<Camera>(Sql.GetEnabled);
    }

    public async Task<Camera?> GetByIdAsync(long id)
    {
        await using var session = _sessionFactory.OpenSession();
        return await session.QueryFirstOrDefaultAsync<Camera>(Sql.GetById, new { Id = id });
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

    private static object ToParams(Camera camera) => new
    {
        camera.Id,
        Name = camera.Name ?? string.Empty,
        RtspUrl = camera.RtspUrl ?? string.Empty,
        Username = camera.Username ?? string.Empty,
        Password = camera.Password ?? string.Empty,
        Description = camera.Description ?? string.Empty,
        camera.SortOrder,
        IsEnabled = camera.IsEnabled ? 1 : 0,
    };

    public static class Sql
    {
        public const string Columns = """
            Id, Name, RtspUrl, Username, Password, Description, SortOrder, IsEnabled
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
            INSERT INTO Cameras (Name, RtspUrl, Username, Password, Description, SortOrder, IsEnabled)
            VALUES (@Name, @RtspUrl, @Username, @Password, @Description, @SortOrder, @IsEnabled);
            """;

        public const string Update = """
            UPDATE Cameras SET
                Name = @Name,
                RtspUrl = @RtspUrl,
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
