namespace AvaloniaUse.ViewModels;

public partial class AnalyticsViewModel : ViewModelBase
{
    public string Title => "数据";

    public string Subtitle => "运行指标与使用情况概览";

    public IReadOnlyList<MetricItem> Metrics { get; } =
    [
        new("今日启动", "12", "+18%"),
        new("活跃会话", "3", "稳定"),
        new("主题切换", "7", "Aurora/Snow"),
        new("登录成功率", "100%", "SQLite"),
    ];

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

public sealed record MetricItem(string Name, string Value, string Hint);

public sealed record TrendItem(string Day, double Value);
