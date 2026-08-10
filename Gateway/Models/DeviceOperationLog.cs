namespace Lana.Gateway.Models;

/// <summary>
/// 设备操作历史记录（读 / 写 / 全部读取）。
/// </summary>
public sealed class DeviceOperationLog
{
    /// <summary>日志主键（自增）。</summary>
    public long Id { get; set; }
    /// <summary>操作发生时间（UTC）。</summary>
    public DateTime OccurredAtUtc { get; set; }
    /// <summary>操作用户 Id（未登录时为 null）。</summary>
    public long? UserId { get; set; }
    /// <summary>操作用户名。</summary>
    public string Username { get; set; } = string.Empty;
    /// <summary>调用来源（DefinedPage / DevicesDebug / Api 等）。</summary>
    public string Source { get; set; } = string.Empty;
    /// <summary>目标设备 Id。</summary>
    public long DeviceId { get; set; }
    /// <summary>目标设备名称（冗余存储，便于历史页展示）。</summary>
    public string DeviceName { get; set; } = string.Empty;
    /// <summary>目标变量 Id（批量读或按地址读时为 null）。</summary>
    public long? VariableId { get; set; }
    /// <summary>目标变量别名。</summary>
    public string VariableAlias { get; set; } = string.Empty;
    /// <summary>读写的协议地址。</summary>
    public string Address { get; set; } = string.Empty;
    /// <summary>操作类型：Read / Write / ReadAll。</summary>
    public string Operation { get; set; } = string.Empty;
    /// <summary>数据类型名称。</summary>
    public string DataType { get; set; } = string.Empty;
    /// <summary>读到的值或写入的值（格式化字符串）。</summary>
    public string? Value { get; set; }
    /// <summary>操作是否成功。</summary>
    public bool Success { get; set; }
    /// <summary>失败时的错误信息。</summary>
    public string? Error { get; set; }
}
