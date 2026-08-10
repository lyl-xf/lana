using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lana.Gateway.Models;
using Lana.Gateway.Services;

namespace Lana.ViewModels;

/// <summary>历史数据列表行（由 DeviceOperationLog 投影）。</summary>
public sealed class HistoryLogItem
{
    /// <summary>日志 Id。</summary>
    public long Id { get; init; }

    /// <summary>操作时间（本地时区格式化）。</summary>
    public string TimeText { get; init; } = string.Empty;

    /// <summary>操作用户名。</summary>
    public string UserText { get; init; } = string.Empty;

    /// <summary>操作来源（DefinedPage / DevicesDebug 等）。</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>设备描述文本。</summary>
    public string DeviceText { get; init; } = string.Empty;

    /// <summary>操作类型（读取/写入/全部读取）。</summary>
    public string Operation { get; init; } = string.Empty;

    /// <summary>目标变量/地址描述。</summary>
    public string TargetText { get; init; } = string.Empty;

    /// <summary>读写的值文本。</summary>
    public string ValueText { get; init; } = string.Empty;

    /// <summary>结果描述（成功/失败原因）。</summary>
    public string ResultText { get; init; } = string.Empty;

    /// <summary>是否操作成功。</summary>
    public bool Success { get; init; }
}

/// <summary>历史页设备筛选下拉项。</summary>
public sealed class HistoryDeviceFilterItem
{
    /// <summary>设备 Id；null 表示「全部设备」。</summary>
    public long? Id { get; init; }

    /// <summary>下拉显示名称。</summary>
    public string DisplayName { get; init; } = string.Empty;
}

/// <summary>
/// 历史数据页：查询 <see cref="DeviceOperationHistoryService"/> 记录的调试/定义页读写。
/// Source 字段可区分来源（DefinedPage / DevicesDebug 等）。
/// </summary>
public partial class HistoryViewModel : ViewModelBase
{
    /// <summary>操作历史查询服务。</summary>
    private readonly DeviceOperationHistoryService _history;

    /// <summary>设备列表服务（用于筛选下拉）。</summary>
    private readonly GatewayDeviceService _devices;

    /// <summary>
    /// 构造历史页 ViewModel 并异步初始化筛选列表。
    /// </summary>
    /// <param name="history">操作历史服务。</param>
    /// <param name="devices">设备服务。</param>
    public HistoryViewModel(DeviceOperationHistoryService history, GatewayDeviceService devices)
    {
        _history = history;
        _devices = devices;
        _ = InitializeAsync();
    }

    /// <summary>底部状态栏提示信息。</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>是否正在加载或清空。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>当前筛选的设备 Id；null 表示全部。</summary>
    [ObservableProperty]
    private long? _filterDeviceId;

    /// <summary>设备筛选下拉列表。</summary>
    public ObservableCollection<HistoryDeviceFilterItem> DeviceFilters { get; } = [];

    /// <summary>历史日志列表。</summary>
    public ObservableCollection<HistoryLogItem> Logs { get; } = [];

    /// <summary>
    /// 筛选设备变更时自动刷新列表。
    /// </summary>
    /// <param name="value">新选中的设备 Id。</param>
    partial void OnFilterDeviceIdChanged(long? value)
        => _ = RefreshAsync();

    /// <summary>
    /// 初始化设备筛选下拉并加载首屏数据。
    /// </summary>
    /// <returns>表示初始化完成的 Task。</returns>
    private async Task InitializeAsync()
    {
        try
        {
            DeviceFilters.Clear();
            DeviceFilters.Add(new HistoryDeviceFilterItem { Id = null, DisplayName = "全部设备" });
            var list = await _devices.ListDevicesAsync();
            foreach (var d in list.OrderBy(x => x.SortOrder).ThenBy(x => x.Id))
            {
                DeviceFilters.Add(new HistoryDeviceFilterItem
                {
                    Id = d.Id,
                    DisplayName = $"#{d.Id} {d.Name}",
                });
            }

            FilterDeviceId = null;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = "初始化历史失败：" + ex.Message;
        }
    }

    /// <summary>
    /// 按当前筛选条件刷新历史列表（最多 300 条）。
    /// </summary>
    /// <returns>表示刷新完成的 Task。</returns>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            var rows = await _history.QueryAsync(FilterDeviceId, limit: 300);
            Logs.Clear();
            foreach (var row in rows)
                Logs.Add(ToItem(row));
            StatusMessage = $"共 {Logs.Count} 条操作记录";
        }
        catch (Exception ex)
        {
            StatusMessage = "加载历史失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 清空全部历史记录。
    /// </summary>
    /// <returns>表示清空完成的 Task。</returns>
    [RelayCommand]
    private async Task ClearAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            await _history.ClearAsync();
            Logs.Clear();
            StatusMessage = "已清空历史数据";
        }
        catch (Exception ex)
        {
            StatusMessage = "清空失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 将数据库日志实体投影为列表行模型。
    /// </summary>
    /// <param name="log">原始操作日志。</param>
    /// <returns>UI 绑定的 <see cref="HistoryLogItem"/>。</returns>
    private static HistoryLogItem ToItem(DeviceOperationLog log)
    {
        var local = log.OccurredAtUtc.ToLocalTime();
        var alias = string.IsNullOrWhiteSpace(log.VariableAlias) ? log.Address : log.VariableAlias;
        return new HistoryLogItem
        {
            Id = log.Id,
            TimeText = local.ToString("yyyy-MM-dd HH:mm:ss"),
            UserText = string.IsNullOrWhiteSpace(log.Username) ? "-" : log.Username,
            Source = log.Source,
            DeviceText = $"#{log.DeviceId} {log.DeviceName}".Trim(),
            Operation = log.Operation switch
            {
                "Read" => "读取",
                "Write" => "写入",
                "ReadAll" => "全部读取",
                _ => log.Operation,
            },
            TargetText = string.IsNullOrWhiteSpace(alias) ? "-" : alias,
            ValueText = log.Value ?? "-",
            ResultText = log.Success ? "成功" : (log.Error ?? "失败"),
            Success = log.Success,
        };
    }
}
