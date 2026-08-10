using Lana.Gateway.Models;
using Lana.Services;

namespace Lana.Gateway.Services;

/// <summary>
/// 设备调试对外 API 实现：封装 <see cref="GatewayDeviceService"/> 的读写，并自动写入历史数据。
/// 其他页面 / 模块请优先注入本类型（或 <see cref="IDeviceDebugApi"/>），不要直接散落调用底层 Debug*。
/// </summary>
public sealed class DeviceDebugApi : IDeviceDebugApi
{
    /// <summary>网关设备底层服务。</summary>
    private readonly GatewayDeviceService _gateway;
    /// <summary>操作历史服务。</summary>
    private readonly DeviceOperationHistoryService _history;
    /// <summary>认证服务（可选，用于填充 UserId/Username）。</summary>
    private readonly IAuthService? _auth;

    /// <summary>
    /// 构造调试 API。
    /// </summary>
    /// <param name="gateway">网关设备服务。</param>
    /// <param name="history">操作历史服务。</param>
    /// <param name="auth">可选认证服务。</param>
    public DeviceDebugApi(
        GatewayDeviceService gateway,
        DeviceOperationHistoryService history,
        IAuthService? auth = null)
    {
        _gateway = gateway;
        _history = history;
        _auth = auth;
    }

    /// <inheritdoc />
    public async Task<DebugReadResult> ReadAsync(
        long deviceId,
        string address,
        DataType dataType,
        DeviceDebugContext? context = null)
    {
        context ??= DeviceDebugContext.Default;
        var device = await _gateway.GetDeviceAsync(deviceId);
        var result = await _gateway.DebugReadAsync(deviceId, address, dataType);
        // 读写完成后异步记历史（失败不影响主流程）
        await TryLogAsync(new DeviceOperationLog
        {
            OccurredAtUtc = DateTime.UtcNow,
            UserId = context.UserId ?? _auth?.CurrentUser?.Id,
            Username = context.Username ?? _auth?.CurrentUser?.Username ?? string.Empty,
            Source = context.Source,
            DeviceId = deviceId,
            DeviceName = device?.Name ?? string.Empty,
            Address = address,
            Operation = "Read",
            DataType = dataType.ToString(),
            Value = FormatValue(result.Value),
            Success = result.Success,
            Error = result.Error,
        }, context);
        return result;
    }

    /// <inheritdoc />
    public async Task<DebugWriteResult> WriteAsync(
        long deviceId,
        string address,
        DataType dataType,
        string? value,
        DeviceDebugContext? context = null)
    {
        context ??= DeviceDebugContext.Default;
        var device = await _gateway.GetDeviceAsync(deviceId);
        var result = await _gateway.DebugWriteAsync(deviceId, address, dataType, value);
        await TryLogAsync(new DeviceOperationLog
        {
            OccurredAtUtc = DateTime.UtcNow,
            UserId = context.UserId ?? _auth?.CurrentUser?.Id,
            Username = context.Username ?? _auth?.CurrentUser?.Username ?? string.Empty,
            Source = context.Source,
            DeviceId = deviceId,
            DeviceName = device?.Name ?? string.Empty,
            Address = address,
            Operation = "Write",
            DataType = dataType.ToString(),
            Value = value,
            Success = result.Success,
            Error = result.Error,
        }, context);
        return result;
    }

    /// <inheritdoc />
    public async Task<DebugReadAllResult> ReadAllAsync(
        long deviceId,
        DeviceDebugContext? context = null)
    {
        context ??= DeviceDebugContext.Default;
        var device = await _gateway.GetDeviceAsync(deviceId);
        var result = await _gateway.DebugReadAllAsync(deviceId);
        var ok = result.Items.Count(x => x.Success);
        var fail = result.Items.Count - ok;
        // 批量读记一条汇总日志
        await TryLogAsync(new DeviceOperationLog
        {
            OccurredAtUtc = DateTime.UtcNow,
            UserId = context.UserId ?? _auth?.CurrentUser?.Id,
            Username = context.Username ?? _auth?.CurrentUser?.Username ?? string.Empty,
            Source = context.Source,
            DeviceId = deviceId,
            DeviceName = device?.Name ?? string.Empty,
            Address = "*",
            Operation = "ReadAll",
            DataType = string.Empty,
            Value = $"成功 {ok} / 失败 {fail} / 共 {result.Items.Count}",
            Success = result.Success,
            Error = result.Error,
        }, context);
        return result;
    }

