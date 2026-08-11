using Lana.Gateway.Models;

namespace Lana.Gateway.Services;

/// <summary>
/// 物模型变量轮询 / 展示 / 上报范围判定（方案 B：显式勾选）。
/// </summary>
public static class DeviceVariablePollRules
{
    /// <summary>是否参与后台轮询（可读、有别名且已勾选）。</summary>
    public static bool ShouldPoll(DeviceVariable variable)
        => variable.IncludeInPoll
           && variable.ReadWrite != ReadWriteAccess.WriteOnly
           && !string.IsNullOrWhiteSpace(variable.Alias);

    /// <summary>HttpClient 路径行是否参与轮询（不要求 Alias，按路径拉取动态 KV）。</summary>
    public static bool ShouldPollHttpPath(DeviceVariable variable)
        => variable.IncludeInPoll
           && variable.ReadWrite != ReadWriteAccess.WriteOnly;

    /// <summary>是否在手动操作页状态区展示。</summary>
    public static bool ShouldShowInStatus(DeviceVariable variable)
        => variable.ShowInStatus && ShouldPoll(variable);

    /// <summary>是否纳入 MQTT 周期遥测。</summary>
    public static bool ShouldIncludeInTelemetry(DeviceVariable variable)
        => variable.IncludeInTelemetry && ShouldPoll(variable);

    /// <summary>保存前规范化采集相关开关。</summary>
    public static void NormalizeCollectionFlags(DeviceVariable variable)
    {
        if (variable.ReadWrite == ReadWriteAccess.WriteOnly)
            variable.IncludeInPoll = false;

        if (variable.ShowInStatus || variable.IncludeInTelemetry)
            variable.IncludeInPoll = true;

        if (!variable.IncludeInPoll)
        {
            variable.ShowInStatus = false;
            variable.IncludeInTelemetry = false;
        }
    }

    /// <summary>保存前校验；不通过时抛出 <see cref="InvalidOperationException"/>。</summary>
    /// <param name="variable">物模型变量。</param>
    /// <param name="protocolType">所属设备协议（HttpClient 动态 KV 可不填别名）。</param>
    public static void ValidateCollectionFlags(DeviceVariable variable, ProtocolType protocolType)
    {
        if (!variable.IncludeInPoll)
            return;

        if (protocolType == ProtocolType.HttpClient)
            return;

        if (string.IsNullOrWhiteSpace(variable.Alias))
            throw new InvalidOperationException("参与轮询时「别名」不能为空。");
    }
}
