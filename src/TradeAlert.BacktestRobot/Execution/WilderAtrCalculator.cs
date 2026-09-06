using System;
using System.Collections.Generic;
using TradeAlert.Core.Models;
using TradeAlert.Core.Series;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>Wilder RMA ATR — matches Pine <c>ta.atr(len)</c>.</summary>
public static class WilderAtrCalculator
{
    public static bool TryComputeSeries(
        SeriesBuffer series,
        int atrPeriod,
        int evalBarIndex,
        out double[] atrByBarIndex)
    {
        atrByBarIndex = Array.Empty<double>();
        if (atrPeriod <= 0 || evalBarIndex < 0)
            return false;

        var current = series.CurrentEvaluationBarIndex;
        var maxBar = Math.Min(evalBarIndex, current);
        if (maxBar < 0)
            return false;

        atrByBarIndex = new double[maxBar + 1];
        double atrWilder = double.NaN;
        double smaSum = 0;
        var barsSeen = 0;

        for (var bar = 0; bar <= maxBar; bar++)
        {
            var offset = current - bar;
            if (offset < 0 || !series.TryGetOhlcAtOffset(offset, out var cur))
                continue;

            var tr = ComputeTrueRange(series, bar, current, in cur);

            barsSeen++;
            if (barsSeen < atrPeriod)
            {
                smaSum += tr;
                atrWilder = double.NaN;
            }
            else if (barsSeen == atrPeriod)
            {
                smaSum += tr;
                atrWilder = smaSum / atrPeriod;
            }
            else
            {
                atrWilder = atrWilder + (tr - atrWilder) / atrPeriod;
            }

            atrByBarIndex[bar] = atrWilder;
        }

        return barsSeen >= atrPeriod;
    }

    static double ComputeTrueRange(SeriesBuffer series, int barIndex, int currentBar, in OhlcTuple cur)
    {
        if (barIndex <= 0)
            return cur.High - cur.Low;

        var prevOffset = currentBar - (barIndex - 1);
        if (prevOffset >= 0 && series.TryGetOhlcAtOffset(prevOffset, out var prev))
        {
            return Math.Max(cur.High - cur.Low,
                Math.Max(Math.Abs(cur.High - prev.Close),
                    Math.Abs(cur.Low - prev.Close)));
        }

        return cur.High - cur.Low;
    }

    public static bool TryGetAtrAtBar(double[] atrByBarIndex, int barIndex, out double atr)
    {
        atr = double.NaN;
        if (barIndex < 0 || barIndex >= atrByBarIndex.Length)
            return false;

        atr = atrByBarIndex[barIndex];
        return !double.IsNaN(atr) && atr > 0;
    }

    public static bool TrySmaOfAtr(double[] atrByBarIndex, int endBarIndex, int period, out double sma)
    {
        sma = double.NaN;
        if (period <= 0 || endBarIndex < 0)
            return false;

        var sum = 0.0;
        var count = 0;
        for (var b = endBarIndex; b >= 0 && count < period; b--)
        {
            if (b >= atrByBarIndex.Length)
                continue;
            var v = atrByBarIndex[b];
            if (double.IsNaN(v) || v <= 0)
                continue;
            sum += v;
            count++;
        }

        if (count < period)
            return false;

        sma = sum / period;
        return sma > 0;
    }
}
