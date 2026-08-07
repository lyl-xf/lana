using Lana.Gateway.Models;
using Lana.Services;

namespace Lana.Gateway.Services;

/// <summary>
/// 设备调试对外 API 实现：封装 <see cref="GatewayDeviceService"/> 的读写，并自动写入历史数据。
/// 其他页面 / 模块请优先注入本类型（或 <see cref="IDeviceDebugApi"/>），不要直接散落调用底层 Debug*。
/// </summary>
public sealed class DeviceDebugApi : IDeviceDebugApi
{
    private readonly GatewayDeviceService _gateway;
    private readonly DeviceOperationHistoryService _history;
    private readonly IAuthService? _auth;

    public DeviceDebugApi(
        GatewayDeviceService gateway,
        DeviceOperationHistoryService history,
        IAuthService? auth = null)
    {
        _gateway = gateway;
        _history = history;
        _auth = auth;
    }

    public async Task<DebugReadResult> ReadAsync(
        long deviceId,
        string address,
        DataType dataType,
        DeviceDebugContext? context = null)
    {
        context ??= DeviceDebugContext.Default;
        var device = await _gateway.GetDeviceAsync(deviceId);
        var result = await _gateway.DebugReadAsync(deviceId, address, dataType);
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

    public async Task<DebugReadAllResult> ReadAllAsync(
        long deviceId,
        DeviceDebugContext? context = null)
    {
        context ??= DeviceDebugContext.Default;
        var device = await _gateway.GetDeviceAsync(deviceId);
        var result = await _gateway.DebugReadAllAsync(deviceId);
        var ok = result.Items.Count(x => x.Success);
        var fail = result.Items.Count - ok;
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

    private static string? FormatValue(object? value)
        => value switch
        {
            null => null,
            string s => s,
            bool b => b ? "true" : "false",
            _ => value.ToString(),
        };
}
