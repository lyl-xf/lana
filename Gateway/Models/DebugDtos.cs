namespace Lana.Gateway.Models;

/// <summary>调试读结果（GatewayDeviceService / IDeviceDebugApi）。</summary>
public sealed class DebugReadResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public object? Value { get; set; }
}

/// <summary>调试写结果。</summary>
public sealed class DebugWriteResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}

public sealed class DebugReadAllItem
{
    public long VariableId { get; set; }
    public string Alias { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public DataType DataType { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public object? Value { get; set; }
}

/// <summary>按物模型批量读结果。</summary>
public sealed class DebugReadAllResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<DebugReadAllItem> Items { get; set; } = [];
}
