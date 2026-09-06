using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>Configuration passed from bot parameters for strategy evaluation CSV/summary.</summary>
public sealed class InternalStrategyEvalContext
{
    public double StartEquityUsd { get; init; } = 100_000;
    public double RewardRisk { get; init; }
    public ProtectionAnchorMode ProtectionAnchorMode { get; init; }
    public bool UseSwingCEdgeTakeProfit { get; init; }
}

/// <summary>One closed trade record for internal equity curve and breakdown metrics.</summary>
public sealed class InternalCloseRecord
{
    public string Label { get; init; } = "";
    public DateTime CloseTime { get; init; }
    public string Rule { get; init; } = "";
    public string Direction { get; init; } = "";
    public string CloseReason { get; init; } = "";
    public double InternalGrossR { get; init; }
    public double InternalNetR { get; init; }
    public double InternalNetUsd { get; init; }
    public double RawNet { get; init; }

    public int TradeIndex { get; set; }
    public double InternalEquityUsd { get; set; }
    public double InternalEquityR { get; set; }
    public double InternalDrawdownUsd { get; set; }
    public double InternalDrawdownPct { get; set; }
    public double InternalDrawdownR { get; set; }
}

/// <summary>Aggregated counters from execution audit for warnings and CSV v2.</summary>
public sealed class InternalStrategyAuditSnapshot
{
    public int HistoryClosedCount { get; init; }
    public int CloseAuditCount { get; init; }
    public int PlanMissingCount { get; init; }
    public int UnknownCloseCount { get; init; }
    public int ProtectionTpMismatchCount { get; init; }
    public int ProtectionSlMismatchCount { get; init; }
    public int ProtectionSyncOkCount { get; init; }
    public int ProtectionSyncRejectCount { get; init; }
    public double RawNetSum { get; init; }
    public double RawVsInternalNetDiffSum { get; init; }
    public IReadOnlyDictionary<string, int> ReasonCounts { get; init; } = new Dictionary<string, int>();
    public bool EnableVirtualXrBarCloseExit { get; init; }
    public double VirtualXrTriggerR { get; init; }
    public string VirtualXrFillMode { get; init; } = "";
    public int XrBarCloseCount { get; init; }
    public double XrBarCloseNetR { get; init; }
    public int XrTouchNoConfirmCount { get; init; }
}

/// <summary>
/// Tracks per-trade internal equity, drawdown, EV, streaks, and breakdown metrics for
/// optimization ranking without relying on raw cTrader History Net/PF.
/// </summary>
public sealed class InternalStrategyMetricsEngine
{
    public const double BreakevenRTolerance = 0.05;
    public const int LowSampleSizeThreshold = 50;
    public const double LowPfThreshold = 1.2;
    public const double HighDrawdownPctThreshold = 10.0;

    readonly List<InternalCloseRecord> _records = new();
    InternalStrategyEvalContext _context = new();
    bool _finalized;

    public void SetContext(in InternalStrategyEvalContext context) => _context = context;

    public void RecordClose(
        string label,
        DateTime closeTime,
        bool isBuy,
        string closeReason,
        in InternalCloseScore internalScore,
        double rawNet)
    {
        _finalized = false;
        _records.Add(new InternalCloseRecord
        {
            Label = label,
            CloseTime = closeTime,
            Rule = LabelRuleParser.ParseRule(label),
            Direction = isBuy ? "BUY" : "SELL",
            CloseReason = closeReason,
            InternalGrossR = internalScore.InternalGrossR,
            InternalNetR = internalScore.InternalNetR,
            InternalNetUsd = internalScore.InternalNetUsd,
            RawNet = rawNet,
        });
    }

