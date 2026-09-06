using System;
using System.Collections.Generic;
using System.Globalization;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Series;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>
/// Result of <see cref="SwingCZoneObstacleGate.Evaluate"/>.
/// </summary>
public sealed record SwingCZoneObstacleResult
{
    public bool HasObstacle { get; init; }
    public string? ObstacleTf { get; init; }
    public ZoneSourceKind? ObstacleSource { get; init; }
    public ZoneEffectiveColor? ObstacleColor { get; init; }
    public double? ObstacleLow { get; init; }
    public double? ObstacleHigh { get; init; }
    public double OverlapPips { get; init; }
    /// <summary>Number of zones skipped because they were identified as the C pivot's own zone (self-identity guard).</summary>
    public int SelfIdentitySkipped { get; init; }
    /// <summary>True when the obstacle was found in the fallback TF set (e.g. Daily) rather than the primary set (M5/M15/H1/H4).</summary>
    public bool IsFallback { get; init; }
}

/// <summary>
/// Entry gate: skip the trade when the swing-C zone overlaps with a zone of the <b>same color</b>
/// as C on any configured TF (M5 / M15 / H1 / H4). Same-color overlap means strong confirmation
/// at C's level that will resist or absorb the move, making the TP unlikely to be reached.
///
/// BUY: C is a confirmed HIGH → C zone is RED → a second RED zone at C means resistance on resistance
///      → BUY unlikely to push through → skip.
/// SELL: C is a confirmed LOW → C zone is GREEN → a second GREEN zone at C means support on support
///      → SELL unlikely to push through → skip.
///
/// Fallback: when no obstacle is found in the primary states (M5/M15/H1/H4), optionally scan
/// <see cref="Evaluate"/>'s <c>fallbackStates</c> (typically Daily "1440") before returning clear.
///
/// Self-identity guard: the C pivot's own keylevel zone (on the chart TF, or cross-TF representations
/// of the same candle) is excluded to prevent false positives.
/// Cross-TF identity uses time containment + price-midpoint guard (same algorithm as
/// <see cref="PostBPushObstacleCancelRule.IsPushSwingOwnKeyLevel"/>).
/// </summary>
public static class SwingCZoneObstacleGate
{
    static readonly EntrySlZoneGateConfig s_scanCfg = new()
    {
        IncludeKeyLevels = true,
        IncludeOrderBlocks = true,
        IncludeBrokenKeyLevels = false,
        RequireSameColor = false,
    };

    /// <summary>
    /// Evaluate whether the C zone overlaps with a same-color zone on any of the provided TFs.
    /// When no obstacle is found in <paramref name="states"/>, optionally scans
    /// <paramref name="fallbackStates"/> (typically Daily) before returning clear.
    /// Returns <see cref="SwingCZoneObstacleResult.HasObstacle"/> = false when
    /// <paramref name="states"/> is empty or C has no valid edge.
    /// </summary>
    /// <param name="tfBuffers">
    /// Per-TF SeriesBuffers keyed by the same TF tokens as <paramref name="states"/>.
    /// When provided, enables cross-TF self-identity detection by populating
    /// <see cref="ZoneCandidate.PivotOpenTime"/> for keylevel zones. Without this, zones on
    /// shorter TFs (e.g. M5) that represent the C pivot's own bar cannot be filtered out.
    /// </param>
    public static SwingCZoneObstacleResult Evaluate(
        SwingCEdgeResult cResult,
        bool isBuy,
        string chartTfToken,
        IReadOnlyDictionary<string, PineStateEngine> states,
        double pipSize,
        SeriesBuffer? chartBuffer = null,
        IReadOnlyDictionary<string, PineStateEngine>? fallbackStates = null,
        IReadOnlyDictionary<string, SeriesBuffer>? tfBuffers = null)
    {
        var cTop = cResult.EdgeTop;
        var cBot = cResult.EdgeBottom;
        if (cTop <= 0 || cBot <= 0 || cTop <= cBot)
            return new SwingCZoneObstacleResult { HasObstacle = false };

        // C zone color: BUY→RED (high-pivot = resistance zone), SELL→GREEN (low-pivot = support zone)
        var wantColor = isBuy ? ZoneEffectiveColor.Red : ZoneEffectiveColor.Green;
        var chartTfPeriod = TfPeriodTokens.Parse(chartTfToken);
        var cOpenTime = ResolveOpenTime(chartBuffer, cResult.PivotBar);
        var pip = pipSize > 0 ? pipSize : 0.0001;
        var selfSkipped = 0;

        // Primary scan (M5/M15/H1/H4)
        var primary = ScanStates(states, in cResult, wantColor, cBot, cTop, pip,
            chartTfToken, cOpenTime, chartTfPeriod, isFallback: false, ref selfSkipped,
            tfBuffers: tfBuffers);
        if (primary.HasObstacle)
            return primary with { SelfIdentitySkipped = selfSkipped };

        // Fallback scan (Daily) — only when primary found nothing
        if (fallbackStates is { Count: > 0 })
        {
            var fallback = ScanStates(fallbackStates, in cResult, wantColor, cBot, cTop, pip,
                chartTfToken, cOpenTime, chartTfPeriod, isFallback: true, ref selfSkipped,
                tfBuffers: tfBuffers);
            if (fallback.HasObstacle)
                return fallback with { SelfIdentitySkipped = selfSkipped };
        }

        return new SwingCZoneObstacleResult { HasObstacle = false, SelfIdentitySkipped = selfSkipped };
    }

