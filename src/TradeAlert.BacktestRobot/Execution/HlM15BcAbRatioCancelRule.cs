using System;
using System.Globalization;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.KeyLevel;
using TradeAlert.Indicator;

namespace TradeAlert.BacktestRobot.Execution;

public sealed class HlM15BcAbRatioCancelResult
{
    public bool ShouldCancel { get; init; }
    public string? VerboseSkip { get; init; }
    public int ABar { get; init; }
    public int BBar { get; init; }
    public int CBar { get; init; }
    public double AB { get; init; }
    public double BC { get; init; }
    public double BcAbRatio { get; init; }
    public double AEdge { get; init; }
    public double BEdge { get; init; }
    public double CEdge { get; init; }
}

/// <summary>
/// HL M15 pending invalidation: cancel when BC &gt; threshold * AB (default 2.6).
/// R3 BUY: A=nearest HIGH before B, B=setup LOW, C=nearest confirmed HIGH after B.
/// R4 SELL: A=nearest LOW before B, B=setup HIGH, C=nearest confirmed LOW after B.
/// </summary>
public static class HlM15BcAbRatioCancelRule
{
    public const string CancelReason = "hl-m15-bcab-ratio";

    public static HlM15BcAbRatioCancelResult Evaluate(
        in PendingOrderContext ctx,
        PerSymbolSignalHost host,
        in HlM15BcAbRatioCancelRuleConfig cfg)
    {
        if (!host.TryGetState(ctx.SwingBTfToken, out var state))
            state = host.State;

        return Evaluate(state, in ctx, in cfg);
    }

    public static HlM15BcAbRatioCancelResult Evaluate(
        PineStateEngine chartState,
        in PendingOrderContext ctx,
        in HlM15BcAbRatioCancelRuleConfig cfg)
    {
        var pivots = chartState.Pivots;
        var bBar = ctx.SwingBPivotBar;

        if (ctx.IsBuy)
            return EvaluateBuy(pivots, in ctx, bBar, in cfg);

        return EvaluateSell(pivots, in ctx, bBar, in cfg);
    }

    static HlM15BcAbRatioCancelResult EvaluateBuy(
        PivotStateStore pivots,
        in PendingOrderContext ctx,
        int bBar,
        in HlM15BcAbRatioCancelRuleConfig cfg)
    {
        var aIdx = FindNearestPivotBefore(pivots, bBar, SwingBResolver.TypeHigh);
        if (aIdx < 0)
            return Skip(cfg.VerboseSkipLog, $"BCAB SKIP no A label={ctx.Label} Bbar={bBar}");

        var cIdx = FindNearestPivotAfter(pivots, bBar, SwingBResolver.TypeHigh);
        if (cIdx < 0)
            return NoCancel();

        var aTop = PivotTop(pivots, aIdx);
        var bBottom = ResolveBEdgeLow(pivots, in ctx);
        var cTop = PivotTop(pivots, cIdx);

        var ab = aTop - bBottom;
        var bc = cTop - bBottom;

        if (ab <= 0 || bc <= 0)
            return Skip(cfg.VerboseSkipLog,
                $"BCAB SKIP invalid geometry label={ctx.Label} AB={Fmt(ab)} BC={Fmt(bc)} Abar={pivots.GetSnapshot(aIdx).BarIndex} Bbar={bBar} Cbar={pivots.GetSnapshot(cIdx).BarIndex}");

        var ratio = bc / ab;
        if (bc <= cfg.BcAbRatioThreshold * ab)
            return NoCancel();

        return BuildCancel(in ctx, pivots, aIdx, bBar, cIdx, ab, bc, ratio, aTop, bBottom, cTop, in cfg);
    }

    static HlM15BcAbRatioCancelResult EvaluateSell(
        PivotStateStore pivots,
        in PendingOrderContext ctx,
        int bBar,
        in HlM15BcAbRatioCancelRuleConfig cfg)
    {
        var aIdx = FindNearestPivotBefore(pivots, bBar, SwingBResolver.TypeLow);
        if (aIdx < 0)
            return Skip(cfg.VerboseSkipLog, $"BCAB SKIP no A label={ctx.Label} Bbar={bBar}");

        var cIdx = FindNearestPivotAfter(pivots, bBar, SwingBResolver.TypeLow);
        if (cIdx < 0)
            return NoCancel();

        var aBottom = PivotBottom(pivots, aIdx);
        var bTop = ResolveBEdgeHigh(pivots, in ctx);
        var cBottom = PivotBottom(pivots, cIdx);

        var ab = bTop - aBottom;
        var bc = bTop - cBottom;

        if (ab <= 0 || bc <= 0)
            return Skip(cfg.VerboseSkipLog,
                $"BCAB SKIP invalid geometry label={ctx.Label} AB={Fmt(ab)} BC={Fmt(bc)} Abar={pivots.GetSnapshot(aIdx).BarIndex} Bbar={bBar} Cbar={pivots.GetSnapshot(cIdx).BarIndex}");

        var ratio = bc / ab;
        if (bc <= cfg.BcAbRatioThreshold * ab)
            return NoCancel();

        return BuildCancel(in ctx, pivots, aIdx, bBar, cIdx, ab, bc, ratio, aBottom, bTop, cBottom, in cfg);
    }

