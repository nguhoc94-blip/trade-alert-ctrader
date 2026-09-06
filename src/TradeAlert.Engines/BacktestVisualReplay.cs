using System;
using System.Collections.Generic;
using cAlgo.API;
using TradeAlert.Core.Models;

namespace TradeAlert.Indicator;

/// <summary>
/// Replays M1 or M5 sub-bars within each chart bar so headless backtest matches
/// cTrader VisualBacktesting (many Calculate passes per HTF bar).
/// </summary>
public static class BacktestVisualReplay
{
    /// <summary>
    /// Collect sub-bar indices (M1/M5) whose open time falls in [htfOpen, htfClose).
    /// Returns empty when series unavailable — caller falls back to bar-close eval.
    /// </summary>
    public static IReadOnlyList<int> CollectSubBarIndicesInHtfBar(
        Bars subBars,
        DateTime htfOpenLocal,
        DateTime htfCloseLocal)
    {
        if (subBars.Count == 0)
            return Array.Empty<int>();

        var start = FindFirstBarAtOrAfter(subBars, htfOpenLocal);
        if (start >= subBars.Count)
            return Array.Empty<int>();

        var result = new List<int>();
        for (var i = start; i < subBars.Count; i++)
        {
            var open = ChartTimeFromBars.NormalizeBarOpenTime(subBars.OpenTimes[i]);
            if (open >= htfCloseLocal)
                break;
            if (open >= htfOpenLocal)
                result.Add(i);
        }

        return result;
    }

    /// <inheritdoc cref="CollectSubBarIndicesInHtfBar"/>
    public static IReadOnlyList<int> CollectM1IndicesInHtfBar(
        Bars m1Bars,
        DateTime htfOpenLocal,
        DateTime htfCloseLocal) =>
        CollectSubBarIndicesInHtfBar(m1Bars, htfOpenLocal, htfCloseLocal);

    /// <summary>Build cumulative OHLC for chart bar through sub-bar step <paramref name="subBarIndex"/>.</summary>
    public static BarSnapshot BuildPartialChartBarSnapshot(
        Bars chartBars,
        int chartBarIndex,
        Bars subBars,
        int subBarIndex,
        double runningHigh,
        double runningLow)
    {
        var open = chartBars.OpenPrices[chartBarIndex];
        var close = subBars.ClosePrices[subBarIndex];
        var high = Math.Max(runningHigh, subBars.HighPrices[subBarIndex]);
        var low = Math.Min(runningLow, subBars.LowPrices[subBarIndex]);
        var openTime = chartBars.OpenTimes[chartBarIndex];

        return BarsToCoreAdapter.ToBarSnapshot(
            openTime,
            open,
            high,
            low,
            close,
            (long)chartBars.TickVolumes[chartBarIndex]);
    }

    public static DateTime SubBarCloseLocal(Bars subBars, int subBarIndex, TimeSpan subBarPeriod) =>
        ChartTimeFromBars.NormalizeBarOpenTime(subBars.OpenTimes[subBarIndex]).Add(subBarPeriod);

    public static DateTime M1BarCloseLocal(Bars m1Bars, int m1Index) =>
        SubBarCloseLocal(m1Bars, m1Index, TimeSpan.FromMinutes(1));

    static int FindFirstBarAtOrAfter(Bars bars, DateTime target)
    {
        var lo = 0;
        var hi = bars.Count - 1;
        var result = bars.Count;
        while (lo <= hi)
        {
            var mid = lo + (hi - lo) / 2;
            var t = ChartTimeFromBars.NormalizeBarOpenTime(bars.OpenTimes[mid]);
            if (t >= target)
            {
                result = mid;
                hi = mid - 1;
            }
            else
                lo = mid + 1;
        }

        return result;
    }
}
