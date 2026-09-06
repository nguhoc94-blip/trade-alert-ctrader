using System;
using System.Collections.Generic;
using System.Globalization;
using cAlgo.API;
using TradeAlert.BacktestRobot.Execution.Analytics;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>RR verification and execution audit logs for TradePlan / broker orders / positions.</summary>
public static class TradePlanRrLog
{
    public const double TpProfitTooLargeFactor = 1.5;

    public static double AppliedRr(double riskPips, double tpPips) =>
        riskPips > 0 ? tpPips / riskPips : 0;

    public static double PriceDistancePips(double from, double to, double pipSize) =>
        pipSize > 0 ? Math.Abs(to - from) / pipSize : 0;

    public static double ApproxUsdFromPips(double pips, double volumeInUnits, double pipValuePerLot, double lotVolumeInUnits)
    {
        if (pips <= 0 || volumeInUnits <= 0 || pipValuePerLot <= 0 || lotVolumeInUnits <= 0)
            return 0;
        return pips * pipValuePerLot * (volumeInUnits / lotVolumeInUnits);
    }

    public static double ActualRrFromPrices(double entry, double? stopLoss, double? takeProfit, bool isBuy)
    {
        if (stopLoss is not > 0 || takeProfit is not > 0)
            return 0;

        var risk = isBuy ? entry - stopLoss.Value : stopLoss.Value - entry;
        var reward = isBuy ? takeProfit.Value - entry : entry - takeProfit.Value;
        return risk > 0 ? reward / risk : 0;
    }

    public static string FormatPlanRrCheck(in TradePlan plan, int ruleSlot, double configuredRewardRisk)
    {
        var riskPips = plan.StopLossPips;
        var tpPips = plan.TakeProfitPips;
        var rr = AppliedRr(riskPips, tpPips);
        return $"[L6BT] PLAN RR CHECK rule=R{ruleSlot + 1} " +
               $"entry={plan.EntryLimit.ToString("0.#####", CultureInfo.InvariantCulture)} " +
               $"sl={plan.StopLoss.ToString("0.#####", CultureInfo.InvariantCulture)} " +
               $"tp={plan.TakeProfit.ToString("0.#####", CultureInfo.InvariantCulture)} " +
               $"riskPips={riskPips.ToString("0.#", CultureInfo.InvariantCulture)} " +
               $"tpPips={tpPips.ToString("0.#", CultureInfo.InvariantCulture)} " +
               $"rr={rr.ToString("0.##", CultureInfo.InvariantCulture)} " +
               $"cfgRR={configuredRewardRisk.ToString("0.#", CultureInfo.InvariantCulture)}";
    }

    public static IReadOnlyList<string> FormatPlanAbs(in TradePlan plan, double cfgRr, double plannedRiskUsd, double volumeUnits)
    {
        var dir = plan.IsBuy ? "BUY" : "SELL";
        var plannedRr = AppliedRr(plan.StopLossPips, plan.TakeProfitPips);
        return new[]
        {
            $"[L6BT] PLAN ABS label={plan.Label} dir={dir}",
            $"[L6BT]   preFillPlannedEntry={Fmt(plan.EntryLimit)} preFillPlannedSL={Fmt(plan.StopLoss)} preFillPlannedTP={Fmt(plan.TakeProfit)}",
            $"[L6BT]   plannedSlPips={Fmt1(plan.StopLossPips)} plannedTpPips={Fmt1(plan.TakeProfitPips)} plannedRR={Fmt2(plannedRr)} cfgRR={Fmt1(cfgRr)}",
            $"[L6BT]   plannedRiskUsd={Fmt2(plannedRiskUsd)} volumeUnits={Fmt0(volumeUnits)}",
        };
    }

    public static IReadOnlyList<string> FormatOrderAudit(
        PendingOrder order,
        PlannedAuditSnapshot? planned,
        in ExecutionAuditMoneyContext money)
    {
        var entry = order.TargetPrice;
        var sl = order.StopLoss;
        var tp = order.TakeProfit;
        var volume = order.VolumeInUnits;
        var slPips = sl is > 0 ? PriceDistancePips(entry, sl.Value, money.PipSize) : 0;
        var tpPips = tp is > 0 ? PriceDistancePips(entry, tp.Value, money.PipSize) : 0;
        var plannedRr = planned?.PlannedRr ?? 0;
        var riskUsd = sl is > 0 ? money.ApproxUsdFromPrices(entry, sl.Value, volume) : 0;
        var rewardUsd = tp is > 0 ? money.ApproxUsdFromPrices(entry, tp.Value, volume) : 0;

        return new[]
        {
            $"[L6BT] ORDER AUDIT label={order.Label} " +
            $"targetPrice={Fmt(entry)} stopLoss={FmtNullable(sl)} takeProfit={FmtNullable(tp)} volume={Fmt0(volume)} " +
            $"plannedRR={Fmt2(plannedRr)} actualTpPips={Fmt1(tpPips)} actualSlPips={Fmt1(slPips)} " +
            $"actualRewardUsdApprox={Fmt2(rewardUsd)} actualRiskUsdApprox={Fmt2(riskUsd)}",
        };
    }

