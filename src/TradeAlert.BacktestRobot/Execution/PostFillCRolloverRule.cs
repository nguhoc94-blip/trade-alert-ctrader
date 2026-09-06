using System;
using System.Collections.Generic;
using System.Globalization;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>Decision returned by <see cref="PostFillCRolloverRule.Evaluate"/>.</summary>
public sealed record PostFillCRolloverDecision
{
    public bool ShouldRollover { get; init; }
    public bool TriggerReached { get; init; }
    /// <summary>The freshly-formed M15 obstacle chosen as the new C target (null when none).</summary>
    public ZoneCandidate? NewCZone { get; init; }
    public string Reason { get; init; } = "";
}

/// <summary>
/// Post-fill C-rollover (SplitTpAtCAndD only). After a split leg has filled and price has reached
/// entry + 0.25R profit (BUY) / entry − 0.25R (SELL), scan M15 for a freshly-formed same-color zone
/// that sits fully between the entry and the near edge of the original C zone. If found, the trade
/// should be closed and re-entered with the new obstacle as C and the old C as D.
///
/// BUY: C is a confirmed HIGH (resistance, RED zone). A new RED M15 zone below C means a closer
///      resistance appeared after fill → the original C TP is now unlikely → roll the target down.
/// SELL: mirror with GREEN support zones above C.
///
/// "New" = the zone's pivot bar is strictly after the fill bar. Because the original C (and the old D,
/// which becomes the new D) both formed before the fill, this condition naturally excludes the trade's
/// own C/D zones — no separate self-identity guard is needed.
///
/// Fully-contained means the obstacle lies entirely inside <c>[entry .. C-near-edge]</c>:
///   BUY:  zone.Low ≥ entry  AND  zone.High ≤ cEdgeBottom
///   SELL: zone.High ≤ entry AND  zone.Low  ≥ cEdgeTop
/// When several candidates qualify, the one nearest the entry (the first obstacle price would hit) wins.
/// </summary>
public static class PostFillCRolloverRule
{
    public const string CloseReason = "post-fill-C-rollover";
    public const double TriggerProfitR = 0.25;

    /// <summary>BUY: entry + profitR × risk. SELL: entry − profitR × risk.</summary>
    public static double ComputeTriggerPrice(double entry, double sl, bool isBuy, double profitR = TriggerProfitR)
    {
        if (entry <= 0 || profitR <= 0)
            return 0;
        var risk = Math.Abs(entry - sl);
        if (risk <= 0)
            return 0;
        return isBuy ? entry + profitR * risk : entry - profitR * risk;
    }

    public static PostFillCRolloverDecision Evaluate(
        bool isBuy,
        double entry,
        double cEdgeTop,
        double cEdgeBottom,
        int fillBarIndex,
        double triggerPrice,
        double bid,
        double ask,
        IReadOnlyList<ZoneCandidate> m15Zones)
    {
        // Trigger: BUY when bid reaches entry + 0.25R; SELL when ask reaches entry − 0.25R.
        var triggerReached = triggerPrice > 0
            && (isBuy ? bid >= triggerPrice : ask <= triggerPrice);
        if (!triggerReached)
            return new PostFillCRolloverDecision { ShouldRollover = false, TriggerReached = false, Reason = "trigger-not-reached" };

        if (entry <= 0 || cEdgeTop <= 0 || cEdgeBottom <= 0 || cEdgeTop <= cEdgeBottom)
            return new PostFillCRolloverDecision { ShouldRollover = false, TriggerReached = true, Reason = "invalid-C-geometry" };

        var wantColor = isBuy ? ZoneEffectiveColor.Red : ZoneEffectiveColor.Green;

        ZoneCandidate? best = null;
        double bestNearEdge = 0;
        foreach (var z in m15Zones)
        {
            if (z.EffectiveColor != wantColor)
                continue;

            // Must be a NEW zone (formed strictly after the fill bar). This also excludes the trade's
            // own C/D zones, which always predate the fill.
            if (z.PivotBar is not { } pb || pb <= fillBarIndex)
                continue;

            var contained = isBuy
                ? (z.Low >= entry && z.High <= cEdgeBottom)
                : (z.High <= entry && z.Low >= cEdgeTop);
            if (!contained)
                continue;

            var nearEdge = isBuy ? z.Low : z.High;
            if (best is null || (isBuy ? nearEdge < bestNearEdge : nearEdge > bestNearEdge))
            {
                best = z;
                bestNearEdge = nearEdge;
            }
        }

        if (best is null)
            return new PostFillCRolloverDecision { ShouldRollover = false, TriggerReached = true, Reason = "no-new-zone-below-C" };

        return new PostFillCRolloverDecision
        {
            ShouldRollover = true,
            TriggerReached = true,
            NewCZone = best,
            Reason = $"rollover: new {wantColor} M15 [{Fmt(best.Low)}..{Fmt(best.High)}] in (entry={Fmt(entry)}, C=[{Fmt(cEdgeBottom)}..{Fmt(cEdgeTop)}])",
        };
    }

    static string Fmt(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

    public static string FormatFillBarLog(string label, int bar) =>
        $"[L6BT] ROLLOVER FILL-BAR label={label} bar={bar}";

    public static string FormatTriggerReachedLog(
        string label,
        bool isBuy,
        double entry,
        double sl,
        double trigger,
        double bid,
        double ask,
        int fillBar,
        double cEdgeTop,
        double cEdgeBottom) =>
        $"[L6BT] ROLLOVER 0.25R-REACHED label={label} dir={(isBuy ? "BUY" : "SELL")} " +
        $"entry={Fmt(entry)} sl={Fmt(sl)} trigger={Fmt(trigger)} profitR={TriggerProfitR:0.##} " +
        $"probe={(isBuy ? Fmt(bid) : Fmt(ask))} fillBar={fillBar} " +
        $"C=[{Fmt(cEdgeBottom)}..{Fmt(cEdgeTop)}]";

    public static string FormatSkipLog(string label, string reason, int zoneCount) =>
        $"[L6BT] ROLLOVER SKIP label={label} reason={reason} m15ZonesScanned={zoneCount}";

    public static string FormatCloseLegLog(string label) =>
        $"[L6BT] ROLLOVER CLOSE-POSITION {label} ({CloseReason})";

    public static string FormatCancelLegLog(string label) =>
        $"[L6BT] ROLLOVER CANCEL-PENDING {label} ({CloseReason})";

    public static string FormatReenterSkipLog(string label, string mapperReason) =>
        $"[L6BT] ROLLOVER RE-ENTER SKIP label={label} mapperReason={mapperReason}";

    public static string FormatReenterPlanLog(string label, string legTag, double entry, double sl, double tp) =>
        $"[L6BT] ROLLOVER RE-ENTER PLAN {label}|{legTag} entry={Fmt(entry)} sl={Fmt(sl)} tp={Fmt(tp)}";
}
