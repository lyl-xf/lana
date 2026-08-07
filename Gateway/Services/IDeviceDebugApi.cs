using Lana.Gateway.Models;

namespace Lana.Gateway.Services;

/// <summary>
/// 设备调试调用上下文（来源、操作人、是否写入历史）。
/// </summary>
public sealed class DeviceDebugContext
{
    public static DeviceDebugContext Default { get; } = new();

    /// <summary>调用来源，如 DefinedPage / DevicesDebug / External。</summary>
    public string Source { get; init; } = "Api";

    public long? UserId { get; init; }

    public string? Username { get; init; }

    /// <summary>是否记录到「历史数据」。默认 true。</summary>
    public bool RecordHistory { get; init; } = true;
}

/// <summary>
/// 设备调试对外 API。定义页、设备调试页及其他模块应通过此接口调用读写，便于统一记历史。
/// </summary>
public interface IDeviceDebugApi
{
    Task<DebugReadResult> ReadAsync(
        long deviceId,
        string address,
        DataType dataType,
        DeviceDebugContext? context = null);

    Task<DebugWriteResult> WriteAsync(
        long deviceId,
        string address,
        DataType dataType,
        string? value,
        DeviceDebugContext? context = null);

    Task<DebugReadAllResult> ReadAllAsync(
        long deviceId,
        DeviceDebugContext? context = null);

    Task<DebugReadResult> ReadVariableAsync(
        long deviceId,
        long variableId,
        DeviceDebugContext? context = null);

    Task<DebugWriteResult> WriteVariableAsync(
        long deviceId,
        long variableId,
        string? value,
        DeviceDebugContext? context = null);
}
