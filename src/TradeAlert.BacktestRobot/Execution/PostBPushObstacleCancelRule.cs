using System;
using System.Collections.Generic;
using System.Globalization;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.KeyLevel;
using TradeAlert.Core.Series;
using TradeAlert.Indicator;

namespace TradeAlert.BacktestRobot.Execution;

public sealed class PostBPushSwingMatch
{
    public int PivotIndex { get; init; }
    public int PivotBar { get; init; }
    public string PushTypeLabel { get; init; } = "";
    public double PushLow { get; init; }
    public double PushHigh { get; init; }
    /// <summary>Open time of the chart-TF bar that holds the push pivot. Null when Series buffer is unavailable
    /// (cross-TF identity falls back to legacy bar_index match only).</summary>
    public DateTime? PivotOpenTime { get; init; }
    /// <summary>Bar duration on the push pivot's TF (chart TF). <see cref="TimeSpan.Zero"/> when TF unknown.</summary>
    public TimeSpan ChartTfPeriod { get; init; }
}

/// <summary>How the matched cancel zone relates to the push swing in TF coordinates.</summary>
public enum PostBPushCancelMatchKind
{
    None = 0,
    /// <summary>Zone TF token equals chart TF token (same TF as push).</summary>
    SameTf = 1,
    /// <summary>Zone TF differs from chart TF and is a genuine foreign obstacle (not push swing's own cross-TF
    /// representation).</summary>
    CrossTf = 2,
}

public sealed class PostBPushObstacleCancelResult
{
    public bool ShouldCancel { get; init; }
    public PostBPushSwingMatch? Push { get; init; }
    public string? MatchedTf { get; init; }
    public string? MatchedTfToken { get; init; }
    public ZoneSourceKind? MatchedSource { get; init; }
    public ZoneEffectiveColor? EffectiveColor { get; init; }
    public BrokenSwingType? BrokenSwing { get; init; }
    public double? ZoneLow { get; init; }
    public double? ZoneHigh { get; init; }
    public double OverlapPips { get; init; }
    public PostBPushCancelMatchKind MatchKind { get; init; }
    /// <summary>Number of candidate zones that were filtered out as the push swing's own cross-TF representation
    /// (time-containment + price-midpoint guard). Diagnostic only; non-zero means the new identity check did work.</summary>
    public int CrossTfSelfIdentitySkipped { get; init; }
}

/// <summary>
/// Pending invalidation: cancel when a confirmed post-B push swing overlaps an opposite-color
/// obstacle zone on any configured TF. BUY → post-B HIGH vs RED zones; SELL → post-B LOW vs GREEN.
/// </summary>
public static class PostBPushObstacleCancelRule
{
    public const string CancelReason = "post-B-push-obstacle";

    public static PostBPushObstacleCancelResult Evaluate(
        in PendingOrderContext ctx,
        PerSymbolSignalHost host,
        in PostBPushObstacleCancelRuleConfig cfg) =>
        EvaluateCore(in ctx, tf =>
        {
            if (host.TryGetState(tf, out var state))
            {
                host.TryGetSeriesBuffer(tf, out var buffer);
                return (state, buffer);
            }
            return (null, null);
        }, in cfg);

    /// <summary>Testable overload with explicit TF → state map.</summary>
    public static PostBPushObstacleCancelResult EvaluateWithStates(
        in PendingOrderContext ctx,
        PineStateEngine chartState,
        IReadOnlyDictionary<string, PineStateEngine> statesByTf,
        in PostBPushObstacleCancelRuleConfig cfg) =>
        EvaluateWithStates(in ctx, chartState, statesByTf,
            buffersByTf: null, chartBuffer: null, in cfg);

