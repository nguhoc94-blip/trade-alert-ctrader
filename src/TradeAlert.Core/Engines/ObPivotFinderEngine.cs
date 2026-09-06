using System;
using TradeAlert.Core.Drawing;
using TradeAlert.Core.Models.KeyLevel;

namespace TradeAlert.Core.Engines;

/// <summary>Pine <c>f_find_and_draw_OB</c> (pivot OB, source=0).</summary>
public static class ObPivotFinderEngine
{
    public static void FindAndDraw(
        int pivotIdx,
        PivotStateStore pivots,
        ObPoolStore pool,
        int barIndex,
        int obScanBars,
        double minObBodyRatio,
        int lookbackBars,
        bool showObBox,
        bool useObConfirm,
        bool hideObLoseZin,
        Func<int, (double open, double high, double low, double close)> ohlcAtLag,
        Func<int, DateTime> chartTimeAtLag,
        IDrawingCommandSink? sink)
    {
        if (pivotIdx < 0 || pivotIdx >= pivots.Count) return;

        var flag = pivots.GetFlag(pivotIdx);
        if (flag != 1 && flag != 4) return;
        if (pool.GetPivotObBar(pivotIdx).HasValue) return;

        var kb = pivots.GetKeyBox(pivotIdx);
        if (kb == null) return;

        var sBar = pivots.GetBarIndexInternal(pivotIdx);
        var typ  = pivots.GetTypeAt(pivotIdx);
        var keyTop = kb.Spec.Top;
        var keyBot = kb.Spec.Bottom;

        var start = Math.Min(barIndex - 2, sBar + obScanBars - 3);
        var end   = sBar - 2;
        if (start < end) return;

        int? foundBar = null;
        for (var b = start; b >= end; b--)
        {
            if (b < 0) break;
            var lag = barIndex - b;
            if (lag < 0 || lag > 180) continue;

            var t = ohlcAtLag(lag);
            var isGreen = t.close > t.open;
            var isRed   = t.close < t.open;
            var colorOk = typ == 1 ? isGreen : typ == -1 && isRed;
            if (!colorOk) continue;

            var isNear = b >= sBar - 2 && b <= sBar;
            var touch = isNear
                || (typ == 1
                    ? t.high >= keyBot && t.low <= keyTop
                    : t.low <= keyTop && t.high >= keyBot);
            if (!touch) continue;

            foundBar = b;
            break;
        }

        if (!foundBar.HasValue || pool.Exists(foundBar.Value, pivotIdx, 0))
            return;

        var foundLag = barIndex - foundBar.Value;
        if (foundLag < 0 || foundLag > 180) return;
        if (!KeyLevelEngine.IsValidObCandle(foundLag, minObBodyRatio, 0.0, ohlcAtLag))
            return;

        var ft = ohlcAtLag(foundLag);
        var xOb = typ == 1 ? ft.low : ft.high;

        ObPoolMaintenance.PushOb(
            pool, barIndex, foundBar.Value, xOb, typ, pivotIdx, source: 0,
            pivotType: typ,
            pivotHighId: pivots.GetHighId(pivotIdx),
            pivotLowId: pivots.GetLowId(pivotIdx),
            obScanBars, ohlcAtLag, chartTimeAtLag, lookbackBars, showObBox, useObConfirm, hideObLoseZin, sink);
    }

    public static void ScanAllActivePivots(
        PivotStateStore pivots,
        ObPoolStore pool,
        int barIndex,
        int obScanBars,
        double minObBodyRatio,
        int lookbackBars,
        bool showObBox,
        bool useObConfirm,
        bool hideObLoseZin,
        Func<int, (double open, double high, double low, double close)> ohlcAtLag,
        Func<int, DateTime> chartTimeAtLag,
        IDrawingCommandSink? sink)
    {
        for (var i = 0; i < pivots.Count; i++)
        {
            var f = pivots.GetFlag(i);
            if (f != 1 && f != 4) continue;
            if (pool.GetPivotObBar(i).HasValue) continue;
            FindAndDraw(i, pivots, pool, barIndex, obScanBars, minObBodyRatio,
                lookbackBars, showObBox, useObConfirm, hideObLoseZin, ohlcAtLag, chartTimeAtLag, sink);
        }
    }
}