    // ── Private scan helpers ──────────────────────────────────────────────

    static SwingCZoneObstacleResult ScanStates(
        IReadOnlyDictionary<string, PineStateEngine> states,
        in SwingCEdgeResult cResult,
        ZoneEffectiveColor wantColor,
        double cBot, double cTop,
        double pip,
        string chartTfToken,
        DateTime? cOpenTime,
        TimeSpan chartTfPeriod,
        bool isFallback,
        ref int selfSkipped,
        IReadOnlyDictionary<string, SeriesBuffer>? tfBuffers = null)
    {
        ZoneCandidate? bestZone = null;
        string? bestTf = null;
        double bestOverlapPips = 0;

        foreach (var (tfToken, tfState) in states)
        {
            SeriesBuffer? tfBuf = null;
            tfBuffers?.TryGetValue(tfToken, out tfBuf);
            var zones = MultiTfZoneSnapshot.Collect(tfState, tfToken, in s_scanCfg, tfBuf);
            foreach (var z in zones)
            {
                if (z.EffectiveColor != wantColor)
                    continue;

                if (IsCZoneOwnKeyLevel(in cResult, in z, chartTfToken, cOpenTime, chartTfPeriod, ref selfSkipped))
                    continue;

                if (!EntrySlZoneGate.Overlaps(z.Low, z.High, cBot, cTop, tolerance: 0))
                    continue;

                var overlapPips = EntrySlZoneGate.ComputeOverlapPips(z.Low, z.High, cBot, cTop, pip);
                if (overlapPips > bestOverlapPips)
                {
                    bestOverlapPips = overlapPips;
                    bestZone = z;
                    bestTf = tfToken;
                }
            }
        }

        if (bestZone is null || bestTf is null)
            return new SwingCZoneObstacleResult { HasObstacle = false };

        return new SwingCZoneObstacleResult
        {
            HasObstacle = true,
            ObstacleTf = FormatTfLabel(bestTf),
            ObstacleSource = bestZone.Source,
            ObstacleColor = bestZone.EffectiveColor,
            ObstacleLow = bestZone.Low,
            ObstacleHigh = bestZone.High,
            OverlapPips = bestOverlapPips,
            IsFallback = isFallback,
        };
    }

    // ── Self-identity guard ───────────────────────────────────────────────

