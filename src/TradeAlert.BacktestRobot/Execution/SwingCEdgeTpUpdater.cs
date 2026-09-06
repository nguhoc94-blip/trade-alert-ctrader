using System;
using System.Collections.Generic;
using System.Globalization;
using TradeAlert.Core.Engines;

namespace TradeAlert.BacktestRobot.Execution;

public sealed class SwingCTpUpdateAction
{
    public bool ShouldUpdate { get; init; }
    public bool ShouldCancel { get; init; }
    public double OldTakeProfit { get; init; }
    public double NewTakeProfit { get; init; }
    public SwingCEdgeResult? SwingC { get; init; }
    public string Reason { get; init; } = "";
}

public static class SwingCEdgeTpUpdater
{
    public const string CancelReasonSwingCRrBelowBase = "Swing-C-rr-below-base";

    public static SwingCTpUpdateAction Evaluate(
        TrackedSetup setup,
        PineStateEngine chartState,
        bool wantsLiveTpUpdate,
        bool useSwingC,
        ISet<int> ruleSlots,
        double tickSize,
        SwingCTpMode tpMode = SwingCTpMode.UpdateOnCConfirm,
        double minRewardRisk = 2.0,
        bool skipIfSwingCRrBelowBase = false,
        SwingCEdgeCrossTfContext? crossTf = null)
    {
        var ctx = setup.Context;
        var swingBBar = R1R2M5Structure.ResolveMonitorSwingBBar(in ctx);

        if (!wantsLiveTpUpdate || !useSwingC)
            return No(setup, "disabled");
        if (!ruleSlots.Contains(ctx.RuleSlot))
            return No(setup, "mask-not-applied");
        if (setup.TpMovedToEntryOnBBroken)
            return No(setup, "B-broken-tp-locked");
        if (setup.TpUpdatedToSwingC)
            return No(setup, "already-updated");

        var swingC = SwingCEdgeTakeProfitResolver.TryResolve(
            chartState, ctx.IsBuy, swingBBar, ctx.EntryPrice, out var reason, tpMode, crossTf);
        if (swingC is null)
            return No(setup, reason);

        if (skipIfSwingCRrBelowBase)
        {
            var cEdge = SwingCEdgeTakeProfitResolver.TryResolveC(
                chartState, ctx.IsBuy, swingBBar, ctx.EntryPrice, out var cReason, crossTf);
            if (cEdge is not null
                && SwingCEdgeTakeProfitResolver.IsEdgeRrBelowBase(
                    cEdge.TakeProfit, ctx.EntryPrice, ctx.StopLoss, ctx.IsBuy, minRewardRisk))
            {
                var rrEdge = SwingCEdgeTakeProfitResolver.ComputeEdgeRr(
                    cEdge.TakeProfit, ctx.EntryPrice, ctx.StopLoss, ctx.IsBuy);
                return Cancel(setup,
                    $"Swing-C RR {rrEdge:0.##} < base {minRewardRisk:0.##} ({cReason})");
            }
        }

        var skipFloor = skipIfSwingCRrBelowBase && tpMode != SwingCTpMode.NextKeyLevelAfterC;
        if (!SwingCEdgeTakeProfitResolver.TryResolveSwingEdgeTp(
                swingC.TakeProfit, ctx.EntryPrice, ctx.StopLoss, ctx.IsBuy, minRewardRisk, skipFloor,
                out var newTp, out var reject))
            return No(setup, reject ?? "Swing-C TP rejected");

        if (tpMode != SwingCTpMode.NoTpUntilC
            && IsSamePrice(setup.CurrentTakeProfit, newTp, tickSize))
            return No(setup, "already-at-c-edge");

        return new SwingCTpUpdateAction
        {
            ShouldUpdate = true,
            OldTakeProfit = setup.CurrentTakeProfit,
            NewTakeProfit = newTp,
            SwingC = swingC,
            Reason = "ok",
        };
    }

    static SwingCTpUpdateAction Cancel(TrackedSetup setup, string reason) => new()
    {
        ShouldCancel = true,
        OldTakeProfit = setup.CurrentTakeProfit,
        NewTakeProfit = setup.CurrentTakeProfit,
        Reason = reason,
    };

    static SwingCTpUpdateAction No(TrackedSetup setup, string reason) => new()
    {
        ShouldUpdate = false,
        OldTakeProfit = setup.CurrentTakeProfit,
        NewTakeProfit = setup.CurrentTakeProfit,
        Reason = reason,
    };

    static bool IsSamePrice(double a, double b, double tickSize)
    {
        var tol = tickSize > 0 ? tickSize / 2.0 : 1e-9;
        return Math.Abs(a - b) <= tol;
    }

    public static string FormatUpdateLog(
        string label,
        bool isBuy,
        in SwingCTpUpdateAction action,
        bool placedOnPending,
        bool success)
    {
        var c = action.SwingC!;
        var dir = isBuy ? "BUY" : "SELL";
        var cType = c.PivotType == SwingBResolver.TypeHigh ? "HIGH" : "LOW";
        var target = placedOnPending ? "pending" : "open";
        return $"[L6BT] TP Swing-C UPDATE label={label} dir={dir} target={target} " +
               $"Cbar={c.PivotBar} Ctype={cType} edge={c.EdgeKind} " +
               $"CedgeTop={Fmt(c.EdgeTop)} CedgeBottom={Fmt(c.EdgeBottom)} " +
               $"oldTP={Fmt(action.OldTakeProfit)} newTP={Fmt(action.NewTakeProfit)} " +
               $"result={(success ? "ok" : "rejected")} source=SwingCEdge";
    }

    public static string FormatCancelLog(string label, in SwingCTpUpdateAction action) =>
        $"[L6BT] TP Swing-C CANCEL label={label} reason={action.Reason}";

    public static string FormatUpdateRejectLog(string label, in SwingCTpUpdateAction action, string error) =>
        $"[L6BT] TP Swing-C UPDATE REJECT label={label} newTP={Fmt(action.NewTakeProfit)} reason={error}";

    static string Fmt(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
}
