using Lana.Gateway.Models;

namespace Lana.Gateway.Services;

/// <summary>
/// 设备调试调用上下文（来源、操作人、是否写入历史）。
/// </summary>
public sealed class DeviceDebugContext
{
    /// <summary>默认上下文（来源 Api，记录历史）。</summary>
    public static DeviceDebugContext Default { get; } = new();

    /// <summary>调用来源，如 DefinedPage / DevicesDebug / External。</summary>
    public string Source { get; init; } = "Api";

    /// <summary>操作用户 Id（未指定时由 Auth 服务填充）。</summary>
    public long? UserId { get; init; }

    /// <summary>操作用户名。</summary>
    public string? Username { get; init; }

    /// <summary>是否记录到「历史数据」。默认 true。</summary>
    public bool RecordHistory { get; init; } = true;
}

/// <summary>
/// 设备调试对外 API。定义页、设备调试页及其他模块应通过此接口调用读写，便于统一记历史。
/// </summary>
public interface IDeviceDebugApi
{
    /// <summary>
    /// 按地址调试读。
    /// </summary>
    /// <param name="deviceId">设备 Id。</param>
    /// <param name="address">协议地址。</param>
    /// <param name="dataType">数据类型。</param>
    /// <param name="context">可选调试上下文。</param>
    /// <returns>读结果。</returns>
    Task<DebugReadResult> ReadAsync(
        long deviceId,
        string address,
        DataType dataType,
        DeviceDebugContext? context = null);

    /// <summary>
    /// 按地址调试写。
    /// </summary>
    /// <param name="deviceId">设备 Id。</param>
    /// <param name="address">协议地址。</param>
    /// <param name="dataType">数据类型。</param>
    /// <param name="value">写入值。</param>
    /// <param name="context">可选调试上下文。</param>
    /// <returns>写结果。</returns>
    Task<DebugWriteResult> WriteAsync(
        long deviceId,
        string address,
        DataType dataType,
        string? value,
        DeviceDebugContext? context = null);

    /// <summary>
    /// 读取设备全部可读变量。
    /// </summary>
    /// <param name="deviceId">设备 Id。</param>
    /// <param name="context">可选调试上下文。</param>
    /// <returns>批量读结果。</returns>
    Task<DebugReadAllResult> ReadAllAsync(
        long deviceId,
        DeviceDebugContext? context = null);

    /// <summary>
    /// 按变量 Id 调试读。
    /// </summary>
    /// <param name="deviceId">设备 Id。</param>
    /// <param name="variableId">变量 Id。</param>
    /// <param name="context">可选调试上下文。</param>
    /// <returns>读结果。</returns>
    Task<DebugReadResult> ReadVariableAsync(
        long deviceId,
        long variableId,
        DeviceDebugContext? context = null);

    /// <summary>
    /// 按变量 Id 调试写。
    /// </summary>
    /// <param name="deviceId">设备 Id。</param>
    /// <param name="variableId">变量 Id。</param>
    /// <param name="value">写入值。</param>
    /// <param name="context">可选调试上下文。</param>
    /// <returns>写结果。</returns>
    Task<DebugWriteResult> WriteVariableAsync(
        long deviceId,
        long variableId,
        string? value,
        DeviceDebugContext? context = null);
}
