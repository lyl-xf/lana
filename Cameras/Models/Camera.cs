namespace Lana.Cameras.Models;

public sealed class Camera
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RtspUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    /// <summary>启用后可在预览页播放。</summary>
    public bool IsEnabled { get; set; } = true;
}
