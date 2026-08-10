namespace Lana.Cameras.Models;

/// <summary>
/// 摄像头来源。当前仅 Network 可用；Local（本机 dshow）因发行版 LibVLC 缺少插件而不支持。
/// </summary>
public enum CameraSourceType
{
    /// <summary>网络流（RTSP / HTTP 等）。</summary>
    Network = 0,
    /// <summary>本机 / USB（未支持，BuildPlayRequest 会抛错）。</summary>
    Local = 1,
}

/// <summary>摄像头配置实体。扩展字段需同步 CameraSchema / CameraMapper / CameraService / UI。</summary>
public sealed class Camera
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public CameraSourceType SourceType { get; set; } = CameraSourceType.Network;
    /// <summary>网络流地址；本机摄像头时可为空。</summary>
    public string RtspUrl { get; set; } = string.Empty;
    /// <summary>本机 / USB 设备友好名称（DirectShow）。</summary>
    public string LocalDeviceName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    /// <summary>启用后可在预览页播放。</summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>交给 LibVLC 播放的统一描述。</summary>
public sealed class CameraPlayRequest
{
    public required string Mrl { get; init; }
    public IReadOnlyList<string> Options { get; init; } = [];
    public bool IsLocal { get; init; }
}
