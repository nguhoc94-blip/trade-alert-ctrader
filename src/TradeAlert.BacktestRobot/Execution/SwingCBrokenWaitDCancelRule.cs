using System.Collections.Generic;
using System.Globalization;
using TradeAlert.Core.Engines;
using TradeAlert.Indicator;

namespace TradeAlert.BacktestRobot.Execution;

public sealed class SwingCBrokenWaitDCancelConfig
{
    public HashSet<int> RuleSlotIndices { get; init; } = new();

    public bool AppliesToSlot(int slotIndex) => RuleSlotIndices.Contains(slotIndex);
}

public sealed class SwingCBrokenWaitDCancelResult
{
    public bool ShouldCancel { get; init; }
    public int SwingCPivotBar { get; init; }
    public int SwingCFlag { get; init; }
    public int SwingCFirstDIdx { get; init; }
}

/// <summary>
/// Pending invalidation: cancel limit orders when the plan's swing C (recorded at entry)
/// transitions to BROKEN + waiting for D (flag=2, firstDIdx=FirstDWaiting).
/// Applies when swing C (see <see cref="PendingOrderContext.SwingCEntryPivotBar"/> on split |D,
/// else <see cref="PendingOrderContext.SwingCPivotBar"/>) transitions to BROKEN + waiting for D.
/// </summary>
public static class SwingCBrokenWaitDCancelRule
{
    public const string CancelReason = "C-broken-wait-D";

    public static int ResolveSwingCEntryPivotBar(in PendingOrderContext ctx) =>
        ctx.SwingCEntryPivotBar > 0 ? ctx.SwingCEntryPivotBar : ctx.SwingCPivotBar;

    public static SwingCBrokenWaitDCancelResult Evaluate(PineStateEngine chartState, in PendingOrderContext ctx)
    {
        var cEntryBar = ResolveSwingCEntryPivotBar(in ctx);
        if (cEntryBar <= 0)
            return NoCancel();

        var idx = SwingBrokenChecker.FindByBar(chartState, cEntryBar);
        if (idx < 0)
            return NoCancel();

        var pivots = chartState.Pivots;
        if (!SwingCEdgeTakeProfitResolver.IsBrokenWaitingD(pivots, idx))
            return NoCancel();

        return new SwingCBrokenWaitDCancelResult
        {
            ShouldCancel = true,
            SwingCPivotBar = cEntryBar,
            SwingCFlag = pivots.GetFlag(idx),
            SwingCFirstDIdx = pivots.GetFirstDIdx(idx),
        };
    }

    public static SwingCBrokenWaitDCancelResult Evaluate(in PendingOrderContext ctx, PerSymbolSignalHost host)
    {
        if (!host.TryGetState(ctx.SwingBTfToken, out var state))
            state = host.State;
        return Evaluate(state, in ctx);
    }

    static SwingCBrokenWaitDCancelResult NoCancel() => new() { ShouldCancel = false };

    public static string FormatCancelLog(in PendingOrderContext ctx, in SwingCBrokenWaitDCancelResult r) =>
        $"[L6BT] CANCEL {ctx.Label} ({CancelReason}) " +
        $"Cbar={r.SwingCPivotBar} flag={r.SwingCFlag} firstDIdx={r.SwingCFirstDIdx} " +
        $"dir={(ctx.IsBuy ? "BUY" : "SELL")} R{ctx.RuleSlot + 1}";

    public static string FormatPairCancelLog(string triggerLabel, string siblingLabel) =>
        $"[L6BT] C-broken-wait-D PAIR-CANCEL trigger={triggerLabel} -> CANCEL {siblingLabel} ({CancelReason})";
}
