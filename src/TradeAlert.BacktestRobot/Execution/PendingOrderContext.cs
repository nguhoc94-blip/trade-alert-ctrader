using TradeAlert.Core.Models.Alerts;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>
/// Context stored for each live pending order, used by pending invalidation rules
/// (post-B push obstacle cancel, etc.). Positions that have filled retain this in the book
/// until closed, but invalidation rules must only act on still-pending orders.
/// </summary>
public sealed class PendingOrderContext
{
    public string Label { get; init; } = "";
    public int RuleSlot { get; init; }
    public SignalDirection Direction { get; init; }
    public bool IsBuy { get; init; }
    public int SwingBPivotBar { get; init; }
    public int SwingBPivotIndex { get; init; }
    public string SwingBTfToken { get; init; } = "15";
    public bool UsesM5SwingC { get; init; }
    public bool SwingBFromM5Anchor { get; init; }
    public int StructureSwingBBar { get; init; }
    public double EntryPrice { get; init; }
    public double StopLoss { get; init; }
    public double OriginalTakeProfit { get; init; }
    public double SwingBKeyLow { get; init; }
    public double SwingBKeyHigh { get; init; }
    /// <summary>True when the entry keylevel is an OrderBlock (near-B gate uses buffer h/8); false = KeyLevel (h/4).</summary>
    public bool SwingBKeyIsOb { get; init; }
    /// <summary>TP price of leg C. Set on both C and D legs; 0 = not a split plan (gate skipped).</summary>
    public double TpCLegPrice { get; init; }
    public int SwingCPivotBar { get; init; }
    public int SwingCEntryPivotBar { get; init; }
    public int SwingCPivotIndex { get; init; }
    /// <summary>Top edge of the swing-C zone (this leg's C/D zone). Used by the post-fill C-rollover rule
    /// to bound the "[entry .. C near-edge]" range when scanning for a new same-color obstacle below C.</summary>
    public double SwingCEdgeTop { get; init; }
    /// <summary>Bottom edge of the swing-C zone. See <see cref="SwingCEdgeTop"/>.</summary>
    public double SwingCEdgeBottom { get; init; }
    public TakeProfitSource TakeProfitSource { get; init; }
    public TradeDedupKey DedupKey { get; init; }

    /// <summary>Near-D cancel zone (high). 0 means gate disabled / no zone available.</summary>
    public double NearDCancelZoneHigh { get; init; }
    /// <summary>Near-D cancel zone (low). 0 means gate disabled / no zone available.</summary>
    public double NearDCancelZoneLow { get; init; }
    /// <summary>True when the near-D cancel zone is an OrderBlock (buffer h/16); false for KeyLevel (buffer h/8).</summary>
    public bool NearDCancelZoneIsOb { get; init; }
    /// <summary>TF token of the near-D cancel zone (for logs).</summary>
    public string NearDCancelZoneTfToken { get; init; } = "";

    /// <summary>Near-B→TpC touch zone. 0 = not set (gate falls back to swing-B keylevel + entry).</summary>
    public double NearBCancelZoneLow { get; init; }
    public double NearBCancelZoneHigh { get; init; }
    public bool NearBCancelZoneIsOb { get; init; }

    /// <summary>Chart bar index at which the compound fire event placed this order (for debug chart labels).</summary>
    public int FireChartBarIndex { get; init; } = -1;

    /// <summary>Alias for <see cref="EntryPrice"/> (legacy field name).</summary>
    public double Entry => EntryPrice;

    public static PendingOrderContext FromPlan(TradePlan plan, int ruleSlot, int fireChartBarIndex = -1) => new()
    {
        Label = plan.Label,
        RuleSlot = ruleSlot,
        Direction = plan.Direction,
        IsBuy = plan.IsBuy,
        SwingBPivotBar = plan.SwingBPivotBar,
        SwingBPivotIndex = plan.SwingBPivotIndex,
        SwingBTfToken = plan.SwingBTfToken,
        UsesM5SwingC = plan.UsesM5SwingC,
        SwingBFromM5Anchor = plan.SwingBFromM5Anchor,
        StructureSwingBBar = plan.StructureSwingBBar,
        EntryPrice = plan.EntryLimit,
        StopLoss = plan.StopLoss,
        OriginalTakeProfit = plan.OriginalTakeProfit > 0 ? plan.OriginalTakeProfit : plan.TakeProfit,
        SwingBKeyLow = plan.SwingBKeyLow,
        SwingBKeyHigh = plan.SwingBKeyHigh,
        SwingBKeyIsOb = plan.SwingBKeyIsOb,
        TpCLegPrice = plan.TpCLegPrice,
        SwingCPivotBar = plan.SwingCPivotBar,
        SwingCEntryPivotBar = plan.SwingCEntryPivotBar,
        SwingCPivotIndex = plan.SwingCPivotIndex,
        SwingCEdgeTop = plan.SwingCEdgeTop,
        SwingCEdgeBottom = plan.SwingCEdgeBottom,
        TakeProfitSource = plan.TakeProfitSource,
        DedupKey = plan.DedupKey,
        NearDCancelZoneHigh = plan.NearDCancelZoneHigh,
        NearDCancelZoneLow = plan.NearDCancelZoneLow,
        NearDCancelZoneIsOb = plan.NearDCancelZoneIsOb,
        NearDCancelZoneTfToken = plan.NearDCancelZoneTfToken,
        NearBCancelZoneLow = plan.NearBCancelZoneLow,
        NearBCancelZoneHigh = plan.NearBCancelZoneHigh,
        NearBCancelZoneIsOb = plan.NearBCancelZoneIsOb,
        FireChartBarIndex = fireChartBarIndex,
    };
}
