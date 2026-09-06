using TradeAlert.Core.Drawing;
using TradeAlert.Core.Models;
using TradeAlert.Core.Models.KeyLevel;
using TradeAlert.Core.Series;

namespace TradeAlert.Core.Engines;

/// <summary>
/// Pine per-bar keylevel pipeline: extend, key-break stop, overlap soft-hide, sync/recreate.
/// Order matches Pine ~4941–4950.
/// </summary>
public static class KeyLevelDrawEngine
{
    const double KeyBreakAtrMult = 0.2;

    /// <summary>OHLC + stop bar for key-break on a <b>closed</b> bar only.</summary>
    public readonly struct KeyBreakCheckContext
    {
        public bool ShouldRun { get; init; }
        public int StopBarIndex { get; init; } = -1;
        public double Open { get; init; }
        public double High { get; init; }
        public double Low { get; init; }
        public double Close { get; init; }
    }

    /// <summary>
    /// Pine key-break timing: backfill/history + closed bars → OHLC [0];
    /// bar transition (IsNew, !IsHistory) → OHLC [1]; skip forming last bar.
    /// </summary>
    public static KeyBreakCheckContext ResolveKeyBreakCheck(SeriesBuffer series, int barIndex)
    {
        if (!series.TryGetCurrentFlags(out var flags))
            return default;

        // Attach/reload (ForAttachHistoryPhaseLastBar): last bar OHLC chưa final.
        if (flags.IsLast && flags.IsHistory && !flags.IsRealtime && !flags.IsNew)
            return default;

        // Live realtime forming tick.
        if (flags.IsRealtime && !flags.IsConfirmed && !flags.IsNew)
            return default;

        // Backtest forming last bar (mid-tick, bar chưa đóng).
        if (flags.IsLast && !flags.IsRealtime && !flags.IsHistory && !flags.IsNew && !flags.IsConfirmed)
            return default;

        // Bar vừa đóng (live hoặc backtest OnBar / open-time change).
        if (flags.IsNew && !flags.IsHistory && barIndex >= 1)
        {
            if (!series.TryGetOhlcAtOffset(1, out var ohlc1))
                return default;

            return new KeyBreakCheckContext
            {
                ShouldRun    = true,
                StopBarIndex = barIndex - 1,
                Open         = ohlc1.Open,
                High         = ohlc1.High,
                Low          = ohlc1.Low,
                Close        = ohlc1.Close,
            };
        }

        // Historical backfill sweep (IsHistory — không dùng offset [1] dù IsNew=true).
        if (flags.IsHistory && flags.IsConfirmed)
            return BuildOhlc0Context(series, barIndex);

        // Closed bar trong series (không phải nến cuối).
        if (!flags.IsLast && flags.IsConfirmed)
            return BuildOhlc0Context(series, barIndex);

        // Nến cuối chart lịch sử đã đóng (không còn bar sau).
        if (flags.IsLast && flags.IsConfirmed && !flags.IsHistory && !flags.IsNew)
            return BuildOhlc0Context(series, barIndex);

        return default;
    }

    static KeyBreakCheckContext BuildOhlc0Context(SeriesBuffer series, int barIndex)
    {
        if (!series.TryGetOhlcAtOffset(0, out var ohlc0))
            return default;

        return new KeyBreakCheckContext
        {
            ShouldRun    = true,
            StopBarIndex = barIndex,
            Open         = ohlc0.Open,
            High         = ohlc0.High,
            Low          = ohlc0.Low,
            Close        = ohlc0.Close,
        };
    }

    public static void Tick(
        KeyLevelTickContext ctx,
        PivotStateStore pivots,
        in KeyBreakCheckContext keyBreak,
        IDrawingCommandSink? sink,
        ObPoolStore? obPool = null)
    {
        if (sink == null) return;

        var barIndex = ctx.BarIndex;
        var atr14    = ctx.AtrValue;

        // 1) f_limit_overlapping_keylevels_soft
        KeyLevelOverlapEngine.LimitOverlappingKeylevelsSoft(pivots, ctx.MaxKeylevelKeep, sink);

        // 2) f_sync_keybox_with_flag — recreate BROKEN / MAIN C sau soft-hide
        for (var i = 0; i < pivots.Count; i++)
            KeyLevelSyncEngine.SyncKeyboxWithFlag(i, pivots, ctx, sink, obPool);

        // 3) f_check_key_*_B_break then extend.right (stop trước extend — khớp Pine cùng bar)
        for (var i = 0; i < pivots.Count; i++)
        {
            var kb = pivots.GetKeyBox(i);
            if (kb?.Spec == null) continue;
            if (!pivots.GetKeyVisible(i)) continue;

            var spec   = kb.Spec;
            var kbName = KeyLevelVisual.ChartObjectName(spec);

            if (keyBreak.ShouldRun)
            {
                TryStopOnKeyBreak(
                    i, pivots, spec,
                    keyBreak.Open, keyBreak.High, keyBreak.Low, keyBreak.Close,
                    atr14, keyBreak.StopBarIndex, sink);
            }

            if (pivots.GetKeyExtending(i))
            {
                sink.Enqueue(new DrawingCommand
                {
                    Kind        = DrawingCommandKind.ExtendKeyBox,
                    LabelKeyStr = kbName,
                    BarIndex    = barIndex,
                });
            }
        }
    }

    static bool TryStopOnKeyBreak(
        int i,
        PivotStateStore pivots,
        KeyBoxSpec spec,
        double open,
        double high,
        double low,
        double close,
        double atr14,
        int barIndex,
        IDrawingCommandSink? sink)
    {
        if (pivots.GetKeyBreakLabeled(i)) return false;
        if (!pivots.GetKeyExtending(i)) return false;

        var flag = pivots.GetFlag(i);
        if (flag != 2 && flag != 6) return false;

        var typ = pivots.GetTypeAt(i);
        var strong = !double.IsNaN(atr14) && atr14 > 0 && Math.Abs(close - open) >= atr14 * KeyBreakAtrMult;

        bool shouldStop;
        if (typ == -1)
            shouldStop = close > open && strong && close > spec.Top;
        else if (typ == 1)
            shouldStop = close < open && strong && close < spec.Bottom;
        else
            return false;

        if (!shouldStop) return false;

        PivotTransitionEngine.EmitStopExtendKeyBox(
            pivots, i, barIndex, sink,
            rightEdgeBarIndex: null,
            stopRightAtBreakBar: false);

        pivots.SetKeyStopBar(i, barIndex);
        pivots.SetKeyBreakLabeled(i, true);
        return true;
    }
}
