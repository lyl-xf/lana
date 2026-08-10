namespace Lana.Gateway.Models;

/// <summary>调试读结果（GatewayDeviceService / IDeviceDebugApi）。</summary>
public sealed class DebugReadResult
{
    /// <summary>读操作是否成功。</summary>
    public bool Success { get; set; }
    /// <summary>失败时的错误描述。</summary>
    public string? Error { get; set; }
    /// <summary>读到的原始值。</summary>
    public object? Value { get; set; }
}

/// <summary>调试写结果。</summary>
public sealed class DebugWriteResult
{
    /// <summary>写操作是否成功。</summary>
    public bool Success { get; set; }
    /// <summary>失败时的错误描述。</summary>
    public string? Error { get; set; }
}

/// <summary>批量读结果中的单个变量项。</summary>
public sealed class DebugReadAllItem
{
    /// <summary>变量 Id。</summary>
    public long VariableId { get; set; }
    /// <summary>变量别名。</summary>
    public string Alias { get; set; } = string.Empty;
    /// <summary>变量协议地址。</summary>
    public string Address { get; set; } = string.Empty;
    /// <summary>变量数据类型。</summary>
    public DataType DataType { get; set; }
    /// <summary>该点读是否成功。</summary>
    public bool Success { get; set; }
    /// <summary>该点失败时的错误描述。</summary>
    public string? Error { get; set; }
    /// <summary>该点读到的值。</summary>
    public object? Value { get; set; }
}

/// <summary>按物模型批量读结果。</summary>
public sealed class DebugReadAllResult
{
    /// <summary>整体是否成功（会话打开成功即为 true，单点失败记录在 Items 中）。</summary>
    public bool Success { get; set; }
    /// <summary>会话级错误（如连接失败）。</summary>
    public string? Error { get; set; }
    /// <summary>各变量的读结果列表。</summary>
    public List<DebugReadAllItem> Items { get; set; } = [];
}