    public IReadOnlyList<InternalCloseRecord> FinalizeEquityCurve()
    {
        if (_finalized)
            return _records;

        var sorted = _records.OrderBy(r => r.CloseTime).ThenBy(r => r.Label, StringComparer.Ordinal).ToList();
        _records.Clear();
        _records.AddRange(sorted);

        var startUsd = _context.StartEquityUsd;
        var equityUsd = startUsd;
        var equityR = 0.0;
        var peakUsd = startUsd;
        var peakR = 0.0;

        for (var i = 0; i < _records.Count; i++)
        {
            var r = _records[i];
            equityUsd += r.InternalNetUsd;
            equityR += r.InternalNetR;

            if (equityUsd > peakUsd)
                peakUsd = equityUsd;
            if (equityR > peakR)
                peakR = equityR;

            r.TradeIndex = i + 1;
            r.InternalEquityUsd = equityUsd;
            r.InternalEquityR = equityR;
            r.InternalDrawdownUsd = peakUsd - equityUsd;
            r.InternalDrawdownPct = peakUsd > 0 ? r.InternalDrawdownUsd / peakUsd * 100.0 : 0;
            r.InternalDrawdownR = peakR - equityR;
        }

        _finalized = true;
        return _records;
    }

    public void PrintStrategyEvaluation(
        Action<string> print,
        in InternalStrategyAuditSnapshot audit)
    {
        var records = FinalizeEquityCurve().ToList();
        var n = records.Count;
        if (n == 0)
        {
            print("[L6BT] INTERNAL STRATEGY EVALUATION: no closed trades");
            return;
        }

        var startUsd = _context.StartEquityUsd;
        var endUsd = records[^1].InternalEquityUsd;
        var netUsd = endUsd - startUsd;
        var netR = records.Sum(r => r.InternalNetR);
        var grossR = records.Sum(r => r.InternalGrossR);

        var netRs = records.Select(r => r.InternalNetR).ToList();
        var netUsds = records.Select(r => r.InternalNetUsd).ToList();
        var winRs = netRs.Where(r => r > BreakevenRTolerance).ToList();
        var lossRs = netRs.Where(r => r < -BreakevenRTolerance).ToList();
        var beCount = netRs.Count(r => Math.Abs(r) <= BreakevenRTolerance);
        var winCount = winRs.Count;
        var lossCount = lossRs.Count;

        var evR = netR / n;
        var evUsd = netUsd / n;
        var grossEvR = grossR / n;
        var medianR = Median(netRs);
        var medianNetUsd = Median(netUsds);
        var stdR = StdDev(netRs);
        var sharpeLike = stdR > 0 ? evR / stdR : 0;
        var sqn = stdR > 0 ? Math.Sqrt(n) * evR / stdR : 0;

        var pfUsd = ProfitFactor(netUsds.Where(x => x >= 0), netUsds.Where(x => x < 0));
        var pfR = ProfitFactorR(winRs, lossRs);
        var avgWinR = winRs.Count > 0 ? winRs.Average() : 0;
        var avgLossR = lossRs.Count > 0 ? lossRs.Average() : 0;
        var payoff = avgLossR != 0 ? Math.Abs(avgWinR / avgLossR) : 0;

        var (maxDdUsd, maxDdPct, maxDdR, ddStart, ddEnd, ddDuration) = ComputeMaxDrawdown(records, startUsd);
        var streaks = ComputeStreaks(netRs);

        print("[L6BT] INTERNAL STRATEGY EVALUATION");
        print($"[L6BT]   internalStartEquityUsd={Fmt2(startUsd)} internalEndEquityUsd={Fmt2(endUsd)} internalNetUsd={Fmt2(netUsd)} internalNetR={Fmt2(netR)}");
        print($"[L6BT]   internalMaxDrawdownUsd={Fmt2(maxDdUsd)} internalMaxDrawdownPct={Fmt2(maxDdPct)} internalMaxDrawdownR={Fmt2(maxDdR)}");
        print($"[L6BT]   internalMaxDrawdownStartTime={FmtTime(ddStart)} internalMaxDrawdownEndTime={FmtTime(ddEnd)} internalMaxDrawdownDuration={ddDuration}");

        print($"[L6BT]   internalEVR={Fmt4(evR)} internalEVUsd={Fmt2(evUsd)} internalGrossEVR={Fmt4(grossEvR)}");
        print($"[L6BT]   internalMedianR={Fmt4(medianR)} internalMedianNetUsd={Fmt2(medianNetUsd)} internalStdDevR={Fmt4(stdR)}");
        print($"[L6BT]   internalSharpeLike={Fmt4(sharpeLike)} internalSQN={Fmt4(sqn)}");

        print($"[L6BT]   internalWinCount={winCount} internalLossCount={lossCount} internalBreakevenCount={beCount}");
        print($"[L6BT]   internalWinRate={Pct(winCount, n)} internalLossRate={Pct(lossCount, n)} internalBreakevenRate={Pct(beCount, n)}");
        print($"[L6BT]   internalAvgWinR={Fmt4(avgWinR)} internalAvgLossR={Fmt4(avgLossR)} internalMedianWinR={Fmt4(Median(winRs))} internalMedianLossR={Fmt4(Median(lossRs))}");
        print($"[L6BT]   internalLargestWinR={Fmt4(winRs.Count > 0 ? winRs.Max() : 0)} internalLargestLossR={Fmt4(lossRs.Count > 0 ? lossRs.Min() : 0)}");
        print($"[L6BT]   internalPayoffRatio={Fmt4(payoff)} internalProfitFactor={Fmt2(pfUsd)} internalProfitFactorR={Fmt4(pfR)}");

        print($"[L6BT]   internalMaxConsecutiveWins={streaks.MaxWins} internalMaxConsecutiveLosses={streaks.MaxLosses} internalCurrentStreak={streaks.CurrentStreak}");
        print($"[L6BT]   internalBestWinStreakR={Fmt4(streaks.BestWinStreakR)} internalWorstLossStreakR={Fmt4(streaks.WorstLossStreakR)}");

        PrintReasonMetrics(print, records);
        PrintRuleMetrics(print, records);
        PrintDirectionMetrics(print, records);
        PrintMonthMetrics(print, records);

        foreach (var line in FormatEquityCsvLines(records))
            print(line);

        PrintQualityWarnings(print, audit, n, pfUsd, maxDdPct, netUsd);
        PrintCsvSummaryV2(print, audit, n, netUsd, netR, evR, evUsd, pfUsd, winCount, n, avgWinR, avgLossR, payoff, maxDdUsd, maxDdPct, maxDdR, sharpeLike, sqn, streaks);
        PrintStrategyScore(print, audit, evR, pfUsd, maxDdPct);

        foreach (var line in FormatRuleCsvLines(records))
            print(line);
        foreach (var line in FormatMonthCsvLines(records))
            print(line);
    }