    public static IReadOnlyList<string> FormatPositionAudit(
        Position position,
        PositionProtectionSnapshot? protection,
        in ExecutionAuditMoneyContext money)
    {
        var entry = position.EntryPrice;
        var sl = position.StopLoss;
        var tp = position.TakeProfit;
        var volume = position.VolumeInUnits;
        var isBuy = position.TradeType == TradeType.Buy;
        var actualRr = ActualRrFromPrices(entry, sl, tp, isBuy);
        var riskUsd = sl is > 0 ? money.ApproxUsdFromPrices(entry, sl.Value, volume) : 0;
        var rewardUsd = tp is > 0 ? money.ApproxUsdFromPrices(entry, tp.Value, volume) : 0;

        return new[]
        {
            $"[L6BT] POSITION AUDIT label={position.Label} " +
            $"entry={Fmt(entry)} stopLoss={FmtNullable(sl)} takeProfit={FmtNullable(tp)} volume={Fmt0(volume)} " +
            $"actualRiskUsdApprox={Fmt2(riskUsd)} actualRewardUsdApprox={Fmt2(rewardUsd)} actualRR={Fmt2(actualRr)}",
        };
    }

    public static string FormatCloseAudit(
        string label,
        string closeReason,
        string internalReason,
        bool planMissing,
        in PositionProtectionSnapshot protection,
        in ClosedTradeData data,
        in InternalCloseScore internalScore,
        double effectiveClose,
        in ExcursionSnapshot excursion,
        bool hasExcursion)
    {
        var rawGross = data.HasData ? data.GrossProfit : 0;
        var rawNet = data.HasData ? data.NetProfit : 0;
        var rawClose = data.HasData ? data.RawClose : 0;
        var commission = data.HasData ? data.Commission : 0;
        var anchorMode = protection.AnchorMode.ToString();
        var bBrokenPrice = protection.BBrokenMoveTpToEntryPrice is { } bp ? Fmt(bp) : "none";
        var mfeR = hasExcursion ? Fmt2(excursion.MfeR) : "n/a";
        var maeR = hasExcursion ? Fmt2(excursion.MaeR) : "n/a";
        var mfePips = hasExcursion ? Fmt1(excursion.MfePips) : "n/a";
        var maePips = hasExcursion ? Fmt1(excursion.MaePips) : "n/a";

        return $"[L6BT] CLOSE AUDIT label={label} reason={closeReason} internalReason={internalReason} anchorMode={anchorMode} " +
               $"planMissing={(planMissing ? "true" : "false")} " +
               $"bBrokenMoveTpToEntryApplied={protection.BBrokenMoveTpToEntryApplied} " +
               $"bBrokenMoveTpToEntryPrice={bBrokenPrice} " +
               $"actualEntry={Fmt(protection.Entry)} finalTP={Fmt(protection.TakeProfit)} finalSL={Fmt(protection.StopLoss)} " +
               $"effectiveClose={Fmt(effectiveClose)} rawClose={Fmt(rawClose)} rawGross={Fmt2(rawGross)} rawNet={Fmt2(rawNet)} " +
               $"internalGrossR={Fmt2(internalScore.InternalGrossR)} internalNetR={Fmt2(internalScore.InternalNetR)} " +
               $"internalGrossUsd={Fmt2(internalScore.InternalGrossUsd)} internalNetUsd={Fmt2(internalScore.InternalNetUsd)} " +
               $"mfeR={mfeR} maeR={maeR} mfePips={mfePips} maePips={maePips} " +
               $"commission={Fmt2(commission)} plannedRiskUsd={Fmt2(protection.PlannedRiskUsd)} plannedRR={Fmt2(protection.PlannedRr)} " +
               $"virtualXrExitEnabled={protection.VirtualXrExitEnabled} virtualXrTriggerR={Fmt2(protection.VirtualXrTriggerR)} " +
               $"virtualXrPrice={Fmt(protection.VirtualXrPrice)} virtualXrTouched={protection.VirtualXrTouched} " +
               $"virtualXrSignalBarTime={(protection.VirtualXrSignalBarTime.HasValue ? FmtTime(protection.VirtualXrSignalBarTime.Value) : "none")} " +
               $"virtualXrSignalBarClose={FmtNullablePrice(protection.VirtualXrSignalBarClose)} virtualXrEffectiveClose={FmtNullablePrice(protection.VirtualXrEffectiveClose)} " +
               $"rawVsInternalNetDiff={Fmt2(internalScore.RawVsInternalNetDiff)}";
    }

