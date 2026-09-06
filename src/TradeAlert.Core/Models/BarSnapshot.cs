namespace TradeAlert.Core.Models;

/// <summary>
/// OHLC một nến. Thời gian mở nến canonical: <see cref="OpenChartTimeLocal"/> (<see cref="DateTimeKind.Unspecified"/> = chart local wall clock
/// từ baseline DATA CHART, không phải machine local). REV_003 Engineering.
/// </summary>
public readonly record struct BarSnapshot(
    DateTime OpenChartTimeLocal,
    double Open,
    double High,
    double Low,
    double Close,
    long Volume = 0)
{
    /// <summary>
    /// UTC instant derived từ chart local UTC+7 — chỉ dùng tích hợp/logging khi cần; parity Loop 2 dùng <see cref="OpenChartTimeLocal"/>.
    /// </summary>
    public DateTime OpenTimeUtcDerived =>
        ChartTimePolicy.ChartLocalUnspecifiedToUtcInstant(OpenChartTimeLocal);

    /// <summary>Chuẩn hóa cờ thời gian: giữ wall clock chart local, không gọi ToUniversalTime() theo máy.</summary>
    public BarSnapshot NormalizeChartTime()
    {
        var t = ChartTimePolicy.EnsureChartLocalUnspecified(OpenChartTimeLocal);
        return this with { OpenChartTimeLocal = t };
    }
}
