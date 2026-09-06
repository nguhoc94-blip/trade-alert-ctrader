using System;
using TradeAlert.Core.Drawing;
using TradeAlert.Core.Models.KeyLevel;

namespace TradeAlert.Core.Engines;

/// <summary>Pine <c>f_find_and_draw_OB_structural</c> (source=1, after MAIN C promote).</summary>
public static class ObStructuralEngine
{
    public static void FindAndDrawStructural(
        int cIdx,
        int dIdx,
        PivotStateStore pivots,
        ObPoolStore pool,
        int barIndex,
        double minObBodyRatio,
        double maxDojiBodyRatio,
        int lookbackBars,
        bool showObBox,
        bool useObConfirm,
        bool hideObLoseZin,
        Func<int, (double open, double high, double low, double close)> ohlcAtLag,
        Func<int, DateTime> chartTimeAtLag,
        IDrawingCommandSink? sink,
        int obScanBars = 20)  // Gap 4.2: Pine uses global obScanBars, not barD-barC+5
    {
        if (cIdx < 0 || dIdx < 0 || cIdx >= pivots.Count || dIdx >= pivots.Count)
            return;

        var bIdx = pivots.GetParent(cIdx);
        if (bIdx == PivotStateStore.FirstDNa || bIdx < 0 || bIdx >= pivots.Count)
            return;

        var kb = pivots.GetKeyBox(bIdx);
        if (kb == null) return;

        var barC  = pivots.GetBarIndexInternal(cIdx);
        var barD  = pivots.GetBarIndexInternal(dIdx);
        var typC  = pivots.GetTypeAt(cIdx);
        var keyTop = kb.Spec.Top;
        var keyBot = kb.Spec.Bottom;
        var pivotObBar = pool.GetPivotObBar(cIdx);

        for (var b = barC; b <= barD; b++)
        {
            var lag = barIndex - b;
            if (lag < 0 || lag > 180) continue;

            if (pivotObBar.HasValue && b == pivotObBar.Value)
                continue;

            var t = ohlcAtLag(lag);
            var inCb = typC == 1 ? t.high >= keyBot : t.low <= keyTop;
            if (!inCb) break;

            var colorOk = typC == 1 ? t.close > t.open : t.close < t.open;
            if (!colorOk) continue;

            if (pool.Exists(b, cIdx, 1))
                continue;

            var strictMin = Math.Max(0.25, minObBodyRatio * 0.7);
            if (!KeyLevelEngine.IsValidObCandle(lag, strictMin, maxDojiBodyRatio, ohlcAtLag))
                continue;

            var xOb = typC == 1 ? t.low : t.high;
            ObPoolMaintenance.PushOb(
                pool, barIndex, b, xOb, typC, cIdx, source: 1,
                pivotType: typC,
                pivotHighId: pivots.GetHighId(cIdx),
                pivotLowId: pivots.GetLowId(cIdx),
                obScanBars: obScanBars,   // Gap 4.2: use global obScanBars (was barD-barC+5)
                ohlcAtLag, chartTimeAtLag, lookbackBars, showObBox, useObConfirm, hideObLoseZin, sink);
        }
    }
}