    /// <inheritdoc />
    public async Task<DebugReadResult> ReadVariableAsync(
        long deviceId,
        long variableId,
        DeviceDebugContext? context = null)
    {
        context ??= DeviceDebugContext.Default;
        var device = await _gateway.GetDeviceAsync(deviceId);
        var variable = device?.Variables.FirstOrDefault(x => x.Id == variableId);
        if (variable is null)
        {
            return new DebugReadResult
            {
                Success = false,
                Error = $"设备 {deviceId} 不存在变量 Id={variableId}",
            };
        }

        var result = await _gateway.DebugReadAsync(deviceId, variable.Address, variable.DataType);
        await TryLogAsync(new DeviceOperationLog
        {
            OccurredAtUtc = DateTime.UtcNow,
            UserId = context.UserId ?? _auth?.CurrentUser?.Id,
            Username = context.Username ?? _auth?.CurrentUser?.Username ?? string.Empty,
            Source = context.Source,
            DeviceId = deviceId,
            DeviceName = device?.Name ?? string.Empty,
            VariableId = variable.Id,
            VariableAlias = variable.Alias,
            Address = variable.Address,
            Operation = "Read",
            DataType = variable.DataType.ToString(),
            Value = FormatValue(result.Value),
            Success = result.Success,
            Error = result.Error,
        }, context);
        return result;
    }

    /// <inheritdoc />
    public async Task<DebugWriteResult> WriteVariableAsync(
        long deviceId,
        long variableId,
        string? value,
        DeviceDebugContext? context = null)
    {
        context ??= DeviceDebugContext.Default;
        var device = await _gateway.GetDeviceAsync(deviceId);
        var variable = device?.Variables.FirstOrDefault(x => x.Id == variableId);
        if (variable is null)
        {
            return new DebugWriteResult
            {
                Success = false,
                Error = $"设备 {deviceId} 不存在变量 Id={variableId}",
            };
        }

        var result = await _gateway.DebugWriteAsync(deviceId, variable.Address, variable.DataType, value);
        await TryLogAsync(new DeviceOperationLog
        {
            OccurredAtUtc = DateTime.UtcNow,
            UserId = context.UserId ?? _auth?.CurrentUser?.Id,
            Username = context.Username ?? _auth?.CurrentUser?.Username ?? string.Empty,
            Source = context.Source,
            DeviceId = deviceId,
            DeviceName = device?.Name ?? string.Empty,
            VariableId = variable.Id,
            VariableAlias = variable.Alias,
            Address = variable.Address,
            Operation = "Write",
            DataType = variable.DataType.ToString(),
            Value = value,
            Success = result.Success,
            Error = result.Error,
        }, context);
        return result;
    }

    /// <summary>
    /// 按上下文决定是否写入操作历史（失败静默忽略）。
    /// </summary>
    /// <param name="log">待写入的日志。</param>
    /// <param name="context">调试上下文。</param>
    /// <returns>异步任务。</returns>
    private async Task TryLogAsync(DeviceOperationLog log, DeviceDebugContext context)
    {
        if (!context.RecordHistory)
            return;

        try
        {
            await _history.InsertAsync(log);
        }
        catch
        {
            /* 历史写入失败不影响调试主流程 */
        }
    }

    /// <summary>
    /// 将读到的值格式化为历史记录字符串。
    /// </summary>
    /// <param name="value">原始值。</param>
    /// <returns>格式化字符串；null 时返回 null。</returns>
    private static string? FormatValue(object? value)
        => value switch
        {
            null => null,
            string s => s,
            bool b => b ? "true" : "false",
            _ => value.ToString(),
        };
}
