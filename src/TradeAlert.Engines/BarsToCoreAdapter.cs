using TradeAlert.Core.Models;

namespace TradeAlert.Indicator;

/// <summary>OHLC + open time từ feed (cTrader/DATA CHART) → <see cref="BarSnapshot"/>.</summary>
public static class BarsToCoreAdapter
{
    public static BarSnapshot ToBarSnapshot(
        DateTime barsOpenTimeRaw,
        double open,
        double high,
        double low,
        double close,
        long volume = 0)
    {
        var t = ChartTimeFromBars.NormalizeBarOpenTime(barsOpenTimeRaw);
        return new BarSnapshot(t, open, high, low, close, volume).NormalizeChartTime();
    }
}