    /// <summary>Testable overload that also supplies optional <see cref="SeriesBuffer"/> per TF so the
    /// cross-TF identity check can resolve bar-open-time. Pass <c>null</c> buffers when running geometry-only
    /// tests; the rule then degrades to legacy bar_index identity.</summary>
    public static PostBPushObstacleCancelResult EvaluateWithStates(
        in PendingOrderContext ctx,
        PineStateEngine chartState,
        IReadOnlyDictionary<string, PineStateEngine> statesByTf,
        IReadOnlyDictionary<string, SeriesBuffer>? buffersByTf,
        SeriesBuffer? chartBuffer,
        in PostBPushObstacleCancelRuleConfig cfg)
    {
        var swingTf = ctx.SwingBTfToken;
        return EvaluateCore(in ctx, tf =>
        {
            if (string.Equals(tf, swingTf, StringComparison.Ordinal))
                return (chartState, chartBuffer);
            if (statesByTf.TryGetValue(tf, out var state))
            {
                SeriesBuffer? buf = null;
                buffersByTf?.TryGetValue(tf, out buf);
                return (state, buf);
            }
            return (null, null);
        }, in cfg);
    }

    static PostBPushObstacleCancelResult EvaluateCore(
        in PendingOrderContext ctx,
        Func<string, (PineStateEngine? State, SeriesBuffer? Buffer)> resolveState,
        in PostBPushObstacleCancelRuleConfig cfg)
    {
        var (chartState, chartBuffer) = resolveState(ctx.SwingBTfToken);
        if (chartState is null)
            return NoCancel();

        var chartTfPeriod = TfPeriodTokens.Parse(ctx.SwingBTfToken);
        var pushSwings = FindPostBPushSwings(chartState, in ctx, chartBuffer, chartTfPeriod);
        if (pushSwings.Count == 0)
            return NoCancel();

        var wantColor = ctx.IsBuy ? ZoneEffectiveColor.Red : ZoneEffectiveColor.Green;
        var zoneCfg = cfg.ToZoneCollectConfig();
        var pip = cfg.PipSize > 0 ? cfg.PipSize : 0.0001;
        var tol = cfg.TolerancePips * pip;

        PostBPushSwingMatch? bestPush = null;
        ZoneCandidate? bestZone = null;
        string? bestTf = null;
        double bestOverlapPips = -1;
        var crossTfSelfSkipped = 0;

        foreach (var push in pushSwings)
        {
            foreach (var tf in cfg.TfTokens)
            {
                var (state, buffer) = resolveState(tf);
                if (state is null)
                    continue;

                var zones = MultiTfZoneSnapshot.Collect(state, tf, in zoneCfg, buffer);
                foreach (var z in zones)
                {
                    if (z.EffectiveColor != wantColor)
                        continue;

                    if (IsPushSwingOwnKeyLevel(in push, in z, ctx.SwingBTfToken, ref crossTfSelfSkipped))
                        continue;

                    if (!EntrySlZoneGate.Overlaps(z.Low, z.High, push.PushLow, push.PushHigh, tol))
                        continue;

                    var overlapPips = EntrySlZoneGate.ComputeOverlapPips(
                        z.Low, z.High, push.PushLow, push.PushHigh, pip);

                    if (overlapPips > bestOverlapPips)
                    {
                        bestOverlapPips = overlapPips;
                        bestPush = push;
                        bestZone = z;
                        bestTf = tf;
                    }
                }
            }
        }

        if (bestPush is null || bestZone is null || bestTf is null)
            return new PostBPushObstacleCancelResult
            {
                ShouldCancel = false,
                CrossTfSelfIdentitySkipped = crossTfSelfSkipped,
            };

        var matchKind = string.Equals(bestTf, ctx.SwingBTfToken, StringComparison.Ordinal)
            ? PostBPushCancelMatchKind.SameTf
            : PostBPushCancelMatchKind.CrossTf;

        return new PostBPushObstacleCancelResult
        {
            ShouldCancel = true,
            Push = bestPush,
            MatchedTf = FormatTfLabel(bestTf),
            MatchedTfToken = bestTf,
            MatchedSource = bestZone.Source,
            EffectiveColor = bestZone.EffectiveColor,
            BrokenSwing = bestZone.BrokenSwing,
            ZoneLow = bestZone.Low,
            ZoneHigh = bestZone.High,
            OverlapPips = bestOverlapPips,
            MatchKind = matchKind,
            CrossTfSelfIdentitySkipped = crossTfSelfSkipped,
        };
    }