    /// <summary>Single-line optimization ranking output (internal metrics only, not raw History Net/PF).</summary>
    public void PrintOptResultLine(Action<string> print, in InternalStrategyAuditSnapshot audit)
    {
        var records = FinalizeEquityCurve().ToList();
        var n = records.Count;
        var startUsd = _context.StartEquityUsd;

        double evR = 0;
        double netR = 0;
        double netUsd = 0;
        double pfUsd = 0;
        double maxDdR = 0;
        double maxDdPct = 0;

        if (n > 0)
        {
            netUsd = records[^1].InternalEquityUsd - startUsd;
            netR = records.Sum(r => r.InternalNetR);
            evR = netR / n;
            var netUsds = records.Select(r => r.InternalNetUsd).ToList();
            pfUsd = ProfitFactor(netUsds.Where(x => x >= 0), netUsds.Where(x => x < 0));
            (_, maxDdPct, maxDdR, _, _, _) = ComputeMaxDrawdown(records, startUsd);
        }

        print(TradePlanRrLog.FormatOptResultLine(
            rewardRisk: _context.RewardRisk,
            enableVirtualXrBarCloseExit: audit.EnableVirtualXrBarCloseExit,
            virtualXrTriggerR: audit.VirtualXrTriggerR,
            virtualXrFillMode: audit.VirtualXrFillMode,
            useSwingCEdgeTakeProfit: _context.UseSwingCEdgeTakeProfit,
            internalEVR: evR,
            internalNetR: netR,
            internalNetUsd: netUsd,
            internalPF: pfUsd,
            internalMaxDrawdownR: maxDdR,
            internalMaxDrawdownPct: maxDdPct,
            closedCount: audit.CloseAuditCount,
            unknownCloseCount: audit.UnknownCloseCount,
            planMissingCount: audit.PlanMissingCount,
            protectionTpMismatchCount: audit.ProtectionTpMismatchCount,
            protectionSlMismatchCount: audit.ProtectionSlMismatchCount));
    }

