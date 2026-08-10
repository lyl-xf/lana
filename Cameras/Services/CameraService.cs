using Lana.Cameras.Data;
using Lana.Cameras.Models;
using Lana.Data.Sqlite;

namespace Lana.Cameras.Services;

/// <summary>
/// 摄像头 CRUD 与播放请求组装。定义页通过 <see cref="ListEnabledAsync"/> 拉取启用摄像头。
/// </summary>
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

    /// <summary>组装 LibVLC 播放请求（网络流或本机 dshow）。</summary>
    public static CameraPlayRequest BuildPlayRequest(Camera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);

        if (camera.SourceType == CameraSourceType.Local)
        {
            // VideoLAN.LibVLC.Windows NuGet 未包含 libdshow_plugin，无法打开本机/USB 摄像头
            throw new NotSupportedException(
                "当前播放组件不支持本机/USB 摄像头，请改用网络摄像头（RTSP/HTTP）。");
        }

        var url = BuildNetworkUrl(camera);
        return new CameraPlayRequest
        {
            Mrl = url,
            Options =
            [
                ":rtsp-tcp",
                ":network-caching=500",
                ":no-audio",
            ],
            IsLocal = false,
        };
    }

    /// <summary>兼容旧调用：仅网络流场景返回 URL；本机返回 dshow://。</summary>
    public static string BuildPlayUrl(Camera camera)
        => BuildPlayRequest(camera).Mrl;

    private static string BuildNetworkUrl(Camera camera)
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
        camera.LocalDeviceName = camera.LocalDeviceName?.Trim() ?? string.Empty;
        camera.Username = camera.Username?.Trim() ?? string.Empty;
        camera.Password ??= string.Empty;
        camera.Description = camera.Description?.Trim() ?? string.Empty;

        if (camera.SourceType == CameraSourceType.Local)
        {
            camera.RtspUrl = string.Empty;
            camera.Username = string.Empty;
            camera.Password = string.Empty;
        }
        else
        {
            camera.LocalDeviceName = string.Empty;
        }
    }

    private static void Validate(Camera camera)
    {
        if (string.IsNullOrWhiteSpace(camera.Name))
            throw new ArgumentException("摄像头名称不能为空。");

        if (camera.SourceType == CameraSourceType.Local)
            throw new ArgumentException("当前版本不支持本机/USB 摄像头，请使用网络流（RTSP/HTTP）。");

        if (string.IsNullOrWhiteSpace(camera.RtspUrl))
            throw new ArgumentException("视频流地址不能为空。");
        if (!camera.RtspUrl.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase)
            && !camera.RtspUrl.StartsWith("rtsps://", StringComparison.OrdinalIgnoreCase)
            && !camera.RtspUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !camera.RtspUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && !camera.RtspUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("网络地址需以 rtsp://、http(s):// 或 file:// 开头。");
        }
    }
}