    /// <summary>
    /// Confirmed push swings after Swing 1 on the setup chart TF.
    /// BUY: swing HIGH (type=1); SELL: swing LOW (type=-1).
    /// </summary>
    public static List<PostBPushSwingMatch> FindPostBPushSwings(
        PineStateEngine chartState,
        in PendingOrderContext ctx) =>
        FindPostBPushSwings(chartState, in ctx,
            chartBuffer: null, chartTfPeriod: TfPeriodTokens.Parse(ctx.SwingBTfToken));

    public static List<PostBPushSwingMatch> FindPostBPushSwings(
        PineStateEngine chartState,
        in PendingOrderContext ctx,
        SeriesBuffer? chartBuffer,
        TimeSpan chartTfPeriod)
    {
        var list = new List<PostBPushSwingMatch>();
        var pivots = chartState.Pivots;
        var wantType = ctx.IsBuy ? SwingBResolver.TypeHigh : SwingBResolver.TypeLow;
        var typeLabel = ctx.IsBuy ? "HIGH" : "LOW";

        for (var i = 0; i < pivots.Count; i++)
        {
            var snap = pivots.GetSnapshot(i);
            if (snap.BarIndex <= ctx.SwingBPivotBar)
                continue;
            if (snap.Type != wantType)
                continue;

            var (lo, hi) = BuildPushRange(pivots, i, snap.Price);
            list.Add(new PostBPushSwingMatch
            {
                PivotIndex = i,
                PivotBar = snap.BarIndex,
                PushTypeLabel = typeLabel,
                PushLow = lo,
                PushHigh = hi,
                PivotOpenTime = ResolveOpenTime(chartBuffer, snap.BarIndex),
                ChartTfPeriod = chartTfPeriod,
            });
        }

        return list;
    }

    static DateTime? ResolveOpenTime(SeriesBuffer? buffer, int pivotBar)
    {
        if (buffer is null) return null;
        var current = buffer.CurrentEvaluationBarIndex;
        var offset = current - pivotBar;
        if (offset < 0) return null;
        return buffer.TryGetSnapshotAtOffset(offset, out var snap)
            ? snap.OpenChartTimeLocal
            : (DateTime?)null;
    }

    static (double Low, double High) BuildPushRange(PivotStateStore pivots, int idx, double pivotPrice)
    {
        if (pivots.GetHasKey(idx) && pivots.GetKeyBox(idx)?.Spec is { } spec)
        {
            var lo = Math.Min(spec.Top, spec.Bottom);
            var hi = Math.Max(spec.Top, spec.Bottom);
            if (hi >= lo)
                return (lo, hi);
        }

        return (pivotPrice, pivotPrice);
    }

