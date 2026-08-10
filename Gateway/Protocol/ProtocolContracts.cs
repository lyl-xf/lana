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
    /// <summary>操作是否成功。</summary>
    public bool Success { get; init; }
    /// <summary>失败时的错误描述。</summary>
    public string? Error { get; init; }

    /// <summary>构造成功结果。</summary>
    /// <returns>Success=true 的结果。</returns>
    public static ProtocolResult Ok() => new() { Success = true };

    /// <summary>构造失败结果。</summary>
    /// <param name="error">错误描述。</param>
    /// <returns>Success=false 的结果。</returns>
    public static ProtocolResult Fail(string? error) => new() { Success = false, Error = error ?? "Unknown error" };
}

/// <summary>带返回值的协议操作结果。</summary>
/// <typeparam name="T">返回值类型。</typeparam>
public readonly struct ProtocolResult<T>
{
    /// <summary>操作是否成功。</summary>
    public bool Success { get; init; }
    /// <summary>失败时的错误描述。</summary>
    public string? Error { get; init; }
    /// <summary>读到的值（失败时通常为 default）。</summary>
    public T? Value { get; init; }

    /// <summary>构造成功结果。</summary>
    /// <param name="value">读到的值。</param>
    /// <returns>携带 Value 的成功结果。</returns>
    public static ProtocolResult<T> Ok(T value) => new() { Success = true, Value = value };

    /// <summary>构造失败结果。</summary>
    /// <param name="error">错误描述。</param>
    /// <returns>Success=false 的结果。</returns>
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
    /// <summary>打开/连接设备。</summary>
    /// <returns>连接结果。</returns>
    ProtocolResult Open();

    /// <summary>关闭/断开设备连接。</summary>
    void Close();

    /// <summary>当前是否已连接。</summary>
    bool IsConnected { get; }

    /// <summary>从指定地址读取值。</summary>
    /// <param name="address">协议地址。</param>
    /// <param name="dataType">数据类型。</param>
    /// <returns>读结果（含 Value）。</returns>
    ProtocolResult<object?> Read(string address, ProtocolDataType dataType);

    /// <summary>向指定地址写入值。</summary>
    /// <param name="address">协议地址。</param>
    /// <param name="dataType">数据类型。</param>
    /// <param name="value">字符串形式的写入值。</param>
    /// <returns>写结果。</returns>
    ProtocolResult Write(string address, ProtocolDataType dataType, string? value);
}
