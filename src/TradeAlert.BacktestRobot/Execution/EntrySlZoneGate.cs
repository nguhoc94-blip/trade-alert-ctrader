using System;
using System.Collections.Generic;
using System.Globalization;
using TradeAlert.Core.Engines;
using TradeAlert.Indicator;

namespace TradeAlert.BacktestRobot.Execution;

public sealed class EntrySlZoneGateResult
{
    public bool Passed { get; init; }
    public string SkipReason { get; init; } = "";
    public double Entry { get; init; }
    public double Sl { get; init; }
    public double RiskLow { get; init; }
    public double RiskHigh { get; init; }
    public string? MatchedTf { get; init; }
    public ZoneSourceKind? MatchedSource { get; init; }
    public BrokenSwingType? BrokenSwing { get; init; }
    public ZoneEffectiveColor? EffectiveColor { get; init; }
    public double? ZoneLow { get; init; }
    public double? ZoneHigh { get; init; }
    public double OverlapPips { get; init; }

    public int ZonesScanned { get; init; }
    public int SameColorZones { get; init; }
    public int OwnSwingBSkipped { get; init; }
    public int M5NonObSkipped { get; init; }
    public int M5M15SlBoundarySkipped { get; init; }
    public int OverlappingAfterExclusion { get; init; }

    public string TfTokenList { get; init; } = "";
}

/// <summary>
/// Entry-SL risk zone confluence gate — requires same-color MTF zone overlap
/// between planned Entry and SL before order execution.
/// </summary>
public static class EntrySlZoneGate
{
    public const string M5TfToken = "5";
    public const string M15TfToken = "15";

    public static bool IsM5OrM15Tf(string tfToken) =>
        string.Equals(tfToken, M5TfToken, StringComparison.Ordinal)
        || string.Equals(tfToken, M15TfToken, StringComparison.Ordinal);

    /// <summary>
    /// M5/M15 confluence between entry and SL must stay inside the risk band at the SL edge:
    /// BUY → zone bottom &gt; SL; SELL → zone top &lt; SL.
    /// </summary>
    public static bool PassesShortTfSlBoundary(bool isBuy, double sl, in ZoneCandidate candidate)
    {
        if (!IsM5OrM15Tf(candidate.TfToken))
            return true;

        return isBuy ? candidate.Low > sl : candidate.High < sl;
    }

    public static EntrySlZoneGateResult Evaluate(
        in TradePlan plan,
        PerSymbolSignalHost host,
        in EntrySlZoneGateConfig cfg) =>
        EvaluateCore(in plan, tf =>
        {
            if (host.TryGetState(tf, out var state))
                return state;
            return null;
        }, in cfg);

    /// <summary>Testable overload with explicit TF → state map.</summary>
    public static EntrySlZoneGateResult EvaluateWithStates(
        in TradePlan plan,
        IReadOnlyDictionary<string, PineStateEngine> statesByTf,
        in EntrySlZoneGateConfig cfg) =>
        EvaluateCore(in plan, tf =>
        {
            if (statesByTf.TryGetValue(tf, out var state))
                return state;
            return null;
        }, in cfg);

