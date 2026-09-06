using TradeAlert.Core.Drawing;
using TradeAlert.Core.Models.KeyLevel;

namespace TradeAlert.Core.Engines;

/// <summary>
/// Pine <c>TRIM PIVOTS OUTSIDE LOOKBACK RANGE</c> (~4428–4537) + push range filter (~3840).
/// Lookback controls pivot pool lifecycle and display — not break/lock engine gates.
/// </summary>
public static class PivotTrimEngine
{
    public static bool IsWithinLookbackWindow(int barIndex, int pivotBar, int lookbackBars) =>
        barIndex - pivotBar <= lookbackBars;

    /// <summary>Remove oldest pivots while <c>pivBar &lt; barIndex - lookbackBars</c>.</summary>
    public static int TrimOutsideLookback(
        int barIndex,
        int lookbackBars,
        PivotStateStore pivots,
        ObPoolStore? obPool,
        IDrawingCommandSink? sink)
    {
        var minBarIndex = barIndex - lookbackBars;
        BackfillPrevConnectBars(pivots);
        var trimmed = 0;
        while (pivots.Count > 0 && pivots.GetBarIndexInternal(0) < minBarIndex)
        {
            TrimOldestPivot(pivots, obPool, sink);
            trimmed++;
        }

        ReconcileLinesOutsideLookback(minBarIndex, pivots, sink);
        return trimmed;
    }

    static void BackfillPrevConnectBars(PivotStateStore pivots)
    {
        for (var i = 1; i < pivots.Count; i++)
        {
            if (pivots.GetPrevConnectBar(i) >= 0) continue;
            pivots.SetPrevConnectBar(i, pivots.GetBarIndexInternal(i - 1));
        }
    }

    static void TrimOldestPivot(
        PivotStateStore pivots,
        ObPoolStore? obPool,
        IDrawingCommandSink? sink)
    {
        if (pivots.Count == 0) return;

        var bar0 = pivots.GetBarIndexInternal(0);
        var prevBar = pivots.GetPrevConnectBar(0);
        if (prevBar >= 0)
            EmitDeleteLine(prevBar, bar0, sink);
        if (pivots.Count >= 2)
        {
            var bar1 = pivots.GetBarIndexInternal(1);
            EmitDeleteLine(bar0, bar1, sink);
        }

        if (obPool != null)
            ObPoolMaintenance.DeleteObsOfPivot(obPool, 0, sink);

        KeyLevelMaintenance.DeleteKeylevelInternal(0, pivots, sink, obPool, deleteObs: false);
        EmitDeleteSwingLabel(pivots, 0, sink);

        pivots.RemoveAt(0);
        AdjustIndexReferencesAfterShift(pivots);
        if (pivots.Count > 0 && pivots.GetPrevConnectBar(0) == bar0)
            pivots.SetPrevConnectBar(0, -1);
        obPool?.ShiftOwnersAfterPivotRemovedAtZero();
    }

    /// <summary>
    /// Remove orphan segment to first pool pivot when its predecessor was already trimmed.
    /// </summary>
    static void ReconcileLinesOutsideLookback(
        int minBarIndex,
        PivotStateStore pivots,
        IDrawingCommandSink? sink)
    {
        if (sink == null || pivots.Count == 0) return;

        var prev = pivots.GetPrevConnectBar(0);
        if (prev >= 0 && prev < minBarIndex)
        {
            var b0 = pivots.GetBarIndexInternal(0);
            EmitDeleteLine(prev, b0, sink);
            pivots.SetPrevConnectBar(0, -1);
        }

        for (var i = 1; i < pivots.Count; i++)
        {
            var bPrev = pivots.GetBarIndexInternal(i - 1);
            var bCur  = pivots.GetBarIndexInternal(i);
            if (bPrev < minBarIndex)
                EmitDeleteLine(bPrev, bCur, sink);
        }
    }

    static void EmitDeleteLine(int barFrom, int barTo, IDrawingCommandSink? sink)
    {
        if (sink == null) return;
        sink.Enqueue(new DrawingCommand
        {
            Kind        = DrawingCommandKind.DelLine,
            LabelKeyStr = $"ln_{barFrom}_{barTo}",
        });
    }

    static void AdjustIndexReferencesAfterShift(PivotStateStore pivots)
    {
        for (var g = 0; g < pivots.Count; g++)
        {
            pivots.SetKeyGroupId(g, DecrementNaIndexRef(pivots.GetKeyGroupId(g)));
            pivots.SetFirstDIdx(g, DecrementFirstDIdx(pivots.GetFirstDIdx(g)));
            pivots.SetParent(g, DecrementParentRef(pivots.GetParent(g)));
            pivots.SetMainParent(g, DecrementParentRef(pivots.GetMainParent(g)));
            pivots.SetMainDIdx(g, DecrementNaIndexRef(pivots.GetMainDIdx(g)));
        }
    }

    static int DecrementNaIndexRef(int value)
    {
        if (value > 0) return value - 1;
        if (value == 0) return PivotStateStore.FirstDNa;
        return value;
    }

    static int DecrementFirstDIdx(int value)
    {
        if (value > 0) return value - 1;
        if (value == 0) return PivotStateStore.FirstDNa;
        return value;
    }

    static int DecrementParentRef(int value)
    {
        if (value > 0) return value - 1;
        if (value == 0) return -1;
        return value;
    }

    static void EmitDeleteSwingLabel(PivotStateStore pivots, int i, IDrawingCommandSink? sink)
    {
        if (sink == null || i < 0 || i >= pivots.Count) return;

        var typ = pivots.GetTypeAt(i);
        var sid = typ == 1 ? $"H{pivots.GetHighId(i)}" : $"L{pivots.GetLowId(i)}";
        sink.Enqueue(new DrawingCommand
        {
            Kind        = DrawingCommandKind.DelLabel,
            LabelKeyStr = $"sw_{sid}",
        });
    }
}