    public static string FormatCloseCsv(
        string label,
        string closeReason,
        string internalReason,
        in PositionProtectionSnapshot protection,
        in InternalCloseScore internalScore,
        double rawNet,
        in ExcursionSnapshot excursion,
        bool hasExcursion)
    {
        var mfeR = hasExcursion ? Fmt2(excursion.MfeR) : "";
        var maeR = hasExcursion ? Fmt2(excursion.MaeR) : "";
        return $"[L6BT_CSV_CLOSE],{label},{closeReason},{internalReason},{protection.AnchorMode}," +
               $"{protection.BBrokenMoveTpToEntryApplied},{protection.VirtualXrExitEnabled},{Fmt2(protection.VirtualXrTriggerR)},{Fmt(protection.VirtualXrPrice)}," +
               $"{FmtNullablePrice(protection.VirtualXrEffectiveClose)},{Fmt2(protection.PlannedRr)},{Fmt2(protection.PlannedRiskUsd)}," +
               $"{Fmt2(internalScore.InternalGrossR)},{Fmt2(internalScore.InternalNetR)},{Fmt2(internalScore.InternalNetUsd)}," +
               $"{Fmt2(rawNet)},{Fmt2(internalScore.RawVsInternalNetDiff)},{mfeR},{maeR}";
    }

    public static string FormatInternalSummaryCsv(
        int closedCount,
        int normalTakeProfitCount,
        int stopLossCount,
        int bBrokenTpEntryCount,
        int bBrokenCount,
        int sessionStopCount,
        int manualCloseCount,
        int unknownCount,
        double internalNetUsd,
        double internalNetR,
        double internalPf,
        double rawNet,
        double rawVsInternalNetDiff) =>
        $"[L6BT_CSV_SUMMARY],{closedCount},{normalTakeProfitCount},{stopLossCount},{bBrokenTpEntryCount},{bBrokenCount},{sessionStopCount},{manualCloseCount},{unknownCount}," +
        $"{Fmt2(internalNetUsd)},{Fmt2(internalNetR)},{Fmt2(internalPf)},{Fmt2(rawNet)},{Fmt2(rawVsInternalNetDiff)}";

    public static string FormatOptResultLine(
        double rewardRisk,
        bool enableVirtualXrBarCloseExit,
        double virtualXrTriggerR,
        string virtualXrFillMode,
        bool useSwingCEdgeTakeProfit,
        double internalEVR,
        double internalNetR,
        double internalNetUsd,
        double internalPF,
        double internalMaxDrawdownR,
        double internalMaxDrawdownPct,
        int closedCount,
        int unknownCloseCount,
        int planMissingCount,
        int protectionTpMismatchCount,
        int protectionSlMismatchCount) =>
        "[L6BT_OPT_RESULT]," +
        $"rewardRisk={Fmt2(rewardRisk)}," +
        $"enableVirtualXrBarCloseExit={enableVirtualXrBarCloseExit}," +
        $"virtualXrTriggerR={Fmt2(virtualXrTriggerR)}," +
        $"virtualXrFillMode={virtualXrFillMode}," +
        $"useSwingCEdgeTakeProfit={useSwingCEdgeTakeProfit}," +
        $"internalEVR={Fmt4(internalEVR)}," +
        $"internalNetR={Fmt4(internalNetR)}," +
        $"internalNetUsd={Fmt2(internalNetUsd)}," +
        $"internalPF={Fmt2(internalPF)}," +
        $"internalMaxDrawdownR={Fmt4(internalMaxDrawdownR)}," +
        $"internalMaxDrawdownPct={Fmt2(internalMaxDrawdownPct)}," +
        $"closedCount={closedCount}," +
        $"unknownCloseCount={unknownCloseCount}," +
        $"planMissingCount={planMissingCount}," +
        $"protectionTpMismatchCount={protectionTpMismatchCount}," +
        $"protectionSlMismatchCount={protectionSlMismatchCount}";

    public static string FormatBBrokenTpEntryMisclassifiedWarning(string label) =>
        $"[L6BT] WARNING BBROKEN TP-ENTRY MISCLASSIFIED label={label}";

