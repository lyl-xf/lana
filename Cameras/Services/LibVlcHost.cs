using LibVLCSharp.Shared;
using Lana.Cameras.Models;

namespace Lana.Cameras.Services;

/// <summary>
/// 进程内共享的 LibVLC 单例宿主。
/// 原生库随 NuGet 包输出，无需系统单独安装 VLC。
/// 实现 <see cref="IDisposable"/>，Dispose 后释放原生资源。
/// </summary>
public sealed class LibVlcHost : IDisposable
{
    /// <summary>单例初始化与预热时的互斥锁。</summary>
    private static readonly object Gate = new();

    /// <summary>全局唯一实例引用。</summary>
    private static LibVlcHost? _instance;

    /// <summary>后台预热任务，避免重复启动。</summary>
    private static Task? _initTask;

    /// <summary>底层 LibVLC 原生实例。</summary>
    private readonly LibVLC _libVlc;

    /// <summary>是否已释放，防止重复 Dispose。</summary>
    private bool _disposed;

    /// <summary>
    /// 私有构造：初始化 LibVLCSharp 核心并创建带全局选项的 LibVLC 实例。
    /// </summary>
    private LibVlcHost()
    {
        Core.Initialize();
        // 禁音频、走 TCP RTSP、关闭硬解，降低多路预览时线程争用与 UI 卡顿风险
        _libVlc = new LibVLC(
            "--no-video-title-show",
            "--no-audio",
            "--network-caching=500",
            "--rtsp-tcp",
            "--avcodec-hw=none");
    }

    /// <summary>获取底层 LibVLC 实例，供 MediaPlayer / Media 创建使用。</summary>
    public LibVLC LibVlc => _libVlc;

    /// <summary>
    /// 获取全局单例；线程安全，双重检查锁定。
    /// </summary>
    public static LibVlcHost Instance
    {
        get
        {
            if (_instance is not null)
                return _instance;

            lock (Gate)
            {
                // 锁内再次检查，避免并发重复创建
                return _instance ??= new LibVlcHost();
            }
        }
    }

    /// <summary>
    /// 在后台线程预热原生库，避免首次预览时在 UI 线程加载导致界面卡死。
    /// 可多次调用，仅首次会启动预热任务。
    /// </summary>
    /// <returns>预热完成的任务；已初始化时立即返回 CompletedTask。</returns>
    public static Task EnsureInitializedAsync()
    {
        if (_instance is not null)
            return Task.CompletedTask;

        lock (Gate)
        {
            if (_instance is not null)
                return Task.CompletedTask;

            // 仅创建一次后台预热任务
            _initTask ??= Task.Run(() =>
            {
                _ = Instance; // 触发单例构造，加载原生 DLL
            });
            return _initTask;
        }
    }

    /// <summary>
    /// 基于共享 LibVLC 实例创建新的 MediaPlayer。
    /// 调用方负责 Dispose 播放器。
    /// </summary>
    /// <returns>新的 <see cref="MediaPlayer"/> 实例。</returns>
    public MediaPlayer CreatePlayer()
        => new(_libVlc);

    /// <summary>
    /// 根据播放请求创建 LibVLC Media 对象，并附加选项。
    /// </summary>
    /// <param name="request">由 <see cref="CameraService.BuildPlayRequest"/> 组装的播放描述。</param>
    /// <returns>配置完毕的 <see cref="Media"/>；调用方负责 Dispose。</returns>
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

    /// <summary>
    /// 释放 LibVLC 原生资源，并清空单例引用。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _libVlc.Dispose();
        // 仅当 Dispose 的是当前单例时才清空引用
        if (ReferenceEquals(_instance, this))
            _instance = null;
    }
}
