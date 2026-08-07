using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lana.Gateway.Models;
using Lana.Gateway.Services;

namespace Lana.ViewModels;

public sealed class HistoryLogItem
{
    public long Id { get; init; }
    public string TimeText { get; init; } = string.Empty;
    public string UserText { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string DeviceText { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public string TargetText { get; init; } = string.Empty;
    public string ValueText { get; init; } = string.Empty;
    public string ResultText { get; init; } = string.Empty;
    public bool Success { get; init; }
}

public sealed class HistoryDeviceFilterItem
{
    public long? Id { get; init; }
    public string DisplayName { get; init; } = string.Empty;
}

public partial class HistoryViewModel : ViewModelBase
{
    private readonly DeviceOperationHistoryService _history;
    private readonly GatewayDeviceService _devices;

    public HistoryViewModel(DeviceOperationHistoryService history, GatewayDeviceService devices)
    {
        _history = history;
        _devices = devices;
        _ = InitializeAsync();
    }

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private long? _filterDeviceId;

    public ObservableCollection<HistoryDeviceFilterItem> DeviceFilters { get; } = [];

    public ObservableCollection<HistoryLogItem> Logs { get; } = [];

    partial void OnFilterDeviceIdChanged(long? value)
        => _ = RefreshAsync();

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
