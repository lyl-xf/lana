using LibVLCSharp.Shared;
using Lana.Cameras.Models;

namespace Lana.Cameras.Services;

/// <summary>
/// 进程内共享的 LibVLC 实例（动态库随 NuGet 输出，无需系统安装）。
/// </summary>
public sealed class LibVlcHost : IDisposable
{
    private static readonly object Gate = new();
    private static LibVlcHost? _instance;
    private static Task? _initTask;

    private readonly LibVLC _libVlc;
    private bool _disposed;

    private LibVlcHost()
    {
        Core.Initialize();
        // 禁音频、走 TCP RTSP，降低多路预览时线程争用与 UI 卡顿风险
        _libVlc = new LibVLC(
            "--no-video-title-show",
            "--no-audio",
            "--network-caching=500",
            "--rtsp-tcp",
            "--avcodec-hw=none");
    }

    public LibVLC LibVlc => _libVlc;

    public static LibVlcHost Instance
    {
        get
        {
            if (_instance is not null)
                return _instance;

            lock (Gate)
            {
                return _instance ??= new LibVlcHost();
            }
        }
    }

    /// <summary>在后台线程预热原生库，避免首次预览卡死 UI。</summary>
    public static Task EnsureInitializedAsync()
    {
        if (_instance is not null)
            return Task.CompletedTask;

        lock (Gate)
        {
            if (_instance is not null)
                return Task.CompletedTask;

            _initTask ??= Task.Run(() =>
            {
                _ = Instance;
            });
            return _initTask;
        }
    }

    public MediaPlayer CreatePlayer()
        => new(_libVlc);

    public Media CreateMedia(CameraPlayRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var media = new Media(_libVlc, request.Mrl, FromType.FromLocation);
        foreach (var option in request.Options)
        {
            if (!string.IsNullOrWhiteSpace(option))
                media.AddOption(option);
        }

        return media;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _libVlc.Dispose();
        if (ReferenceEquals(_instance, this))
            _instance = null;
    }
}
