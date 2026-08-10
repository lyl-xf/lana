namespace Lana.ViewModels;

/// <summary>遗留「数据」页占位，当前未挂到 Shell 导航；可复用或删除。</summary>
public partial class AnalyticsViewModel : ViewModelBase
{
    /// <summary>页面标题。</summary>
    public string Title => "数据";

    /// <summary>页面副标题。</summary>
    public string Subtitle => "运行指标与使用情况概览";

    /// <summary>概览指标卡片列表（静态演示数据）。</summary>
    public IReadOnlyList<MetricItem> Metrics { get; } =
    [
        new("今日启动", "12", "+18%"),
        new("活跃会话", "3", "稳定"),
        new("主题切换", "7", "Aurora/Snow"),
        new("登录成功率", "100%", "SQLite"),
    ];

    /// <summary>趋势图数据点列表（静态演示数据）。</summary>
    public IReadOnlyList<TrendItem> Trends { get; } =
    [
        new("周一", 40),
        new("周二", 55),
        new("周三", 48),
        new("周四", 72),
        new("周五", 66),
        new("周六", 30),
        new("周日", 24),
    ];
}

/// <summary>指标卡片项（名称、数值、提示）。</summary>
/// <param name="Name">指标名称。</param>
/// <param name="Value">指标数值。</param>
/// <param name="Hint">附加提示（如同比变化）。</param>
public sealed record MetricItem(string Name, string Value, string Hint);

/// <summary>趋势图数据点（日期标签与数值）。</summary>
/// <param name="Day">日期或星期标签。</param>
/// <param name="Value">对应数值。</param>
public sealed record TrendItem(string Day, double Value);
