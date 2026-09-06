using System;
using System.Collections.Generic;

namespace TradeAlert.Core.Engines;

/// <summary>
/// Aggregate partial HTF OHLC from closed M5 bars up to snapshot — no lookahead past snapshot.
/// </summary>
public static class HtfPartialOhlcFromM5
{
    public static bool TryAggregate(
        IReadOnlyList<DateTime> m5OpenTimesRaw,
        IReadOnlyList<double> m5Opens,
        IReadOnlyList<double> m5Highs,
        IReadOnlyList<double> m5Lows,
        IReadOnlyList<double> m5Closes,
        TimeSpan m5Period,
        DateTime htfOpen,
        DateTime snapshotCloseTime,
        Func<DateTime, DateTime>? normalizeTime,
        out double open,
        out double high,
        out double low,
        out double close,
        out double touchLtfOpen)
    {
        open = high = low = close = touchLtfOpen = 0;
        normalizeTime ??= static t => t;

        if (m5OpenTimesRaw.Count == 0)
            return false;

        var found = false;
        for (var i = 0; i < m5OpenTimesRaw.Count; i++)
        {
            var m5Open = normalizeTime(m5OpenTimesRaw[i]);
            var m5Close = m5Open.Add(m5Period);
            if (m5Close <= htfOpen)
                continue;
            if (m5Open >= snapshotCloseTime)
                break;
            if (m5Close > snapshotCloseTime)
                continue;

            var o = m5Opens[i];
            var h = m5Highs[i];
            var l = m5Lows[i];
            var c = m5Closes[i];
            if (!found)
            {
                open = o;
                high = h;
                low = l;
                close = c;
                touchLtfOpen = o;
                found = true;
            }
            else
            {
                if (h > high) high = h;
                if (l < low) low = l;
                close = c;
                touchLtfOpen = o;
            }
        }

        return found;
    }
}
