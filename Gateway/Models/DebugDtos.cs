namespace Lana.Gateway.Models;

public sealed class DebugReadResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public object? Value { get; set; }
}

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

public sealed class DebugReadAllResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<DebugReadAllItem> Items { get; set; } = [];
}