    void PrintReasonMetrics(Action<string> print, List<InternalCloseRecord> records)
    {
        foreach (var reason in ReasonOrder())
        {
            var bucket = records.Where(r => MapReasonKey(r.CloseReason) == reason).ToList();
            if (bucket.Count == 0)
                continue;
            PrintGroupMetrics(print, "REASON METRICS", DisplayReason(reason), bucket);
        }
    }

    void PrintRuleMetrics(Action<string> print, List<InternalCloseRecord> records)
    {
        foreach (var rule in records.Select(r => r.Rule).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
        {
            var bucket = records.Where(r => r.Rule == rule).ToList();
            if (bucket.Count == 0)
                continue;

            var netR = bucket.Sum(r => r.InternalNetR);
            var pf = ProfitFactorR(
                bucket.Select(r => r.InternalNetR).Where(x => x > BreakevenRTolerance),
                bucket.Select(r => r.InternalNetR).Where(x => x < -BreakevenRTolerance));
            var wins = bucket.Count(r => r.InternalNetR > BreakevenRTolerance);
            var winRate = Pct(wins, bucket.Count);
            var evR = bucket.Count > 0 ? netR / bucket.Count : 0;
            var winRs = bucket.Where(r => r.InternalNetR > BreakevenRTolerance).Select(r => r.InternalNetR).ToList();
            var lossRs = bucket.Where(r => r.InternalNetR < -BreakevenRTolerance).Select(r => r.InternalNetR).ToList();

            print($"[L6BT] RULE METRICS rule={rule} count={bucket.Count} netUsd={Fmt2(bucket.Sum(r => r.InternalNetUsd))} netR={Fmt4(netR)} EVR={Fmt4(evR)} PF={Fmt2(pf)} winRate={winRate} avgWinR={Fmt4(Avg(winRs))} avgLossR={Fmt4(Avg(lossRs))} normalTP={CountReason(bucket, "TakeProfit")} SL={CountReason(bucket, "StopLoss")} BBrokenTpEntry={CountReason(bucket, "BBrokenTpEntry")} BBroken={CountReason(bucket, "BBroken")}");
        }
    }

    void PrintDirectionMetrics(Action<string> print, List<InternalCloseRecord> records)
    {
        foreach (var dir in new[] { "BUY", "SELL" })
        {
            var bucket = records.Where(r => r.Direction == dir).ToList();
            if (bucket.Count == 0)
                continue;

            var netR = bucket.Sum(r => r.InternalNetR);
            var pf = ProfitFactorR(
                bucket.Select(r => r.InternalNetR).Where(x => x > BreakevenRTolerance),
                bucket.Select(r => r.InternalNetR).Where(x => x < -BreakevenRTolerance));
            var wins = bucket.Count(r => r.InternalNetR > BreakevenRTolerance);
            var winRs = bucket.Where(r => r.InternalNetR > BreakevenRTolerance).Select(r => r.InternalNetR).ToList();
            var lossRs = bucket.Where(r => r.InternalNetR < -BreakevenRTolerance).Select(r => r.InternalNetR).ToList();
            var maxLossStreak = ComputeStreaks(bucket.Select(r => r.InternalNetR).ToList()).MaxLosses;

            print($"[L6BT] DIRECTION METRICS dir={dir} count={bucket.Count} netUsd={Fmt2(bucket.Sum(r => r.InternalNetUsd))} netR={Fmt4(netR)} EVR={Fmt4(netR / bucket.Count)} PF={Fmt2(pf)} winRate={Pct(wins, bucket.Count)} avgWinR={Fmt4(Avg(winRs))} avgLossR={Fmt4(Avg(lossRs))} maxConsecutiveLosses={maxLossStreak}");
        }
    }

    void PrintMonthMetrics(Action<string> print, List<InternalCloseRecord> records)
    {
        foreach (var month in records.GroupBy(r => r.CloseTime.ToString("yyyy-MM", CultureInfo.InvariantCulture)).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var bucket = month.ToList();
            var netR = bucket.Sum(r => r.InternalNetR);
            var pf = ProfitFactorR(
                bucket.Select(r => r.InternalNetR).Where(x => x > BreakevenRTolerance),
                bucket.Select(r => r.InternalNetR).Where(x => x < -BreakevenRTolerance));
            var wins = bucket.Count(r => r.InternalNetR > BreakevenRTolerance);
            var monthMaxDdR = ComputeLocalMaxDrawdownR(bucket);

            print($"[L6BT] MONTH METRICS month={month.Key} count={bucket.Count} netUsd={Fmt2(bucket.Sum(r => r.InternalNetUsd))} netR={Fmt4(netR)} EVR={Fmt4(netR / bucket.Count)} PF={Fmt2(pf)} winRate={Pct(wins, bucket.Count)} maxDrawdownR={Fmt4(monthMaxDdR)}");
        }
    }

    static void PrintGroupMetrics(Action<string> print, string tag, string key, List<InternalCloseRecord> bucket)
    {
        var netR = bucket.Sum(r => r.InternalNetR);
        var wins = bucket.Count(r => r.InternalNetR > BreakevenRTolerance);
        var losses = bucket.Count(r => r.InternalNetR < -BreakevenRTolerance);
        print($"[L6BT] {tag} reason={key} count={bucket.Count} netUsd={Fmt2(bucket.Sum(r => r.InternalNetUsd))} netR={Fmt4(netR)} avgR={Fmt4(netR / bucket.Count)} winCount={wins} lossCount={losses} winRate={Pct(wins, bucket.Count)}");
    }

    void PrintQualityWarnings(Action<string> print, in InternalStrategyAuditSnapshot audit, int closedCount, double internalPf, double maxDdPct, double internalNetUsd)
    {
        if (closedCount < LowSampleSizeThreshold)
            print("[L6BT] WARNING LOW SAMPLE SIZE");

        if (internalPf < LowPfThreshold)
            print("[L6BT] WARNING LOW INTERNAL PF");

        if (maxDdPct > HighDrawdownPctThreshold)
            print("[L6BT] WARNING HIGH INTERNAL DRAWDOWN");

        if (audit.UnknownCloseCount > 0)
            print(TradePlanRrLog.FormatUnknownClosesExistWarning(audit.UnknownCloseCount));

        if (audit.PlanMissingCount > 0)
            print($"[L6BT] WARNING PLAN MISSING EXISTS count={audit.PlanMissingCount}");

        if (audit.CloseAuditCount != audit.HistoryClosedCount)
            print(TradePlanRrLog.FormatCloseAuditCountMismatchWarning(audit.CloseAuditCount, audit.HistoryClosedCount));

        if (Math.Abs(audit.RawVsInternalNetDiffSum) > Math.Abs(internalNetUsd) * 0.1 && Math.Abs(internalNetUsd) > 0.01)
            print(TradePlanRrLog.FormatRawInternalNetDiffWarning(audit.RawNetSum, internalNetUsd, audit.RawVsInternalNetDiffSum));
    }

    void PrintCsvSummaryV2(
        Action<string> print,
        in InternalStrategyAuditSnapshot audit,
        int closedCount,
        double internalNetUsd,
        double internalNetR,
        double evR,
        double evUsd,
        double internalPf,
        int winCount,
        int n,
        double avgWinR,
        double avgLossR,
        double payoff,
        double maxDdUsd,
        double maxDdPct,
        double maxDdR,
        double sharpeLike,
        double sqn,
        StreakStats streaks)
    {
        var rc = audit.ReasonCounts;
        int C(string k) => rc.TryGetValue(k, out var v) ? v : 0;

        print("[L6BT_CSV_SUMMARY_V2]," +
              $"{Fmt1(_context.RewardRisk)}," +
              $"{_context.ProtectionAnchorMode}," +
              $"{_context.UseSwingCEdgeTakeProfit}," +
              $"{closedCount}," +
              $"{Fmt2(internalNetUsd)}," +
              $"{Fmt4(internalNetR)}," +
              $"{Fmt4(evR)}," +
              $"{Fmt2(evUsd)}," +
              $"{Fmt2(internalPf)}," +
              $"{PctPlain(winCount, n)}," +
              $"{Fmt4(avgWinR)}," +
              $"{Fmt4(avgLossR)}," +
              $"{Fmt4(payoff)}," +
              $"{Fmt2(maxDdUsd)}," +
              $"{Fmt2(maxDdPct)}," +
              $"{Fmt4(maxDdR)}," +
              $"{Fmt4(sharpeLike)}," +
              $"{Fmt4(sqn)}," +
              $"{streaks.MaxWins}," +
              $"{streaks.MaxLosses}," +
              $"{C("TakeProfit")}," +
              $"{C("StopLoss")}," +
              $"{C("BBrokenTpEntry")}," +
              $"{C("XRBarClose")}," +
              $"{C("XRBarCloseNextOpen")}," +
              $"{C("XRBarCloseActual")}," +
              $"{C("BBroken")}," +
              $"{C("SessionStop")}," +
              $"{C("ManualClose")}," +
              $"{C("Unknown")}," +
              $"{audit.PlanMissingCount}," +
              $"{audit.ProtectionTpMismatchCount}," +
              $"{audit.ProtectionSlMismatchCount}," +
              $"{Fmt2(audit.RawNetSum)}," +
              $"{Fmt2(audit.RawVsInternalNetDiffSum)}," +
              $"{audit.EnableVirtualXrBarCloseExit}," +
              $"{Fmt2(audit.VirtualXrTriggerR)}," +
              $"{audit.VirtualXrFillMode}," +
              $"{audit.XrBarCloseCount}," +
              $"{Fmt4(audit.XrBarCloseNetR)}," +
              $"{audit.XrTouchNoConfirmCount}");
    }

    void PrintStrategyScore(Action<string> print, in InternalStrategyAuditSnapshot audit, double evR, double internalPf, double maxDdPct)
    {
        var mismatch = Math.Max(0, Math.Abs(audit.CloseAuditCount - audit.HistoryClosedCount));
        var pfCap = Math.Min(internalPf, 3.0);
        var score = evR * 100
                    + pfCap * 10
                    - maxDdPct * 2
                    - audit.UnknownCloseCount * 100
                    - audit.PlanMissingCount * 100
                    - mismatch * 100;

        print("[L6BT_STRATEGY_SCORE] " +
              $"score={Fmt2(score)} " +
              $"formula=EVR*100+min(PF,3)*10-maxDrawdownPct*2-penalties " +
              $"evR={Fmt4(evR)} pf={Fmt2(internalPf)} maxDdPct={Fmt2(maxDdPct)} " +
              $"unknown={audit.UnknownCloseCount} planMissing={audit.PlanMissingCount} auditMismatch={mismatch}");
    }

    static IEnumerable<string> FormatEquityCsvLines(IReadOnlyList<InternalCloseRecord> records)
    {
        foreach (var r in records)
        {
            yield return "[L6BT_CSV_EQUITY]," +
                         $"{r.TradeIndex}," +
                         $"{r.CloseTime:O}," +
                         $"{r.Label}," +
                         $"{r.Rule}," +
                         $"{r.Direction}," +
                         $"{r.CloseReason}," +
                         $"{Fmt4(r.InternalNetR)}," +
                         $"{Fmt2(r.InternalNetUsd)}," +
                         $"{Fmt4(r.InternalEquityR)}," +
                         $"{Fmt2(r.InternalEquityUsd)}," +
                         $"{Fmt4(r.InternalDrawdownR)}," +
                         $"{Fmt2(r.InternalDrawdownPct)}";
        }
    }

    static IEnumerable<string> FormatRuleCsvLines(IReadOnlyList<InternalCloseRecord> records)
    {
        foreach (var rule in records.Select(r => r.Rule).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
        {
            var bucket = records.Where(r => r.Rule == rule).ToList();
            var netR = bucket.Sum(r => r.InternalNetR);
            var pf = ProfitFactorR(
                bucket.Select(r => r.InternalNetR).Where(x => x > BreakevenRTolerance),
                bucket.Select(r => r.InternalNetR).Where(x => x < -BreakevenRTolerance));
            var wins = bucket.Count(r => r.InternalNetR > BreakevenRTolerance);
            var winRs = bucket.Where(r => r.InternalNetR > BreakevenRTolerance).Select(r => r.InternalNetR).ToList();
            var lossRs = bucket.Where(r => r.InternalNetR < -BreakevenRTolerance).Select(r => r.InternalNetR).ToList();

            yield return "[L6BT_CSV_RULE]," +
                         $"{rule}," +
                         $"{bucket.Count}," +
                         $"{Fmt2(bucket.Sum(r => r.InternalNetUsd))}," +
                         $"{Fmt4(netR)}," +
                         $"{Fmt4(bucket.Count > 0 ? netR / bucket.Count : 0)}," +
                         $"{Fmt2(pf)}," +
                         $"{PctPlain(wins, bucket.Count)}," +
                         $"{Fmt4(Avg(winRs))}," +
                         $"{Fmt4(Avg(lossRs))}," +
                         $"{CountReason(bucket, "TakeProfit")}," +
                         $"{CountReason(bucket, "StopLoss")}," +
                         $"{CountReason(bucket, "BBrokenTpEntry")}," +
                         $"{CountReason(bucket, "BBroken")}";
        }
    }

    static IEnumerable<string> FormatMonthCsvLines(IReadOnlyList<InternalCloseRecord> records)
    {
        foreach (var month in records.GroupBy(r => r.CloseTime.ToString("yyyy-MM", CultureInfo.InvariantCulture)).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var bucket = month.ToList();
            var netR = bucket.Sum(r => r.InternalNetR);
            var pf = ProfitFactorR(
                bucket.Select(r => r.InternalNetR).Where(x => x > BreakevenRTolerance),
                bucket.Select(r => r.InternalNetR).Where(x => x < -BreakevenRTolerance));
            var wins = bucket.Count(r => r.InternalNetR > BreakevenRTolerance);
            var monthMaxDdR = ComputeLocalMaxDrawdownR(bucket);

            yield return "[L6BT_CSV_MONTH]," +
                         $"{month.Key}," +
                         $"{bucket.Count}," +
                         $"{Fmt2(bucket.Sum(r => r.InternalNetUsd))}," +
                         $"{Fmt4(netR)}," +
                         $"{Fmt4(bucket.Count > 0 ? netR / bucket.Count : 0)}," +
                         $"{Fmt2(pf)}," +
                         $"{PctPlain(wins, bucket.Count)}," +
                         $"{Fmt4(monthMaxDdR)}";
        }
    }

    static (double MaxDdUsd, double MaxDdPct, double MaxDdR, DateTime? Start, DateTime? End, string Duration) ComputeMaxDrawdown(
        List<InternalCloseRecord> records,
        double startEquityUsd)
    {
        if (records.Count == 0)
            return (0, 0, 0, null, null, "n/a");

        var equityUsd = startEquityUsd;
        var equityR = 0.0;
        var peakUsd = startEquityUsd;
        var peakR = 0.0;
        var maxDdUsd = 0.0;
        var maxDdPct = 0.0;
        var maxDdR = 0.0;
        DateTime? ddStart = null;
        DateTime? ddEnd = null;
        DateTime? peakTime = records[0].CloseTime;

        foreach (var r in records)
        {
            equityUsd += r.InternalNetUsd;
            equityR += r.InternalNetR;

            if (equityUsd >= peakUsd)
            {
                peakUsd = equityUsd;
                peakTime = r.CloseTime;
            }
            if (equityR >= peakR)
                peakR = equityR;

            var ddUsd = peakUsd - equityUsd;
            var ddPct = peakUsd > 0 ? ddUsd / peakUsd * 100.0 : 0;
            var ddR = peakR - equityR;

            if (ddUsd > maxDdUsd + 1e-9)
            {
                maxDdUsd = ddUsd;
                maxDdPct = ddPct;
                maxDdR = ddR;
                ddStart = peakTime;
                ddEnd = r.CloseTime;
            }
        }

        var duration = ddStart.HasValue && ddEnd.HasValue
            ? (ddEnd.Value - ddStart.Value).ToString(@"d\.hh\:mm\:ss", CultureInfo.InvariantCulture)
            : "n/a";

        return (maxDdUsd, maxDdPct, maxDdR, ddStart, ddEnd, duration);
    }

    static double ComputeLocalMaxDrawdownR(IEnumerable<InternalCloseRecord> records)
    {
        var equityR = 0.0;
        var peakR = 0.0;
        var maxDdR = 0.0;
        foreach (var r in records.OrderBy(x => x.CloseTime))
        {
            equityR += r.InternalNetR;
            if (equityR > peakR)
                peakR = equityR;
            maxDdR = Math.Max(maxDdR, peakR - equityR);
        }

        return maxDdR;
    }

    static StreakStats ComputeStreaks(IReadOnlyList<double> netRs)
    {
        var maxWins = 0;
        var maxLosses = 0;
        var curWins = 0;
        var curLosses = 0;
        var current = 0;
        var bestWinR = 0.0;
        var worstLossR = 0.0;
        var curWinR = 0.0;
        var curLossR = 0.0;

        foreach (var r in netRs)
        {
            if (r > BreakevenRTolerance)
            {
                curWins++;
                curLosses = 0;
                curWinR += r;
                curLossR = 0;
                current = curWins;
                if (curWins > maxWins)
                {
                    maxWins = curWins;
                    bestWinR = curWinR;
                }
                else if (curWins == maxWins)
                    bestWinR = Math.Max(bestWinR, curWinR);
            }
            else if (r < -BreakevenRTolerance)
            {
                curLosses++;
                curWins = 0;
                curLossR += r;
                curWinR = 0;
                current = -curLosses;
                if (curLosses > maxLosses)
                {
                    maxLosses = curLosses;
                    worstLossR = curLossR;
                }
                else if (curLosses == maxLosses)
                    worstLossR = Math.Min(worstLossR, curLossR);
            }
            else
            {
                curWins = 0;
                curLosses = 0;
                curWinR = 0;
                curLossR = 0;
                current = 0;
            }
        }

        return new StreakStats(maxWins, maxLosses, current, bestWinR, worstLossR);
    }

    static double ProfitFactor(IEnumerable<double> wins, IEnumerable<double> losses)
    {
        var w = wins.Sum();
        var l = losses.Sum();
        if (l >= 0 || w <= 0)
            return 0;
        return w / Math.Abs(l);
    }

    static double ProfitFactorR(IEnumerable<double> winRs, IEnumerable<double> lossRs)
    {
        var w = winRs.Sum();
        var l = lossRs.Sum();
        if (l >= 0 || w <= 0)
            return 0;
        return w / Math.Abs(l);
    }

    static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return 0;
        var sorted = values.OrderBy(x => x).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2.0
            : sorted[mid];
    }

    static double StdDev(IReadOnlyList<double> values)
    {
        if (values.Count <= 1)
            return 0;
        var mean = values.Average();
        var sumSq = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sumSq / values.Count);
    }

