using System;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>Internal R-based PnL for a single closed trade (optimization metric).</summary>
public readonly struct InternalCloseScore
{
    public string CloseReason { get; init; }
    public double InternalGrossR { get; init; }
    public double InternalGrossUsd { get; init; }
    public double InternalNetUsd { get; init; }
    public double InternalNetR { get; init; }
    public double RawVsInternalNetDiff { get; init; }
}

/// <summary>
/// Computes internal R-scores from planned risk/RR and final protection — never inflated by
/// cTrader TP fill overshoot when classified as TakeProfit.
/// </summary>
public static class InternalRScoreCalculator
{
    public static InternalCloseScore Compute(
        in PositionProtectionSnapshot protection,
        in ClosedTradeData data,
        string closeReason,
        in InternalPerformanceConfig config,
        double pipSize)
    {
        var rawNet = data.HasData ? data.NetProfit : 0;
        var commission = data.HasData ? data.Commission : 0;
        var rawClose = data.HasData ? data.RawClose : 0;
        var rawGross = data.HasData ? data.GrossProfit : 0;
        var riskUsd = protection.PlannedRiskUsd;

        var (internalGrossR, internalGrossUsd) = closeReason switch
        {
            "TakeProfit" => ComputeTakeProfit(in protection, in config, pipSize),
            "StopLoss" => ComputeStopLoss(in protection, in config, pipSize),
            "BBrokenTpEntry" => ComputeBBrokenTpEntry(in protection, rawClose),
            "XRBarClose" or "XRBarCloseNextOpen" or "XRBarCloseActual" =>
                ComputeFromEffectiveClose(in protection, protection.VirtualXrEffectiveClose ?? rawClose),
            _ => ComputeFromActualClose(in protection, rawClose, rawGross),
        };

        var internalNetUsd = internalGrossUsd + commission;
        var internalNetR = riskUsd > 0 ? internalNetUsd / riskUsd : 0;

        return new InternalCloseScore
        {
            CloseReason = closeReason,
            InternalGrossR = internalGrossR,
            InternalGrossUsd = internalGrossUsd,
            InternalNetUsd = internalNetUsd,
            InternalNetR = internalNetR,
            RawVsInternalNetDiff = rawNet - internalNetUsd,
        };
    }

    static (double GrossR, double GrossUsd) ComputeTakeProfit(
        in PositionProtectionSnapshot protection,
        in InternalPerformanceConfig config,
        double pipSize)
    {
        if (config.InternalTpSlippagePips <= 0)
            return (protection.PlannedRr, protection.PlannedRiskUsd * protection.PlannedRr);

        var effectiveTp = EffectiveTakeProfit(protection.TakeProfit, config.InternalTpSlippagePips, pipSize, protection.IsBuy);
        var grossUsd = ExecutionPnLCalculator.SignedGross(
            protection.Entry, effectiveTp, protection.VolumeUnits, protection.IsBuy);
        var grossR = protection.PlannedRiskUsd > 0 ? grossUsd / protection.PlannedRiskUsd : 0;
        return (grossR, grossUsd);
    }

    static (double GrossR, double GrossUsd) ComputeStopLoss(
        in PositionProtectionSnapshot protection,
        in InternalPerformanceConfig config,
        double pipSize)
    {
        if (config.InternalSlSlippagePips <= 0)
            return (-1.0, -protection.PlannedRiskUsd);

        var effectiveSl = EffectiveStopLoss(protection.StopLoss, config.InternalSlSlippagePips, pipSize, protection.IsBuy);
        var grossUsd = ExecutionPnLCalculator.SignedGross(
            protection.Entry, effectiveSl, protection.VolumeUnits, protection.IsBuy);
        var grossR = protection.PlannedRiskUsd > 0 ? grossUsd / protection.PlannedRiskUsd : 0;
        return (grossR, grossUsd);
    }

    static (double GrossR, double GrossUsd) ComputeFromEffectiveClose(
        in PositionProtectionSnapshot protection,
        double effectiveClose)
    {
        var grossUsd = ExecutionPnLCalculator.SignedGross(
            protection.Entry, effectiveClose, protection.VolumeUnits, protection.IsBuy);
        var grossR = protection.PlannedRiskUsd > 0 ? grossUsd / protection.PlannedRiskUsd : 0;
        return (grossR, grossUsd);
    }

    static (double GrossR, double GrossUsd) ComputeBBrokenTpEntry(
        in PositionProtectionSnapshot protection,
        double rawClose)
    {
        var effectiveClose = protection.TakeProfit > 0 ? protection.TakeProfit : rawClose;
        var grossUsd = ExecutionPnLCalculator.SignedGross(
            protection.Entry, effectiveClose, protection.VolumeUnits, protection.IsBuy);
        var grossR = protection.PlannedRiskUsd > 0 ? grossUsd / protection.PlannedRiskUsd : 0;
        return (grossR, grossUsd);
    }

    static (double GrossR, double GrossUsd) ComputeFromActualClose(
        in PositionProtectionSnapshot protection,
        double rawClose,
        double rawGross)
    {
        var grossUsd = rawClose > 0 && protection.VolumeUnits > 0
            ? ExecutionPnLCalculator.SignedGross(protection.Entry, rawClose, protection.VolumeUnits, protection.IsBuy)
            : rawGross;
        var grossR = protection.PlannedRiskUsd > 0 ? grossUsd / protection.PlannedRiskUsd : 0;
        return (grossR, grossUsd);
    }

    /// <summary>Unfavorable TP slippage: BUY closes lower, SELL closes higher.</summary>
    public static double EffectiveTakeProfit(double finalTp, double slippagePips, double pipSize, bool isBuy)
    {
        if (finalTp <= 0 || slippagePips <= 0 || pipSize <= 0)
            return finalTp;

        var slip = slippagePips * pipSize;
        return isBuy ? finalTp - slip : finalTp + slip;
    }

    /// <summary>Unfavorable SL slippage: BUY stops lower, SELL stops higher.</summary>
    public static double EffectiveStopLoss(double finalSl, double slippagePips, double pipSize, bool isBuy)
    {
        if (finalSl <= 0 || slippagePips <= 0 || pipSize <= 0)
            return finalSl;

        var slip = slippagePips * pipSize;
        return isBuy ? finalSl - slip : finalSl + slip;
    }
}
