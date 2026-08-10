namespace Lana.Cameras.Models;

/// <summary>
/// 摄像头来源类型。当前仅 <see cref="Network"/> 可用；
/// <see cref="Local"/>（本机 DirectShow/USB）因发行版 LibVLC 缺少 dshow 插件而不支持。
/// </summary>
public enum CameraSourceType
{
    /// <summary>网络流（RTSP / HTTP / HTTPS / file 等）。</summary>
    Network = 0,

    /// <summary>本机 / USB 摄像头（DirectShow）；<see cref="CameraService.BuildPlayRequest"/> 会抛 <see cref="NotSupportedException"/>。</summary>
    Local = 1,
}

/// <summary>
/// 摄像头配置实体，对应数据库 Cameras 表。
/// 新增字段时需同步更新 <c>CameraSchema</c>、<c>CameraMapper</c>、<c>CameraService</c> 及 UI 层。
/// </summary>
public sealed class Camera
{
    /// <summary>主键 Id，由数据库自增生成。</summary>
    public long Id { get; set; }

    /// <summary>摄像头显示名称，用于列表与预览页标题。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>来源类型，决定播放时使用网络流还是本机设备。</summary>
    public CameraSourceType SourceType { get; set; } = CameraSourceType.Network;

    /// <summary>网络流地址（RTSP/HTTP 等）；本机摄像头时可为空。</summary>
    public string RtspUrl { get; set; } = string.Empty;

    /// <summary>本机 / USB 设备友好名称（DirectShow 设备名）。</summary>
    public string LocalDeviceName { get; set; } = string.Empty;

    /// <summary>网络流认证用户名，可为空。</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>网络流认证密码，可为空。</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>备注说明，仅用于管理界面展示。</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>排序权重，数值越小越靠前。</summary>
    public int SortOrder { get; set; }

    /// <summary>是否启用；启用后可在预览页播放。</summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// 交给 LibVLC 播放的统一描述，由 <see cref="CameraService.BuildPlayRequest"/> 组装。
/// </summary>
public sealed class CameraPlayRequest
{
    /// <summary>媒体资源定位符（MRL），即 LibVLC 可直接打开的 URL 或路径。</summary>
    public required string Mrl { get; init; }

    /// <summary>LibVLC 媒体选项列表（如 <c>:rtsp-tcp</c>、缓存时长等）。</summary>
    public IReadOnlyList<string> Options { get; init; } = [];

    /// <summary>是否为本机设备源；当前版本恒为 <c>false</c>。</summary>
    public bool IsLocal { get; init; }
}
