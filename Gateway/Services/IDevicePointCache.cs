using Lana.Gateway.Models;

namespace Lana.Gateway.Services;

/// <summary>
/// 设备点值 live 缓存（Poll commit 写入；MQTT 周期上报与 UI 分过滤读取）。
/// </summary>
public interface IDevicePointCache
{
    /// <summary>一轮 Poll 完成后批量提交该设备全部采集点。</summary>
    void CommitPoll(
        long deviceId,
        Device device,
        IReadOnlyDictionary<string, object> payload,
        DateTime updatedAtUtc);

    /// <summary>当前缓存版本；无缓存时返回 0。</summary>
    long GetVersion(long deviceId);

    /// <summary>构建 MQTT 周期遥测 payload（仅 IncludeInTelemetry 及 Http 动态键）。</summary>
    IReadOnlyDictionary<string, object> GetTelemetryPayload(Device device);

    /// <summary>写成功后更新单点（不整轮 Poll）。</summary>
    void PatchPoint(
        long deviceId,
        Device device,
        string alias,
        object value,
        DateTime updatedAtUtc);

    /// <summary>清空全部设备缓存（登出时）。</summary>
    void Clear();
}

/// <summary>缓存中的单个点值。</summary>
public sealed class CachedPointValue
{
    /// <summary>物模型变量 Id；动态键为 0。</summary>
    public long VariableId { get; init; }

    /// <summary>协议原始值（MQTT 序列化用）。</summary>
    public required object RawValue { get; init; }

    /// <summary>UI 展示文本。</summary>
    public required string ValueText { get; init; }

    /// <summary>该点更新时间（UTC）。</summary>
    public DateTime UpdatedAtUtc { get; init; }
}
