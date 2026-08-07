namespace Lana.Gateway.Models;

/// <summary>
/// 设备操作历史记录（读 / 写 / 全部读取）。
/// </summary>
public sealed class DeviceOperationLog
{
    public long Id { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public long? UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public long DeviceId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public long? VariableId { get; set; }
    public string VariableAlias { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    /// <summary>Read / Write / ReadAll</summary>
    public string Operation { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string? Value { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}