    static HlM15BcAbRatioCancelResult BuildCancel(
        in PendingOrderContext ctx,
        PivotStateStore pivots,
        int aIdx,
        int bBar,
        int cIdx,
        double ab,
        double bc,
        double ratio,
        double aEdge,
        double bEdge,
        double cEdge,
        in HlM15BcAbRatioCancelRuleConfig cfg) =>
        new()
        {
            ShouldCancel = true,
            ABar = pivots.GetSnapshot(aIdx).BarIndex,
            BBar = bBar,
            CBar = pivots.GetSnapshot(cIdx).BarIndex,
            AB = ab,
            BC = bc,
            BcAbRatio = ratio,
            AEdge = aEdge,
            BEdge = bEdge,
            CEdge = cEdge,
        };

    public static string FormatCancelLog(in PendingOrderContext ctx, in HlM15BcAbRatioCancelResult result, in HlM15BcAbRatioCancelRuleConfig cfg)
    {
        var rule = $"R{ctx.RuleSlot + 1}";
        var dir = ctx.IsBuy ? "BUY" : "SELL";
        return $"[L6BT] CANCEL {ctx.Label} ({CancelReason}) " +
               $"rule={rule} dir={dir} " +
               $"Abar={result.ABar} Bbar={result.BBar} Cbar={result.CBar} " +
               $"AB={Fmt(result.AB)} BC={Fmt(result.BC)} BC_AB={Fmt(result.BcAbRatio)} threshold={Fmt(cfg.BcAbRatioThreshold)} " +
               $"Aedge={Fmt(result.AEdge)} Bedge={Fmt(result.BEdge)} Cedge={Fmt(result.CEdge)}";
    }

    static int FindNearestPivotBefore(PivotStateStore pivots, int bBar, int wantType)
    {
        var bestIdx = -1;
        var bestBar = int.MinValue;
        for (var i = 0; i < pivots.Count; i++)
        {
            var snap = pivots.GetSnapshot(i);
            if (snap.BarIndex >= bBar)
                continue;
            if (snap.Type != wantType)
                continue;
            if (snap.BarIndex > bestBar)
            {
                bestBar = snap.BarIndex;
                bestIdx = i;
            }
        }

        return bestIdx;
    }

    static int FindNearestPivotAfter(PivotStateStore pivots, int bBar, int wantType)
    {
        var bestIdx = -1;
        var bestBar = int.MaxValue;
        for (var i = 0; i < pivots.Count; i++)
        {
            var snap = pivots.GetSnapshot(i);
            if (snap.BarIndex <= bBar)
                continue;
            if (snap.Type != wantType)
                continue;
            if (snap.BarIndex < bestBar)
            {
                bestBar = snap.BarIndex;
                bestIdx = i;
            }
        }

        return bestIdx;
    }

    static double PivotTop(PivotStateStore pivots, int idx)
    {
        if (pivots.GetHasKey(idx) && pivots.GetKeyBox(idx)?.Spec is { } spec)
            return Math.Max(spec.Top, spec.Bottom);

        return pivots.GetSnapshot(idx).Price;
    }

    static double PivotBottom(PivotStateStore pivots, int idx)
    {
        if (pivots.GetHasKey(idx) && pivots.GetKeyBox(idx)?.Spec is { } spec)
            return Math.Min(spec.Top, spec.Bottom);

        return pivots.GetSnapshot(idx).Price;
    }

    static double ResolveBEdgeLow(PivotStateStore pivots, in PendingOrderContext ctx)
    {
        if (ctx.SwingBKeyHigh > ctx.SwingBKeyLow)
            return ctx.SwingBKeyLow;

        var bIdx = FindPivotIndexByBar(pivots, ctx.SwingBPivotBar);
        if (bIdx >= 0)
            return PivotBottom(pivots, bIdx);

        return ctx.SwingBKeyLow;
    }

    static double ResolveBEdgeHigh(PivotStateStore pivots, in PendingOrderContext ctx)
    {
        if (ctx.SwingBKeyHigh > ctx.SwingBKeyLow)
            return ctx.SwingBKeyHigh;

        var bIdx = FindPivotIndexByBar(pivots, ctx.SwingBPivotBar);
        if (bIdx >= 0)
            return PivotTop(pivots, bIdx);

        return ctx.SwingBKeyHigh;
    }

    static int FindPivotIndexByBar(PivotStateStore pivots, int pivotBar)
    {
        for (var i = pivots.Count - 1; i >= 0; i--)
        {
            if (pivots.GetSnapshot(i).BarIndex == pivotBar)
                return i;
        }

        return -1;
    }

    static HlM15BcAbRatioCancelResult NoCancel() => new() { ShouldCancel = false };

    static HlM15BcAbRatioCancelResult Skip(bool verbose, string message) =>
        new() { ShouldCancel = false, VerboseSkip = verbose ? message : null };

    static string Fmt(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
}
