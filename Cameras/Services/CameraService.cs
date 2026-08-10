using Lana.Cameras.Data;
using Lana.Cameras.Models;
using Lana.Data.Sqlite;

namespace Lana.Cameras.Services;

/// <summary>
/// 摄像头业务服务：CRUD 操作、字段规范化与校验、LibVLC 播放请求组装。
/// 定义页通过 <see cref="ListEnabledAsync"/> 拉取启用摄像头供预览使用。
/// </summary>
public sealed class CameraService
{
    private readonly CameraMapper _mapper;

    /// <summary>
    /// 通过 SQLite 会话工厂初始化服务，内部创建 <see cref="CameraMapper"/>。
    /// </summary>
    /// <param name="sessionFactory">SQLite 会话工厂。</param>
    public CameraService(ISqliteSessionFactory sessionFactory)
    {
        _mapper = new CameraMapper(sessionFactory);
    }

    /// <summary>
    /// 列出全部摄像头，可选按名称模糊过滤。
    /// </summary>
    /// <param name="name">名称关键字；为空时返回全部。</param>
    public Task<IReadOnlyList<Camera>> ListAsync(string? name = null)
        => _mapper.GetAllAsync(name);

    /// <summary>
    /// 列出所有已启用的摄像头，供预览页使用。
    /// </summary>
    public Task<IReadOnlyList<Camera>> ListEnabledAsync()
        => _mapper.GetEnabledAsync();

    /// <summary>
    /// 按 Id 获取单个摄像头。
    /// </summary>
    /// <param name="id">摄像头主键。</param>
    public Task<Camera?> GetAsync(long id)
        => _mapper.GetByIdAsync(id);

    /// <summary>
    /// 创建新摄像头：规范化 → 校验 → 入库，并将生成的 Id 写回实体。
    /// </summary>
    /// <param name="camera">待创建的实体。</param>
    /// <returns>新插入行的自增 Id。</returns>
    public async Task<long> CreateAsync(Camera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        Normalize(camera);
        Validate(camera);
        var id = await _mapper.InsertAsync(camera);
        camera.Id = id;
        return id;
    }

    /// <summary>
    /// 更新已有摄像头：规范化 → 校验 → 写库。
    /// </summary>
    /// <param name="camera">含有效 Id 的完整实体。</param>
    public async Task UpdateAsync(Camera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        if (camera.Id <= 0)
            throw new ArgumentException("摄像头 Id 无效。");
        Normalize(camera);
        Validate(camera);
        await _mapper.UpdateAsync(camera);
    }

    /// <summary>
    /// 按 Id 删除摄像头。
    /// </summary>
    /// <param name="id">待删除的主键。</param>
    public Task DeleteAsync(long id)
        => _mapper.DeleteAsync(id);

    /// <summary>
    /// 将摄像头配置组装为 LibVLC 播放请求（网络流或本机 dshow）。
    /// 当前版本仅支持网络流；本机源会抛出 <see cref="NotSupportedException"/>。
    /// </summary>
    /// <param name="camera">摄像头实体。</param>
    /// <returns>包含 MRL 与 LibVLC 选项的播放请求。</returns>
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
                ":rtsp-tcp",           // 强制 RTSP 走 TCP，避免 UDP 被防火墙阻断
                ":network-caching=500", // 500ms 网络缓存，平衡延迟与流畅度
                ":no-audio",            // 预览场景无需音频，减少解码开销
            ],
            IsLocal = false,
        };
    }

    /// <summary>
    /// 兼容旧调用：直接返回播放 URL（MRL）。
    /// 网络流场景返回带认证信息的 URL；本机源会抛异常。
    /// </summary>
    /// <param name="camera">摄像头实体。</param>
    /// <returns>LibVLC 可打开的 MRL 字符串。</returns>
    public static string BuildPlayUrl(Camera camera)
        => BuildPlayRequest(camera).Mrl;

    /// <summary>
    /// 构建网络流 URL，若配置了用户名且 URL 中尚无认证信息，则嵌入 UserInfo。
    /// </summary>
    /// <param name="camera">摄像头实体。</param>
    /// <returns>可直接传给 LibVLC 的 URL；地址为空时返回空字符串。</returns>
    private static string BuildNetworkUrl(Camera camera)
    {
        var url = (camera.RtspUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        // 无用户名则原样返回
        if (string.IsNullOrWhiteSpace(camera.Username))
            return url;

        // URL 格式无效时无法嵌入认证，原样返回
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;

        // URL 已含 UserInfo（如 rtsp://user:pass@host），避免重复嵌入
        if (!string.IsNullOrEmpty(uri.UserInfo))
            return url;

        var builder = new UriBuilder(uri)
        {
            UserName = Uri.EscapeDataString(camera.Username.Trim()),
            Password = Uri.EscapeDataString(camera.Password ?? string.Empty),
        };
        return builder.Uri.ToString();
    }

    /// <summary>
    /// 规范化实体字段：Trim 字符串、按来源类型清空互斥字段。
    /// </summary>
    /// <param name="camera">待规范化的实体（原地修改）。</param>
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
            // 本机源不使用网络相关字段
            camera.RtspUrl = string.Empty;
            camera.Username = string.Empty;
            camera.Password = string.Empty;
        }
        else
        {
            // 网络源不使用本机设备名
            camera.LocalDeviceName = string.Empty;
        }
    }

    /// <summary>
    /// 校验实体必填项与 URL 协议前缀；不通过则抛出 <see cref="ArgumentException"/>。
    /// </summary>
    /// <param name="camera">已规范化的实体。</param>
    private static void Validate(Camera camera)
    {
        if (string.IsNullOrWhiteSpace(camera.Name))
            throw new ArgumentException("摄像头名称不能为空。");

        if (camera.SourceType == CameraSourceType.Local)
            throw new ArgumentException("当前版本不支持本机/USB 摄像头，请使用网络流（RTSP/HTTP）。");

        if (string.IsNullOrWhiteSpace(camera.RtspUrl))
            throw new ArgumentException("视频流地址不能为空。");

        // 仅允许常见的流媒体与本地文件协议
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