    public static string FormatUnknownCloseWarning(string label) =>
        $"[L6BT] WARNING UNKNOWN CLOSE label={label}";

    public static string FormatCloseAuditCountMismatchWarning(int closeAuditCount, int closedCount) =>
        $"[L6BT] WARNING CLOSE AUDIT COUNT MISMATCH closeAuditCount={closeAuditCount} closedCount={closedCount}";

    public static string FormatUnknownClosesExistWarning(int unknownCloseCount) =>
        $"[L6BT] WARNING UNKNOWN CLOSES EXIST count={unknownCloseCount}";

    public static string FormatRawInternalNetDiffWarning(double rawNet, double internalNetUsd, double diff) =>
        $"[L6BT] WARNING RAW HISTORY NET DIFFERS FROM INTERNAL NET rawNet={Fmt2(rawNet)} internalNetUsd={Fmt2(internalNetUsd)} diff={Fmt2(diff)}";

    public static string FormatCloseAuditMissingWarning(int closeAuditCount, int historyClosedCount) =>
        $"[L6BT] WARNING CLOSE AUDIT MISSING count={historyClosedCount - closeAuditCount} " +
        $"closeAudit={closeAuditCount} historyClosed={historyClosedCount}";

    public static string FormatProtectionAudit(
        string label,
        double preFillPlannedEntry,
        double actualEntry,
        double expectedTp,
        double positionTp,
        double expectedSl,
        double positionSl,
        double tpMismatchPips,
        double slMismatchPips) =>
        $"[L6BT] PROTECTION AUDIT label={label} mode=ActualFillRelative " +
        $"preFillPlannedEntry={Fmt(preFillPlannedEntry)} actualEntry={Fmt(actualEntry)} " +
        $"expectedTP={Fmt(expectedTp)} positionTP={Fmt(positionTp)} " +
        $"expectedSL={Fmt(expectedSl)} positionSL={Fmt(positionSl)} " +
        $"tpMismatchPips={Fmt1(tpMismatchPips)} slMismatchPips={Fmt1(slMismatchPips)}";

    public static string FormatProtectionSync(
        string label,
        double oldSl,
        double oldTp,
        double newSl,
        double newTp,
        bool success) =>
        $"[L6BT] PROTECTION SYNC label={label} mode=PlannedAbsolute " +
        $"oldSL={Fmt(oldSl)} oldTP={Fmt(oldTp)} newSL={Fmt(newSl)} newTP={Fmt(newTp)} " +
        $"result={(success ? "ok" : "rejected")}";

    public static string FormatRrMismatchWarning(string label, double plannedRr, double actualRr) =>
        $"[L6BT] WARNING RR MISMATCH label={label} plannedRR={Fmt2(plannedRr)} actualRR={Fmt2(actualRr)}";

    public static string FormatTpProfitTooLargeWarning(string label, double net, double expectedMaxNet) =>
        $"[L6BT] WARNING TP PROFIT TOO LARGE label={label} net={Fmt2(net)} expectedMaxNet={Fmt2(expectedMaxNet)}";

    public static string FormatCloseReason(string reason) => MapCloseReason(reason);

    public static string MapCloseReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return "Unknown";

        var normalized = reason.Trim();
        return normalized switch
        {
            "TakeProfit" => "TakeProfit",
            "StopLoss" => "StopLoss",
            "session-stop" => "SessionStop",
            "1-Broken" => "BBroken",
            "1-Broken-tp-reject" => "BBroken",
            "flip" => "ManualClose",
            "Closed" => "ManualClose",
            "Manual" => "ManualClose",
            "split-leg-paired-close" => "SplitLegPairedClose",
            "XRBarClose" => "XRBarClose",
            "XRBarCloseNextOpen" => "XRBarCloseNextOpen",
            "XRBarCloseActual" => "XRBarCloseActual",
            _ when normalized.StartsWith("1-Broken", StringComparison.OrdinalIgnoreCase) => "BBroken",
            _ => "Unknown",
        };
    }

    static string Fmt(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
    static string Fmt0(double v) => v.ToString("0", CultureInfo.InvariantCulture);
    static string Fmt1(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
    static string Fmt2(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    static string Fmt4(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
    static string FmtNullable(double? v) => v is > 0 ? Fmt(v.Value) : "none";
    static string FmtNullablePrice(double? v) => v.HasValue ? Fmt(v.Value) : "none";
    static string FmtTime(DateTime t) => t.ToString("O", CultureInfo.InvariantCulture);
}
