namespace Lana.Gateway.Protocol;

/// <summary>
/// 协议层数据类型（与 Models.DataType 对应，供会话读写使用）。
/// </summary>
public enum ProtocolDataType
{
    Bool, Int16, Int32, Float, Double, String, Coil, Discrete, Short, UShort, Long, ULong
}

/// <summary>无返回值的协议操作结果。</summary>
public readonly struct ProtocolResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    public static ProtocolResult Ok() => new() { Success = true };
    public static ProtocolResult Fail(string? error) => new() { Success = false, Error = error ?? "Unknown error" };
}

/// <summary>带返回值的协议操作结果。</summary>
public readonly struct ProtocolResult<T>
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public T? Value { get; init; }

    public static ProtocolResult<T> Ok(T value) => new() { Success = true, Value = value };
    public static ProtocolResult<T> Fail(string? error) => new() { Success = false, Error = error ?? "Unknown error" };
}

/// <summary>
/// 单设备协议会话抽象。采集 Worker、调试读写均通过此接口访问设备。
/// <para>
/// <b>新增协议实现步骤：</b>
/// <list type="number">
/// <item>在 <c>ProtocolType</c> 增加枚举值，并更新校验与 UI 显示名。</item>
/// <item>实现本接口（Open/Close/Read/Write）。</item>
/// <item>在 <c>ProtocolSessionFactory.CreateSession</c> 中按协议类型分支创建。</item>
/// <item>若采集载荷与通用 Alias 读取不同（如 HttpClient），同步改 <c>DataCollectionWorker</c>。</item>
/// </list>
/// </para>
/// </summary>
public interface IDeviceProtocolSession : IDisposable
{
    ProtocolResult Open();
    void Close();
    bool IsConnected { get; }
    ProtocolResult<object?> Read(string address, ProtocolDataType dataType);
    ProtocolResult Write(string address, ProtocolDataType dataType, string? value);
}
