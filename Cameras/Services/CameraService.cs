using Lana.Cameras.Data;
using Lana.Cameras.Models;
using Lana.Data.Sqlite;

namespace Lana.Cameras.Services;

public sealed class CameraService
{
    private readonly CameraMapper _mapper;

    public CameraService(ISqliteSessionFactory sessionFactory)
    {
        _mapper = new CameraMapper(sessionFactory);
    }

    public Task<IReadOnlyList<Camera>> ListAsync(string? name = null)
        => _mapper.GetAllAsync(name);

    public Task<IReadOnlyList<Camera>> ListEnabledAsync()
        => _mapper.GetEnabledAsync();

    public Task<Camera?> GetAsync(long id)
        => _mapper.GetByIdAsync(id);

    public async Task<long> CreateAsync(Camera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        Normalize(camera);
        Validate(camera);
        var id = await _mapper.InsertAsync(camera);
        camera.Id = id;
        return id;
    }

    public async Task UpdateAsync(Camera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        if (camera.Id <= 0)
            throw new ArgumentException("摄像头 Id 无效。");
        Normalize(camera);
        Validate(camera);
        await _mapper.UpdateAsync(camera);
    }

    public Task DeleteAsync(long id)
        => _mapper.DeleteAsync(id);

    /// <summary>组装可播放的 RTSP 地址（含可选账号密码）。</summary>
    public static string BuildPlayUrl(Camera camera)
    {
        var url = (camera.RtspUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        if (string.IsNullOrWhiteSpace(camera.Username))
            return url;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;

        if (!string.IsNullOrEmpty(uri.UserInfo))
            return url;

        var builder = new UriBuilder(uri)
        {
            UserName = Uri.EscapeDataString(camera.Username.Trim()),
            Password = Uri.EscapeDataString(camera.Password ?? string.Empty),
        };
        return builder.Uri.ToString();
    }

    private static void Normalize(Camera camera)
    {
        camera.Name = camera.Name?.Trim() ?? string.Empty;
        camera.RtspUrl = camera.RtspUrl?.Trim() ?? string.Empty;
        camera.Username = camera.Username?.Trim() ?? string.Empty;
        camera.Password ??= string.Empty;
        camera.Description = camera.Description?.Trim() ?? string.Empty;
    }

    private static void Validate(Camera camera)
    {
        if (string.IsNullOrWhiteSpace(camera.Name))
            throw new ArgumentException("摄像头名称不能为空。");
        if (string.IsNullOrWhiteSpace(camera.RtspUrl))
            throw new ArgumentException("RTSP 地址不能为空。");
        if (!camera.RtspUrl.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase)
            && !camera.RtspUrl.StartsWith("rtsps://", StringComparison.OrdinalIgnoreCase)
            && !camera.RtspUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !camera.RtspUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && !camera.RtspUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("地址需以 rtsp://、http(s):// 或 file:// 开头。");
        }
    }
}
