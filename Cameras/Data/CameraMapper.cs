using Lana.Cameras.Models;
using Lana.Data.Sqlite;

namespace Lana.Cameras.Data;

/// <summary>
/// 摄像头表的 Dapper 数据访问层；SQL 语句集中在嵌套类 <see cref="Sql"/>。
/// </summary>
public sealed class CameraMapper
{
    private readonly ISqliteSessionFactory _sessionFactory;

    /// <summary>
    /// 通过会话工厂创建 Mapper 实例。
    /// </summary>
    /// <param name="sessionFactory">SQLite 会话工厂。</param>
    public CameraMapper(ISqliteSessionFactory sessionFactory)
    {
        _sessionFactory = sessionFactory;
    }

    /// <summary>
    /// 查询全部摄像头，可选按名称模糊过滤。
    /// </summary>
    /// <param name="name">名称关键字；为空或空白时返回全部。</param>
    /// <returns>按 SortOrder、Id 排序的摄像头列表。</returns>
    public async Task<IReadOnlyList<Camera>> GetAllAsync(string? name = null)
    {
        await using var session = _sessionFactory.OpenSession();
        var rows = string.IsNullOrWhiteSpace(name)
            ? await session.QueryAsync<CameraRow>(Sql.GetAll)
            : await session.QueryAsync<CameraRow>(Sql.GetByName, new { Name = $"%{name.Trim()}%" });
        return rows.Select(ToModel).ToList();
    }

    /// <summary>
    /// 查询所有已启用的摄像头（IsEnabled = 1）。
    /// </summary>
    /// <returns>按 SortOrder、Id 排序的启用摄像头列表。</returns>
    public async Task<IReadOnlyList<Camera>> GetEnabledAsync()
    {
        await using var session = _sessionFactory.OpenSession();
        var rows = await session.QueryAsync<CameraRow>(Sql.GetEnabled);
        return rows.Select(ToModel).ToList();
    }

    /// <summary>
    /// 按主键 Id 查询单个摄像头。
    /// </summary>
    /// <param name="id">摄像头主键。</param>
    /// <returns>找到则返回实体，否则 <c>null</c>。</returns>
    public async Task<Camera?> GetByIdAsync(long id)
    {
        await using var session = _sessionFactory.OpenSession();
        var row = await session.QueryFirstOrDefaultAsync<CameraRow>(Sql.GetById, new { Id = id });
        return row is null ? null : ToModel(row);
    }

    /// <summary>
    /// 插入新摄像头记录。
    /// </summary>
    /// <param name="camera">待插入的实体（Id 由数据库生成）。</param>
    /// <returns>新插入行的自增 Id。</returns>
    public async Task<long> InsertAsync(Camera camera)
    {
        await using var session = _sessionFactory.OpenSession();
        await session.ExecuteAsync(Sql.Insert, ToParams(camera));
        return await session.ExecuteScalarAsync<long>("SELECT last_insert_rowid();");
    }

    /// <summary>
    /// 按 Id 更新摄像头记录。
    /// </summary>
    /// <param name="camera">含有效 Id 的完整实体。</param>
    public async Task UpdateAsync(Camera camera)
    {
        await using var session = _sessionFactory.OpenSession();
        await session.ExecuteAsync(Sql.Update, ToParams(camera));
    }

    /// <summary>
    /// 按 Id 删除摄像头记录。
    /// </summary>
    /// <param name="id">待删除的主键。</param>
    public async Task DeleteAsync(long id)
    {
        await using var session = _sessionFactory.OpenSession();
        await session.ExecuteAsync(Sql.Delete, new { Id = id });
    }

    /// <summary>
    /// 将数据库行映射为领域模型，处理可空字符串与布尔转换。
    /// </summary>
    /// <param name="row">Dapper 查询得到的行对象。</param>
    /// <returns><see cref="Camera"/> 实体。</returns>
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
        IsEnabled = row.IsEnabled != 0, // SQLite 以 INTEGER 0/1 存储布尔
    };

    /// <summary>
    /// 将领域模型转换为 Dapper 参数字典，供 INSERT/UPDATE 使用。
    /// </summary>
    /// <param name="camera">源实体。</param>
    /// <returns>匿名参数对象。</returns>
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

    /// <summary>Cameras 表查询结果的 Dapper 行映射（字段可空以兼容数据库 NULL）。</summary>
    private sealed class CameraRow
    {
        /// <summary>主键 Id。</summary>
        public long Id { get; set; }

        /// <summary>摄像头名称。</summary>
        public string? Name { get; set; }

        /// <summary>来源类型整型值（对应 <see cref="CameraSourceType"/>）。</summary>
        public int SourceType { get; set; }

        /// <summary>网络流地址。</summary>
        public string? RtspUrl { get; set; }

        /// <summary>本机设备名称。</summary>
        public string? LocalDeviceName { get; set; }

        /// <summary>认证用户名。</summary>
        public string? Username { get; set; }

        /// <summary>认证密码。</summary>
        public string? Password { get; set; }

        /// <summary>备注说明。</summary>
        public string? Description { get; set; }

        /// <summary>排序权重。</summary>
        public int SortOrder { get; set; }

        /// <summary>启用标志（0 = 禁用，1 = 启用）。</summary>
        public int IsEnabled { get; set; }
    }

    /// <summary>Cameras 表 CRUD 相关的 SQL 常量。</summary>
    public static class Sql
    {
        /// <summary>SELECT 列清单，供各查询语句复用。</summary>
        public const string Columns = """
            Id, Name, SourceType, RtspUrl, LocalDeviceName, Username, Password, Description, SortOrder, IsEnabled
            """;

        /// <summary>查询全部摄像头，按 SortOrder、Id 排序。</summary>
        public const string GetAll = $"""
            SELECT {Columns} FROM Cameras
            ORDER BY SortOrder, Id;
            """;

        /// <summary>查询已启用摄像头，按 SortOrder、Id 排序。</summary>
        public const string GetEnabled = $"""
            SELECT {Columns} FROM Cameras
            WHERE IsEnabled = 1
            ORDER BY SortOrder, Id;
            """;

        /// <summary>按名称 LIKE 模糊查询。</summary>
        public const string GetByName = $"""
            SELECT {Columns} FROM Cameras
            WHERE Name LIKE @Name
            ORDER BY SortOrder, Id;
            """;

        /// <summary>按 Id 精确查询单条记录。</summary>
        public const string GetById = $"""
            SELECT {Columns} FROM Cameras
            WHERE Id = @Id
            LIMIT 1;
            """;

        /// <summary>插入新摄像头（不含 Id，由数据库自增）。</summary>
        public const string Insert = """
            INSERT INTO Cameras (
                Name, SourceType, RtspUrl, LocalDeviceName, Username, Password, Description, SortOrder, IsEnabled
            ) VALUES (
                @Name, @SourceType, @RtspUrl, @LocalDeviceName, @Username, @Password, @Description, @SortOrder, @IsEnabled
            );
            """;

        /// <summary>按 Id 更新全部可写字段。</summary>
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

        /// <summary>按 Id 删除摄像头。</summary>
        public const string Delete = """
            DELETE FROM Cameras WHERE Id = @Id;
            """;
    }
}
