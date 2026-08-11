namespace Lana.Gateway.Services;

/// <summary>物理链路配置键（同键共享一条协议连接与 IO 队列）。</summary>
public static class DeviceLinkKey
{
    /// <summary>根据设备连接参数生成链路键。</summary>
    public static string FromDevice(Models.Device device)
        => $"{device.ProtocolType}_{device.Ip}_{device.Port}_{device.PortName}_{device.BaudRate}_{device.DataBits}_{device.StopBits}_{device.Parity}_{device.PlcVersion}_{device.PluginConfigJson}";
}

/// <summary>Scheduler 使用的连接状态回调（由 Worker 注入）。</summary>
public sealed class DeviceIoConnectionHandlers
{
    /// <summary>连接成功并可用。</summary>
    public Action<long>? MarkOnline { get; init; }

    /// <summary>连接不可用。</summary>
    public Action<long>? MarkOffline { get; init; }

    /// <summary>登记失败与退避。</summary>
    public Action<long, DateTime, string>? RegisterFailure { get; init; }

    /// <summary>清除退避。</summary>
    public Action<long>? ClearFailure { get; init; }

    /// <summary>是否处于退避窗口。</summary>
    public Func<long, DateTime, bool>? IsInBackoff { get; init; }
}

/// <summary>单设备 Poll 结果。</summary>
public sealed class DevicePollResult
{
    /// <summary>采集到的 Alias/Key → 值。</summary>
    public Dictionary<string, object> Payload { get; init; } = [];

    /// <summary>协议层是否已连接并完成读点尝试。</summary>
    public bool Connected { get; init; }

    /// <summary>连接或 IO 失败描述。</summary>
    public string? Error { get; init; }
}

/// <summary>MQTT 指令 IO 结果（回包由 Worker 发布）。</summary>
public sealed class MqttIoCommandResult
{
    /// <summary>设备是否可达。</summary>
    public bool Connected { get; init; }

    /// <summary>读回包 data 字段；无读时为 null。</summary>
    public Dictionary<string, object>? ReplyPayload { get; init; }

    /// <summary>replyTo 标志。</summary>
    public int ReplyTo { get; init; } = 2;

    /// <summary>错误描述。</summary>
    public string? Error { get; init; }
}

/// <summary>MQTT 下行指令 JSON DTO。</summary>
public sealed class MqttCommandDto
{
    /// <summary>可选消息 ID。</summary>
    public string? MessageId { get; set; }

    /// <summary>目标设备 ID。</summary>
    public long DeviceId { get; set; }

    /// <summary>query / write。</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>写指令 Alias → 值。</summary>
    public Dictionary<string, object>? Writes { get; set; }

    /// <summary>读 Alias 列表；null 表示未传 reads。</summary>
    public List<string>? Reads { get; set; }
}

/// <summary>IO 优先级：写 &gt; 读/指令 &gt; 轮询。</summary>
public enum DeviceIoPriority
{
    Poll = 0,
    Normal = 1,
    Write = 2,
}