    static double Avg(IReadOnlyList<double> values) => values.Count > 0 ? values.Average() : 0;

    static int CountReason(IEnumerable<InternalCloseRecord> records, string reason) =>
        records.Count(r => r.CloseReason == reason);

    static IEnumerable<string> ReasonOrder() =>
        new[]
        {
            "TakeProfit", "StopLoss", "BBrokenTpEntry",
            "XRBarClose", "XRBarCloseNextOpen", "XRBarCloseActual",
            "BBroken", "SessionStop", "ManualClose", "Unknown",
        };

    static string MapReasonKey(string reason) => reason;

    static string DisplayReason(string reason) => reason switch
    {
        "TakeProfit" => "normalTakeProfit",
        _ => reason,
    };

    static string Fmt2(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    static string Fmt4(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
    static string Fmt1(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
    static string FmtTime(DateTime? t) => t.HasValue ? t.Value.ToString("O", CultureInfo.InvariantCulture) : "n/a";
    static string Pct(int part, int total) => total > 0 ? Fmt2(part * 100.0 / total) + "%" : "n/a";
    static string PctPlain(int part, int total) => total > 0 ? Fmt2(part * 100.0 / total) : "0";

    readonly record struct StreakStats(int MaxWins, int MaxLosses, int CurrentStreak, double BestWinStreakR, double WorstLossStreakR);
}

/// <summary>Parse rule slot token from L6BT label (e.g. L6BT|R3|B7080 => R3).</summary>
public static class LabelRuleParser
{
    public static string ParseRule(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return "Unknown";

        foreach (var part in label.Split('|'))
        {
            if (part.Length >= 2
                && part[0] == 'R'
                && char.IsDigit(part[1])
                && part.Skip(1).All(char.IsDigit))
                return part;
        }

        return "Unknown";
    }
}