    /// <summary>
    /// True when <paramref name="zone"/> represents the same physical candle as the C pivot,
    /// either on the same TF (bar-index match) or across TFs (time containment + midpoint guard).
    /// </summary>
    public static bool IsCZoneOwnKeyLevel(
        in SwingCEdgeResult cResult,
        in ZoneCandidate zone,
        string chartTfToken,
        DateTime? cPivotOpenTime,
        TimeSpan chartTfPeriod,
        ref int selfIdentitySkipped)
    {
        // Same-TF fast path
        if (string.Equals(zone.TfToken, chartTfToken, StringComparison.Ordinal))
        {
            if (zone.PivotBar.HasValue && zone.PivotBar.Value == cResult.PivotBar)
                return true;
            // PivotIndex match: only use when both sides have a valid non-negative index.
            if (zone.PivotIndex.HasValue && zone.PivotIndex.Value >= 0
                && cResult.PivotIndex >= 0
                && zone.PivotIndex.Value == cResult.PivotIndex)
                return true;
            return false;
        }

        // Cross-TF time-containment guard (requires open times and periods)
        if (!cPivotOpenTime.HasValue || !zone.PivotOpenTime.HasValue)
            return false;
        if (zone.TfPeriod <= TimeSpan.Zero || chartTfPeriod <= TimeSpan.Zero)
            return false;

        var pStart = cPivotOpenTime.Value;
        var pEnd   = pStart + chartTfPeriod;
        var zStart = zone.PivotOpenTime.Value;
        var zEnd   = zStart + zone.TfPeriod;

        var zoneInsideC = zStart >= pStart && zEnd <= pEnd;
        var cInsideZone = pStart >= zStart && pEnd <= zEnd;

        if (!zoneInsideC && !cInsideZone)
            return false;

        // Midpoint guard for "C inside larger zone bar": ensure the zone bar represents the
        // same price area as C, not just a coincident candle at a different price.
        if (cInsideZone && !zoneInsideC)
        {
            var zSpan = Math.Max(zone.High - zone.Low, 0);
            var cSpan = Math.Max(cResult.EdgeTop - cResult.EdgeBottom, 0);
            var span  = Math.Max(zSpan, cSpan);
            if (span > 0)
            {
                var midZ = (zone.High + zone.Low) / 2.0;
                var midC = (cResult.EdgeTop + cResult.EdgeBottom) / 2.0;
                if (Math.Abs(midZ - midC) > span * 0.5)
                    return false;
            }
        }

        selfIdentitySkipped++;
        return true;
    }

    // ── Log helpers ───────────────────────────────────────────────────────

    public static string FormatSkipLog(
        in SwingCEdgeResult cResult,
        bool isBuy,
        in SwingCZoneObstacleResult r,
        string label)
    {
        var dir = isBuy ? "BUY" : "SELL";
        var colorLabel = r.ObstacleColor == ZoneEffectiveColor.Green ? "GREEN" : "RED";
        var src = r.ObstacleSource switch
        {
            ZoneSourceKind.OrderBlock => "OB",
            ZoneSourceKind.BrokenKeyLevel => "BrokenKL",
            _ => "KL",
        };
        var fallbackTag = r.IsFallback ? " (fallback-daily)" : "";
        return $"[L6BT] ZONE SKIP {dir} {label} swing-C zone=[{Fmt(cResult.EdgeBottom)}..{Fmt(cResult.EdgeTop)}] " +
               $"obstacle{fallbackTag} {r.ObstacleTf} {src} {colorLabel} zone=[{Fmt(r.ObstacleLow ?? 0)}..{Fmt(r.ObstacleHigh ?? 0)}] " +
               $"overlap={r.OverlapPips:0.#}p selfSkipped={r.SelfIdentitySkipped}";
    }

    static string FormatTfLabel(string tfToken) => tfToken switch
    {
        "240"  => "H4",
        "60"   => "H1",
        "15"   => "M15",
        "5"    => "M5",
        "1440" => "Daily",
        "D"    => "Daily",
        "1D"   => "Daily",
        _      => tfToken,
    };

    static string Fmt(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

    static DateTime? ResolveOpenTime(SeriesBuffer? buffer, int pivotBar)
    {
        if (buffer is null || pivotBar < 0) return null;
        var current = buffer.CurrentEvaluationBarIndex;
        var offset = current - pivotBar;
        if (offset < 0) return null;
        return buffer.TryGetSnapshotAtOffset(offset, out var snap)
            ? snap.OpenChartTimeLocal
            : (DateTime?)null;
    }
}
