using System;
using System.Collections.Generic;
using System.Globalization;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>
/// Configuration for the near-D pending-cancel rule. The rule cancels limit pending orders that
/// were planned via <see cref="SwingCTpMode.SplitTpAtCAndD"/> when the live bid/ask reaches the
/// recorded near-D zone with the alert-touch buffer applied. Cancels are applied as a pair: when
/// any leg of a split (C and D share the same swing-B pivot bar) triggers, both legs are cancelled.
/// </summary>
public sealed class NearDZoneCancelConfig
{
    /// <summary>0-based slot indices that participate in the cancel evaluation.</summary>
    public HashSet<int> RuleSlotIndices { get; init; } = new();

    public bool AppliesToSlot(int slotIndex) => RuleSlotIndices.Contains(slotIndex);
}

/// <summary>
/// Result of evaluating <see cref="NearDZoneCancelRule.Evaluate"/>.
/// </summary>
public sealed class NearDZoneCancelResult
{
    public bool ShouldCancel { get; init; }
    /// <summary>Probe price (Bid for SELL, Ask for BUY) used for the touch check.</summary>
    public double ProbePrice { get; init; }
    public double ZoneLow { get; init; }
    public double ZoneHigh { get; init; }
    public double Buffer { get; init; }
    public bool IsOb { get; init; }
    public string TfToken { get; init; } = "";
}

/// <summary>
/// Pending invalidation: cancel a SplitTpAtCAndD limit order when the live tick price reaches the
/// "near-D" zone (recorded at plan time) with the alert-touch buffer applied. Mirrors the buffered
/// touch math used by <c>TouchRealEvaluators</c>:<br/>
///   buffer = height / (8 × (isOb ? 2 : 1))<br/>
/// Touch (BUY): probe + buffer ≥ zoneBot  (i.e., probe ≥ zoneBot − buffer).<br/>
/// Touch (SELL): probe − buffer ≤ zoneTop (i.e., probe ≤ zoneTop + buffer).<br/>
/// </summary>
public static class NearDZoneCancelRule
{
    public const string CancelReason = "near-D-zone";

    /// <summary>
    /// Evaluate whether the live probe price has reached the near-D zone (with alert-touch buffer).
    /// Returns <see cref="NearDZoneCancelResult.ShouldCancel"/> = false when the context has no
    /// near-D zone set (gate disabled / no zone available at plan time).
    /// </summary>
    public static NearDZoneCancelResult Evaluate(in PendingOrderContext ctx, double bid, double ask) =>
        EvaluateWithProbe(in ctx, ctx.IsBuy ? ask : bid);

    /// <summary>
    /// Evaluate touch against an explicit probe price. Callers choose the probe source:
    /// realtime uses Ask (BUY) / Bid (SELL); backtest bar-close uses the closed bar's
    /// High (BUY) / Low (SELL) so intrabar touches invalidate the pending as they would live.
    /// </summary>
    public static NearDZoneCancelResult EvaluateWithProbe(in PendingOrderContext ctx, double probe)
    {
        var top = ctx.NearDCancelZoneHigh;
        var bot = ctx.NearDCancelZoneLow;
        if (top <= 0 || bot <= 0 || top <= bot)
            return new NearDZoneCancelResult { ShouldCancel = false };

        if (probe <= 0)
            return new NearDZoneCancelResult { ShouldCancel = false };

        var buffer = ZoneTouchBuffer(top, bot, ctx.NearDCancelZoneIsOb);
        bool touched = ctx.IsBuy
            ? (probe + buffer >= bot)
            : (probe - buffer <= top);

        return new NearDZoneCancelResult
        {
            ShouldCancel = touched,
            ProbePrice = probe,
            ZoneLow = bot,
            ZoneHigh = top,
            Buffer = buffer,
            IsOb = ctx.NearDCancelZoneIsOb,
            TfToken = ctx.NearDCancelZoneTfToken,
        };
    }

    /// <summary>Alert-touch buffer. Key zones: height/8. OB zones: height/16.</summary>
    public static double ZoneTouchBuffer(double top, double bot, bool isOb)
    {
        var height = top - bot;
        if (height <= 0) return 0;
        return height / (8.0 * (isOb ? 2 : 1));
    }

    /// <summary>Price band where pending near-D cancel triggers (touch edge + buffer).</summary>
    public static (double BandLow, double BandHigh) BufferTouchBand(
        double zoneLow,
        double zoneHigh,
        bool isBuy,
        bool isOb)
    {
        var buffer = ZoneTouchBuffer(zoneHigh, zoneLow, isOb);
        if (isBuy)
            return (zoneLow - buffer, zoneLow);
        return (zoneHigh, zoneHigh + buffer);
    }

    /// <summary>
    /// OnBar in cTrader backtest, <c>Bars.Count - 1</c> is the newly opened (forming) bar.
    /// Pending-cancel H/L probes must use the last <b>closed</b> bar (Count - 2), or
    /// <paramref name="hostLastClosedBar"/> from <c>PerSymbolSignalHost.Tick()</c> when set.
    /// </summary>
    public static int BacktestLastClosedBarIndex(int barsCount, int hostLastClosedBar = -1)
    {
        if (hostLastClosedBar >= 0)
            return hostLastClosedBar;
        if (barsCount <= 0)
            return -1;
        return barsCount >= 2 ? barsCount - 2 : 0;
    }

    public static string FormatCancelLog(in PendingOrderContext ctx, in NearDZoneCancelResult r)
    {
        var dir = ctx.IsBuy ? "BUY" : "SELL";
        var srcKind = r.IsOb ? "OB" : "Key";
        var tf = string.IsNullOrEmpty(r.TfToken) ? "?" : FormatTfLabel(r.TfToken);
        return $"[L6BT] CANCEL {ctx.Label} ({CancelReason}) dir={dir} probe={Fmt(r.ProbePrice)} " +
               $"zone=[{Fmt(r.ZoneLow)}..{Fmt(r.ZoneHigh)}] buffer={Fmt(r.Buffer)} src={tf}/{srcKind}";
    }

    static string FormatTfLabel(string tfToken) => tfToken switch
    {
        "240"  => "H4",
        "60"   => "H1",
        "15"   => "M15",
        "5"    => "M5",
        "1440" => "D1",
        "1D"   => "D1",
        "D"    => "D1",
        _      => tfToken,
    };

    static string Fmt(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
}
