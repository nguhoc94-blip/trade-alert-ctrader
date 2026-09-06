using System.Globalization;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>
/// Configuration for the near-B-then-C-TP pending-cancel rule.
/// </summary>
public sealed class NearBThenCTpCancelConfig
{
    /// <summary>0-based slot indices that participate in the cancel evaluation.</summary>
    public System.Collections.Generic.HashSet<int> RuleSlotIndices { get; init; } = new();

    public bool AppliesToSlot(int slotIndex) => RuleSlotIndices.Contains(slotIndex);
}

/// <summary>
/// Result returned by <see cref="NearBThenCTpCancelRule.EvaluateTouch"/> (step 1 — latch update)
/// and <see cref="NearBThenCTpCancelRule.EvaluateCancel"/> (step 2 — cancel decision).
/// </summary>
public sealed class NearBThenCTpCancelResult
{
    public bool ShouldCancel { get; init; }
    public bool TouchedB { get; init; }
    /// <summary>Probe price used for the touch / cancel check.</summary>
    public double ProbePrice { get; init; }
    public double BZoneHigh { get; init; }
    public double BZoneLow { get; init; }
    public double Buffer { get; init; }
    public bool BIsOb { get; init; }
    public double TpCPrice { get; init; }
}

/// <summary>
/// Pending invalidation for SplitTpAtCAndD limit orders: cancel when the live price has
/// first touched the near-B zone (entry keylevel with alert-touch buffer) and later the bid/ask
/// reaches TP C without the limit having filled — meaning the full B→C move occurred unfilled.
///
/// Buffer formula (different from near-D):
///   Key zone: height / 4
///   OB zone:  height / 8
///
/// Touch B (BUY):  ask ≤ bTop  AND  ask + buffer ≥ bBot
/// Touch B (SELL): bid ≥ bBot  AND  bid − buffer ≤ bTop
///
/// Hit TP C (BUY):  bid ≥ TpCLegPrice   (no buffer — exact price level)
/// Hit TP C (SELL): ask ≤ TpCLegPrice
/// </summary>
public static class NearBThenCTpCancelRule
{
    public const string CancelReason = "near-B-toward-TpC";

    // ── Touch B (latch step) ──────────────────────────────────────────────

    /// <summary>
    /// Winning entry candidate zone from plan time: KeyLevel B box (h/4) or selected OB box (h/8).
    /// Falls back to raw swing-B keylevel when near-B fields are unset.
    /// </summary>
    public static (double Low, double High, bool IsOb) ResolveNearBZone(in PendingOrderContext ctx)
    {
        var low = ctx.NearBCancelZoneLow;
        var high = ctx.NearBCancelZoneHigh;
        if (low > 0 && high > low)
            return (low, high, ctx.NearBCancelZoneIsOb);

        low = ctx.SwingBKeyLow;
        high = ctx.SwingBKeyHigh;
        if (low > 0 && high > low)
            return (low, high, ctx.SwingBKeyIsOb);

        return (0, 0, false);
    }

    /// <summary>
    /// Evaluate whether the probe price touches the near-B zone (with alert-touch buffer).
    /// Call this every tick/bar to update the latch stored in <see cref="TrackedSetup.NearBZoneTouched"/>.
    /// Returns <see cref="NearBThenCTpCancelResult.TouchedB"/> = false when B geometry is unavailable
    /// or <see cref="PendingOrderContext.TpCLegPrice"/> is 0 (not a split plan).
    /// </summary>

    public static NearBThenCTpCancelResult EvaluateTouch(in PendingOrderContext ctx, double bid, double ask) =>
        EvaluateTouchWithProbe(in ctx, ctx.IsBuy ? ask : bid);

    /// <summary>
    /// Touch near-B with an explicit probe. Realtime: Ask (BUY) / Bid (SELL).
    /// Backtest bar-close: Low (BUY) / High (SELL).
    /// </summary>
    public static NearBThenCTpCancelResult EvaluateTouchWithProbe(in PendingOrderContext ctx, double probe)
    {
        var (bot, top, isOb) = ResolveNearBZone(in ctx);
        if (top <= 0 || bot <= 0 || top <= bot || ctx.TpCLegPrice <= 0)
            return new NearBThenCTpCancelResult { TouchedB = false };

        if (probe <= 0)
            return new NearBThenCTpCancelResult { TouchedB = false };

        var buffer = ZoneTouchBuffer(top, bot, isOb);
        bool touched = ctx.IsBuy
            ? (probe <= top && probe + buffer >= bot)
            : (probe >= bot && probe - buffer <= top);

        return new NearBThenCTpCancelResult
        {
            TouchedB = touched,
            ProbePrice = probe,
            BZoneHigh = top,
            BZoneLow = bot,
            Buffer = buffer,
            BIsOb = isOb,
            TpCPrice = ctx.TpCLegPrice,
        };
    }

    // ── Cancel step ───────────────────────────────────────────────────────

    /// <summary>
    /// Evaluate cancel condition: latch was set AND probe has now reached TP C (no buffer).
    /// </summary>
    public static NearBThenCTpCancelResult EvaluateCancel(in PendingOrderContext ctx, bool nearBTouched, double bid, double ask) =>
        EvaluateCancelWithProbe(in ctx, nearBTouched, ctx.IsBuy ? bid : ask);

    /// <summary>
    /// Cancel when latch set and probe reaches TP C. Realtime: Bid (BUY) / Ask (SELL).
    /// Backtest bar-close: High (BUY) / Low (SELL).
    /// </summary>
    public static NearBThenCTpCancelResult EvaluateCancelWithProbe(in PendingOrderContext ctx, bool nearBTouched, double probe)
    {
        if (!nearBTouched || ctx.TpCLegPrice <= 0)
            return new NearBThenCTpCancelResult { ShouldCancel = false };

        bool hitTpC = ctx.IsBuy
            ? probe >= ctx.TpCLegPrice
            : probe <= ctx.TpCLegPrice;

        var (bLow, bHigh, bIsOb) = ResolveNearBZone(in ctx);
        return new NearBThenCTpCancelResult
        {
            ShouldCancel = hitTpC,
            ProbePrice = probe,
            TpCPrice = ctx.TpCLegPrice,
            BZoneHigh = bHigh,
            BZoneLow = bLow,
            BIsOb = bIsOb,
        };
    }

    // ── Buffer ────────────────────────────────────────────────────────────

    /// <summary>Alert-touch buffer. Key zones: height/4. OB zones: height/8.</summary>
    public static double ZoneTouchBuffer(double top, double bot, bool isOb)
    {
        var height = top - bot;
        if (height <= 0) return 0;
        return height / (4.0 * (isOb ? 2.0 : 1.0));
    }

    // ── Logging ───────────────────────────────────────────────────────────

    public static string FormatCancelLog(in PendingOrderContext ctx, in NearBThenCTpCancelResult r)
    {
        var dir = ctx.IsBuy ? "BUY" : "SELL";
        var kind = r.BIsOb ? "OB" : "Key";
        return $"[L6BT] CANCEL {ctx.Label} ({CancelReason}) dir={dir} probe={Fmt(r.ProbePrice)} " +
               $"tpC={Fmt(r.TpCPrice)} bZone=[{Fmt(r.BZoneLow)}..{Fmt(r.BZoneHigh)}] buffer={Fmt(r.Buffer)} bKind={kind}";
    }

    static string Fmt(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
}