    static EntrySlZoneGateResult EvaluateCore(
        in TradePlan plan,
        Func<string, PineStateEngine?> resolveState,
        in EntrySlZoneGateConfig cfg)
    {
        var entry = plan.EntryLimit;
        var sl = plan.StopLoss;
        var pip = cfg.PipSize > 0 ? cfg.PipSize : 0.0001;
        var tol = cfg.OverlapTolerancePips * pip;

        var (riskLow, riskHigh) = ComputeRiskZone(plan.IsBuy, entry, sl);
        var wantColor = plan.IsBuy ? ZoneEffectiveColor.Green : ZoneEffectiveColor.Red;
        var tfList = string.Join(",", cfg.TfTokens);

        var zonesScanned = 0;
        var sameColorZones = 0;
        var ownBSkipped = 0;
        var m5NonObSkipped = 0;
        var m5M15SlBoundarySkipped = 0;
        var overlappingAfterExclusion = 0;

        ZoneCandidate? best = null;
        double bestOverlapPips = 0;

        foreach (var tf in cfg.TfTokens)
        {
            var state = resolveState(tf);
            if (state is null)
                continue;

            var zones = MultiTfZoneSnapshot.Collect(state, tf, in cfg);
            foreach (var z in zones)
            {
                zonesScanned++;

                if (cfg.RequireSameColor && z.EffectiveColor != wantColor)
                    continue;

                sameColorZones++;

                if (IsM5NonObConfluence(in z, in cfg))
                {
                    m5NonObSkipped++;
                    continue;
                }

                if (IsSetupSwingBKeyLevel(in plan, in z, in cfg))
                {
                    ownBSkipped++;
                    continue;
                }

                if (!PassesShortTfSlBoundary(plan.IsBuy, sl, in z))
                {
                    m5M15SlBoundarySkipped++;
                    continue;
                }

                if (!Overlaps(z.Low, z.High, riskLow, riskHigh, tol))
                    continue;

                overlappingAfterExclusion++;

                var overlapPips = ComputeOverlapPips(z.Low, z.High, riskLow, riskHigh, pip);
                if (overlapPips > bestOverlapPips)
                {
                    bestOverlapPips = overlapPips;
                    best = z;
                }
            }
        }

        var diag = new
        {
            ZonesScanned = zonesScanned,
            SameColorZones = sameColorZones,
            OwnSwingBSkipped = ownBSkipped,
            M5NonObSkipped = m5NonObSkipped,
            M5M15SlBoundarySkipped = m5M15SlBoundarySkipped,
            OverlappingAfterExclusion = overlappingAfterExclusion,
        };

        if (best is null)
        {
            var colorLabel = wantColor == ZoneEffectiveColor.Green ? "GREEN" : "RED";
            return new EntrySlZoneGateResult
            {
                Passed = false,
                SkipReason = $"no {colorLabel} KL/OB overlap across {tfList}",
                Entry = entry,
                Sl = sl,
                RiskLow = riskLow,
                RiskHigh = riskHigh,
                TfTokenList = tfList,
                ZonesScanned = diag.ZonesScanned,
                SameColorZones = diag.SameColorZones,
                OwnSwingBSkipped = diag.OwnSwingBSkipped,
                M5NonObSkipped = diag.M5NonObSkipped,
                M5M15SlBoundarySkipped = diag.M5M15SlBoundarySkipped,
                OverlappingAfterExclusion = diag.OverlappingAfterExclusion,
            };
        }

        return new EntrySlZoneGateResult
        {
            Passed = true,
            Entry = entry,
            Sl = sl,
            RiskLow = riskLow,
            RiskHigh = riskHigh,
            MatchedTf = FormatTfLabel(best.TfToken),
            MatchedSource = best.Source,
            BrokenSwing = best.BrokenSwing,
            EffectiveColor = best.EffectiveColor,
            ZoneLow = best.Low,
            ZoneHigh = best.High,
            OverlapPips = bestOverlapPips,
            TfTokenList = tfList,
            ZonesScanned = diag.ZonesScanned,
            SameColorZones = diag.SameColorZones,
            OwnSwingBSkipped = diag.OwnSwingBSkipped,
            M5NonObSkipped = diag.M5NonObSkipped,
            M5M15SlBoundarySkipped = diag.M5M15SlBoundarySkipped,
            OverlappingAfterExclusion = diag.OverlappingAfterExclusion,
        };
    }

    /// <summary>M5 confluence must be OB-only when <see cref="EntrySlZoneGateConfig.M5ConfluenceObOnly"/> is set.</summary>
    public static bool IsM5NonObConfluence(in ZoneCandidate candidate, in EntrySlZoneGateConfig cfg)
    {
        if (!cfg.M5ConfluenceObOnly)
            return false;

        if (!string.Equals(candidate.TfToken, M5TfToken, StringComparison.Ordinal))
            return false;

        return candidate.Source != ZoneSourceKind.OrderBlock;
    }

    public static bool IsSetupSwingBKeyLevel(
        in TradePlan plan,
        in ZoneCandidate candidate,
        in EntrySlZoneGateConfig cfg)
    {
        if (!cfg.ExcludeSetupSwingBKeyLevel)
            return false;

        if (candidate.Source != ZoneSourceKind.KeyLevel
            && candidate.Source != ZoneSourceKind.BrokenKeyLevel)
            return false;

        if (!string.Equals(candidate.TfToken, plan.SwingBTfToken, StringComparison.Ordinal))
            return false;

        if (candidate.PivotBar.HasValue && candidate.PivotBar.Value == plan.SwingBPivotBar)
            return true;

        if (candidate.PivotIndex.HasValue && candidate.PivotIndex.Value == plan.SwingBPivotIndex)
            return true;

        if (plan.SwingBKeyHigh > plan.SwingBKeyLow
            && SamePrice(candidate.Low, plan.SwingBKeyLow, cfg)
            && SamePrice(candidate.High, plan.SwingBKeyHigh, cfg))
            return true;

        return false;
    }

