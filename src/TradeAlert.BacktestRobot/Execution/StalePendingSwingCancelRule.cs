using System.Collections.Generic;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.KeyLevel;
using TradeAlert.Indicator;

namespace TradeAlert.BacktestRobot.Execution;

public sealed class StalePendingSwingCancelConfig
{
    public HashSet<int> RuleSlotIndices { get; init; } = new();
    public int ActiveSwingThreshold { get; init; } = 10;

    public bool AppliesToSlot(int slotIndex) => RuleSlotIndices.Contains(slotIndex);
}

public enum StalePendingSwingCancelTrigger
{
    None = 0,
    TooManyNewSwings = 1,
}

public sealed class StalePendingSwingCancelResult
{
    public bool ShouldCancel { get; init; }
    public StalePendingSwingCancelTrigger Trigger { get; init; }
    public int NewSwingCount { get; init; }
    public int FireChartBarIndex { get; init; }
}

/// <summary>
/// Cancel stale pending limits when the market has moved sideways too long since the fire bar:
/// ≥ <see cref="StalePendingSwingCancelConfig.ActiveSwingThreshold"/> new ACTIVE swings (flag=1, bar &gt; fire bar).
/// </summary>
public static class StalePendingSwingCancelRule
{
    public const string CancelReason = "stale-pending-swings";

    public static StalePendingSwingCancelResult Evaluate(
        PineStateEngine chartState,
        in PendingOrderContext ctx,
        in StalePendingSwingCancelConfig cfg)
    {
        if (ctx.FireChartBarIndex < 0)
            return NoCancel();

        var activeSwings = CountActiveSinceFire(chartState.Pivots, ctx.FireChartBarIndex);

        if (activeSwings >= cfg.ActiveSwingThreshold)
        {
            return Cancel(
                StalePendingSwingCancelTrigger.TooManyNewSwings,
                ctx.FireChartBarIndex,
                activeSwings);
        }

        return NoCancel(ctx.FireChartBarIndex, activeSwings);
    }

    public static StalePendingSwingCancelResult Evaluate(
        in PendingOrderContext ctx,
        PerSymbolSignalHost host,
        in StalePendingSwingCancelConfig cfg)
    {
        if (!host.TryGetState(ctx.SwingBTfToken, out var state))
            state = host.State;
        return Evaluate(state, in ctx, in cfg);
    }

    public static int CountActiveSinceFire(PivotStateStore pivots, int fireChartBarIndex)
    {
        var activeSwings = 0;

        for (var i = 0; i < pivots.Count; i++)
        {
            var bar = pivots.GetSnapshot(i).BarIndex;
            if (bar <= fireChartBarIndex)
                continue;

            if (pivots.GetFlag(i) == SwingBResolver.FlagActive)
                activeSwings++;
        }

        return activeSwings;
    }

    static StalePendingSwingCancelResult Cancel(
        StalePendingSwingCancelTrigger trigger,
        int fireBar,
        int activeSwings) => new()
    {
        ShouldCancel = true,
        Trigger = trigger,
        FireChartBarIndex = fireBar,
        NewSwingCount = activeSwings,
    };

    static StalePendingSwingCancelResult NoCancel(int fireBar = -1, int activeSwings = 0) => new()
    {
        ShouldCancel = false,
        FireChartBarIndex = fireBar,
        NewSwingCount = activeSwings,
    };

    public static string FormatCancelLog(in PendingOrderContext ctx, in StalePendingSwingCancelResult r)
    {
        var trigger = r.Trigger switch
        {
            StalePendingSwingCancelTrigger.TooManyNewSwings => $"activeSwings>={r.NewSwingCount}",
            _ => "?",
        };

        return
            $"[L6BT] CANCEL {ctx.Label} ({CancelReason}) fireBar={r.FireChartBarIndex} " +
            $"activeSwings={r.NewSwingCount} trigger={trigger} " +
            $"dir={(ctx.IsBuy ? "BUY" : "SELL")} R{ctx.RuleSlot + 1}";
    }

    public static string FormatTriggerLabel(StalePendingSwingCancelTrigger trigger) =>
        trigger switch
        {
            StalePendingSwingCancelTrigger.TooManyNewSwings => "10+ ACTIVE swings",
            _ => "",
        };
}
