using TradeAlert.Core.Models.Alerts;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>
/// A fully-resolved trade setup derived from a <see cref="CompoundFireEvent"/> plus swing/keylevel/OB
/// geometry. Prices are spread-adjusted (Bid-chart, KlEntryLot parity). Pure data — no cAlgo types so
/// it stays unit-testable. The cBot converts <see cref="LotFtmo"/> to volume-in-units before placing.
/// </summary>
public sealed class TradePlan
{
    public int SlotIndex { get; init; }
    public string RuleName { get; init; } = "";
    public SignalDirection Direction { get; init; }
    public bool IsBuy { get; init; }

    /// <summary>Pending limit entry price (spread-adjusted).</summary>
    public double EntryLimit { get; init; }
    public double StopLoss { get; init; }
    public double TakeProfit { get; init; }

    /// <summary>Distance entry→SL in cTrader pips.</summary>
    public double StopLossPips { get; init; }
    /// <summary>Distance entry→TP in cTrader pips.</summary>
    public double TakeProfitPips { get; init; }

    /// <summary>KlEntryLot FTMO lot (cTrader quantity). Null when sizing could not be computed.</summary>
    public double? LotFtmo { get; init; }

    public TradeDedupKey DedupKey { get; init; }
    public int SwingBPivotIndex { get; init; }
    public int SwingBPivotBar { get; init; }
    /// <summary>Chart TF token where swing B was resolved (e.g. "15" for M15).</summary>
    public string SwingBTfToken { get; init; } = "15";
    /// <summary>R1/R2 on M15: swing C (and live TP monitor) resolves on M5.</summary>
    public bool UsesM5SwingC { get; init; }
    /// <summary>True when swing B came from M5 anchored fallback (B-broken monitor uses M5 bar).</summary>
    public bool SwingBFromM5Anchor { get; init; }
    /// <summary>M5 bar for C resolution / monitor when <see cref="UsesM5SwingC"/>; native M5 B bar when <see cref="SwingBFromM5Anchor"/>.</summary>
    public int StructureSwingBBar { get; init; }
    public double SwingBKeyLow { get; init; }
    public double SwingBKeyHigh { get; init; }
    /// <summary>True when the entry keylevel came from an OrderBlock (near-B buffer = h/8); false = KeyLevel (buffer = h/4).</summary>
    public bool SwingBKeyIsOb { get; init; }

    /// <summary>
    /// TP price of the C leg. Set on both legs of a SplitTpAtCAndD plan.
    /// On leg C this equals <see cref="TakeProfit"/>; on leg D this is the C-leg TP.
    /// 0 when not a split plan (gate skipped).
    /// </summary>
    public double TpCLegPrice { get; init; }

    public int SwingCPivotIndex { get; init; }
    public int SwingCPivotBar { get; init; }
    /// <summary>
    /// Swing C pivot bar for C-broken-wait-D invalidation. On split |D leg this is the entry C bar
    /// (same as |C); on single-leg plans equals <see cref="SwingCPivotBar"/>.
    /// </summary>
    public int SwingCEntryPivotBar { get; init; }
    public double SwingCEdgeTop { get; init; }
    public double SwingCEdgeBottom { get; init; }

    public TakeProfitSource TakeProfitSource { get; init; } = TakeProfitSource.RewardRisk;
    public double OriginalTakeProfit { get; init; }

    public double KeyWidth { get; init; }
    public double DynamicSlWidthMult { get; init; }
    public double SlBaseMult { get; init; }
    public double SlAdjustFactor { get; init; }
    public double SlRiskDistance { get; init; }
    public AtrSlWidthAdjustResult? SlAdjust { get; init; }

    /// <summary>Spread (pips) used when this plan was built.</summary>
    public double SpreadPipsAtPlan { get; init; }
    /// <summary><see cref="SpreadTelemetrySources.Override"/> or <see cref="SpreadTelemetrySources.Symbol"/>.</summary>
    public string SpreadSourceAtPlan { get; init; } = "";
    /// <summary>Min-SL floor in pips (spread×mult or forex abs) when constraint is enabled.</summary>
    public double MinSlFloorPips { get; init; }
    /// <summary>SL distance in pips before any min-SL floor clamp.</summary>
    public double SlPipsBeforeFloor { get; init; }
    public bool SlFloorApplied { get; init; }

    /// <summary>Bid-chart levels before spread forward adjustment (logging only).</summary>
    public double RawEntryBid { get; init; }
    public double RawSlBid { get; init; }
    public double RawTpBid { get; init; }
    public double EntryShiftBySpread { get; init; }
    public double SlShiftBySpread { get; init; }
    public double TpShiftBySpread { get; init; }

    public string Label { get; init; } = "";

    /// <summary>Split-TP leg tag: <c>C</c>, <c>D</c>, or empty for single-leg plans.</summary>
    public string TpLegTag { get; init; } = "";

    /// <summary>
    /// Near-D cancel zone (high). Set on both legs of a SplitTpAtCAndD plan when D is found in
    /// M5/M15/H1/H4 (or as gate-only fallback in the daily TF). 0 means "no near-D zone available
    /// — gate skipped". Used by the near-D pending-cancel rule to invalidate the limit when price
    /// reaches the D zone before fill.
    /// </summary>
    public double NearDCancelZoneHigh { get; init; }
    /// <summary>Near-D cancel zone (low). See <see cref="NearDCancelZoneHigh"/>.</summary>
    public double NearDCancelZoneLow { get; init; }
    /// <summary>True when the near-D cancel zone came from an OrderBlock (buffer divisor 16); false for KeyLevel (buffer divisor 8).</summary>
    public bool NearDCancelZoneIsOb { get; init; }
    /// <summary>TF token of the near-D cancel zone (for logs). Empty when no zone set.</summary>
    public string NearDCancelZoneTfToken { get; init; } = "";

    /// <summary>Near-B→TpC touch zone low from the winning entry candidate (Key box or OB box).</summary>
    public double NearBCancelZoneLow { get; init; }
    /// <summary>Near-B→TpC touch zone high from the winning entry candidate.</summary>
    public double NearBCancelZoneHigh { get; init; }
    /// <summary>True when the winning candidate was an OB (buffer h/8); false for KeyLevel (h/4).</summary>
    public bool NearBCancelZoneIsOb { get; init; }

    /// <summary>Human-readable explanation of how entry/SL/TP were derived (for logs).</summary>
    public string Reason { get; init; } = "";
}
