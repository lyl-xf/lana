using System.Collections.ObjectModel;

namespace Lana.Gateway.Services;

/// <summary>
/// 轮询采集结果的共享实时状态存储（Composition Root 单例，登录后 Worker 与 UI 共用）。
/// <para>
/// <b>写入方：</b><see cref="DataCollectionWorker"/> 每轮采集完成后调用
/// <see cref="UpdateDevice"/>。<br/>
/// <b>读取方：</b>定义页等 UI 直接绑定 <see cref="Groups"/>，勿 Copy 到新集合、勿订阅后整表重建。
/// </para>
/// <para>
/// 实现类负责线程安全：可在后台线程写入，内部合并后投递 UI 线程更新可观察属性。
/// </para>
/// </summary>
public interface IDeviceDataSnapshotStore
{
    /// <summary>
    /// 按设备分组的实时状态。集合引用在应用会话内保持不变，仅项内属性与增删点会变化。
    /// </summary>
    ObservableCollection<DeviceLiveGroup> Groups { get; }

    /// <summary>是否存在至少一个带展示点的设备分组（用于空状态提示）。</summary>
    bool HasData { get; }

    /// <summary>
    /// 合并某设备本轮轮询结果。可在 Worker 后台线程调用；若无实质变化则实现可 no-op。
    /// </summary>
    /// <param name="deviceId">设备 Id。</param>
    /// <param name="deviceName">当前设备名（配置变更时同步到 UI）。</param>
    /// <param name="entries">本轮采集到的变量快照（见 <see cref="DeviceVariableSnapshotEntry"/>）。</param>
    void UpdateDevice(long deviceId, string deviceName, IReadOnlyList<DeviceVariableSnapshotEntry> entries);

    /// <summary>清空全部实时状态（登出或重置时可调用；当前 MainViewModel 登出未调用，保留末次数据直至进程结束）。</summary>
    void Clear();
}

/// <summary>
/// Worker → Store 的单轮变量快照传输 DTO（非 UI 绑定模型）。
/// <para>由 <see cref="DataCollectionWorker.BuildSnapshotEntries"/> 构建，经 <see cref="IDeviceDataSnapshotStore.UpdateDevice"/> 合并进 <see cref="DeviceLiveGroup"/>。</para>
/// </summary>
public sealed class DeviceVariableSnapshotEntry
{
    /// <summary>物模型变量 Id；非物模型键（仅 payload 扩展）时为 0。</summary>
    public long VariableId { get; init; }

    /// <summary>展示标签，优先 Description。</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>已格式化的值字符串。</summary>
    public string ValueText { get; init; } = string.Empty;

    /// <summary>本轮采集 UTC 时间戳。</summary>
    public DateTime UpdatedAtUtc { get; init; }
}