    static bool SamePrice(double a, double b, in EntrySlZoneGateConfig cfg)
    {
        var pip = cfg.PipSize > 0 ? cfg.PipSize : 0.0001;
        var tol = Math.Max(cfg.OverlapTolerancePips * pip, pip * 0.01);
        return Math.Abs(a - b) <= tol;
    }

    public static (double Low, double High) ComputeRiskZone(bool isBuy, double entry, double sl)
    {
        if (isBuy)
            return (Math.Min(sl, entry), Math.Max(sl, entry));

        return (Math.Min(entry, sl), Math.Max(entry, sl));
    }

    public static bool Overlaps(double zoneLow, double zoneHigh, double riskLow, double riskHigh, double tolerance) =>
        zoneLow <= riskHigh + tolerance && zoneHigh >= riskLow - tolerance;

    public static double ComputeOverlapPips(
        double zoneLow,
        double zoneHigh,
        double riskLow,
        double riskHigh,
        double pipSize)
    {
        var overlapLow = Math.Max(zoneLow, riskLow);
        var overlapHigh = Math.Min(zoneHigh, riskHigh);
        var overlap = Math.Max(0, overlapHigh - overlapLow);
        return pipSize > 0 ? overlap / pipSize : 0;
    }

    static string FormatDiagSuffix(in EntrySlZoneGateResult gate) =>
        $" scanned={gate.ZonesScanned} sameColor={gate.SameColorZones} ownBSkipped={gate.OwnSwingBSkipped} m5NonObSkipped={gate.M5NonObSkipped} m5m15SlBoundarySkipped={gate.M5M15SlBoundarySkipped} overlapAfterExcl={gate.OverlappingAfterExclusion}";

    public static string FormatPassLog(in TradePlan plan, int ruleSlot, in EntrySlZoneGateResult gate)
    {
        var dir = plan.IsBuy ? "BUY" : "SELL";
        var r = ruleSlot + 1;
        var risk = $"[{gate.RiskLow.ToString("0.#####", CultureInfo.InvariantCulture)}..{gate.RiskHigh.ToString("0.#####", CultureInfo.InvariantCulture)}]";
        var zone = gate.ZoneLow.HasValue && gate.ZoneHigh.HasValue
            ? $"[{gate.ZoneLow.Value.ToString("0.#####", CultureInfo.InvariantCulture)}..{gate.ZoneHigh.Value.ToString("0.#####", CultureInfo.InvariantCulture)}]"
            : "[?..?]";
        var overlap = $"{gate.OverlapPips.ToString("0.#", CultureInfo.InvariantCulture)}p";
        var diag = FormatDiagSuffix(in gate);

        if (gate.MatchedSource == ZoneSourceKind.BrokenKeyLevel && gate.BrokenSwing.HasValue)
        {
            var swingLabel = gate.BrokenSwing == BrokenSwingType.SwingLow ? "swingLow=>RED" : "swingHigh=>GREEN";
            return $"[L6BT] ZONE PASS R{r} {dir} risk={risk} matched {gate.MatchedTf} BrokenKeyLevel {swingLabel} zone={zone} overlap={overlap}{diag}";
        }

        var color = gate.EffectiveColor == ZoneEffectiveColor.Green ? "GREEN" : "RED";
        var src = gate.MatchedSource == ZoneSourceKind.OrderBlock ? "OB" : "KeyLevel";
        return $"[L6BT] ZONE PASS R{r} {dir} risk={risk} matched {gate.MatchedTf} {src} {color} zone={zone} overlap={overlap}{diag}";
    }

    public static string FormatSkipLog(in TradePlan plan, int ruleSlot, in EntrySlZoneGateResult gate)
    {
        var dir = plan.IsBuy ? "BUY" : "SELL";
        var r = ruleSlot + 1;
        var risk = $"[{gate.RiskLow.ToString("0.#####", CultureInfo.InvariantCulture)}..{gate.RiskHigh.ToString("0.#####", CultureInfo.InvariantCulture)}]";
        var color = plan.IsBuy ? "GREEN" : "RED";
        var diag = FormatDiagSuffix(in gate);
        return $"[L6BT] ZONE SKIP R{r} {dir} risk={risk} ownBSkipped={gate.OwnSwingBSkipped} no {color} KL/OB overlap across {gate.TfTokenList}{diag}";
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
