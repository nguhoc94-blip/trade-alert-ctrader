using System;
using TradeAlert.Core.Models.KeyLevel;

namespace TradeAlert.Core.Engines;

/// <summary>Aggregate partial HTF OHLC from clipped LTF slice — no lookahead past clipBefore.</summary>
public static class HtfFormingBarPartial
{
    public static bool TryAggregateFromLtfBundle(
        double htfBarOpen,
        LtfBarBundle? ltfSlice,
        out double high,
        out double low,
        out double close)
    {
        high = htfBarOpen;
        low = htfBarOpen;
        close = htfBarOpen;

        if (ltfSlice is null || ltfSlice.Count == 0)
            return false;

        high = ltfSlice.HighAt(0);
        low = ltfSlice.LowAt(0);
        close = ltfSlice.CloseAt(ltfSlice.Count - 1);
        for (var i = 0; i < ltfSlice.Count; i++)
        {
            if (ltfSlice.HighAt(i) > high) high = ltfSlice.HighAt(i);
            if (ltfSlice.LowAt(i) < low) low = ltfSlice.LowAt(i);
        }

        return true;
    }
}