    /// <summary>
    /// Determine whether the candidate zone is the push swing's <b>own</b> keylevel — either in the same TF
    /// (legacy bar_index match) or in a different TF where the same physical pivot is represented (cross-TF
    /// time-containment + price-midpoint guard). Returns <c>true</c> to skip the zone as identity.
    /// </summary>
    /// <param name="crossTfSelfSkipped">Incremented when the cross-TF rule fires; non-zero means new logic actually
    /// blocked a self-overlap that the legacy bar_index check would have missed.</param>
    public static bool IsPushSwingOwnKeyLevel(
        in PostBPushSwingMatch push,
        in ZoneCandidate zone,
        string chartTfToken,
        ref int crossTfSelfSkipped)
    {
        // Same-TF fast path: original bar_index/pivot_index match.
        if (zone.PivotBar.HasValue && zone.PivotBar.Value == push.PivotBar
            && string.Equals(zone.TfToken, chartTfToken, StringComparison.Ordinal))
            return true;
        if (zone.PivotIndex.HasValue && zone.PivotIndex.Value == push.PivotIndex
            && string.Equals(zone.TfToken, chartTfToken, StringComparison.Ordinal))
            return true;

        // Cross-TF: only run when we have time + TF period for both sides.
        if (string.Equals(zone.TfToken, chartTfToken, StringComparison.Ordinal))
            return false;
        if (!zone.PivotOpenTime.HasValue || !push.PivotOpenTime.HasValue)
            return false;
        if (zone.TfPeriod <= TimeSpan.Zero || push.ChartTfPeriod <= TimeSpan.Zero)
            return false;

        var pStart = push.PivotOpenTime.Value;
        var pEnd   = pStart + push.ChartTfPeriod;
        var zStart = zone.PivotOpenTime.Value;
        var zEnd   = zStart + zone.TfPeriod;

        var zoneInsidePush = zStart >= pStart && zEnd <= pEnd;   // smaller TF inside chart push bar
        var pushInsideZone = pStart >= zStart && pEnd <= zEnd;   // chart push bar inside larger TF zone bar

        if (!zoneInsidePush && !pushInsideZone)
            return false;

        // Price-midpoint guard for the "push inside larger zone-bar" case (e.g. H1/H4 4×/16× window may hold
        // a different sub-bar swing at a very different price level). Zone-inside-push doesn't need this guard
        // because a smaller bar's high/low is geometrically bounded by the larger bar's high/low.
        if (pushInsideZone && !zoneInsidePush)
        {
            var zSpan = Math.Max(zone.High - zone.Low, 0);
            var pSpan = Math.Max(push.PushHigh - push.PushLow, 0);
            var span  = Math.Max(zSpan, pSpan);
            if (span > 0)
            {
                var midZ = (zone.High + zone.Low) / 2.0;
                var midP = (push.PushHigh + push.PushLow) / 2.0;
                if (Math.Abs(midZ - midP) > span * 0.5)
                    return false;
            }
        }

        crossTfSelfSkipped++;
        return true;
    }

    static PostBPushObstacleCancelResult NoCancel() => new() { ShouldCancel = false };

    public static string FormatCancelLog(in PendingOrderContext ctx, in PostBPushObstacleCancelResult result)
    {
        var push = result.Push!;
        var pushRange = $"[{push.PushLow.ToString("0.#####", CultureInfo.InvariantCulture)}..{push.PushHigh.ToString("0.#####", CultureInfo.InvariantCulture)}]";
        var zoneRange = result.ZoneLow.HasValue && result.ZoneHigh.HasValue
            ? $"[{result.ZoneLow.Value.ToString("0.#####", CultureInfo.InvariantCulture)}..{result.ZoneHigh.Value.ToString("0.#####", CultureInfo.InvariantCulture)}]"
            : "[?..?]";
        var overlap = $"{result.OverlapPips.ToString("0.#", CultureInfo.InvariantCulture)}p";

        var src = result.MatchedSource switch
        {
            ZoneSourceKind.OrderBlock => "OrderBlock",
            ZoneSourceKind.BrokenKeyLevel => "BrokenKeyLevel",
            _ => "KeyLevel",
        };

        var color = result.EffectiveColor == ZoneEffectiveColor.Green ? "GREEN" : "RED";
        if (result.MatchedSource == ZoneSourceKind.BrokenKeyLevel && result.BrokenSwing.HasValue)
        {
            var swingLabel = result.BrokenSwing == BrokenSwingType.SwingLow ? "swingLow=>GREEN" : "swingHigh=>RED";
            return $"[L6BT] CANCEL {ctx.Label} ({CancelReason}) pushBar={push.PivotBar} pushType={push.PushTypeLabel} push={pushRange} " +
                   $"matched {result.MatchedTf} {src} {swingLabel} zone={zoneRange} overlap={overlap}";
        }

        return $"[L6BT] CANCEL {ctx.Label} ({CancelReason}) pushBar={push.PivotBar} pushType={push.PushTypeLabel} push={pushRange} " +
               $"matched {result.MatchedTf} {src} {color} zone={zoneRange} overlap={overlap}";
    }

    static string FormatTfLabel(string tfToken) => tfToken switch
    {
        "240" => "H4",
        "60" => "H1",
        "15" => "M15",
        "5" => "M5",
        _ => tfToken,
    };
}
