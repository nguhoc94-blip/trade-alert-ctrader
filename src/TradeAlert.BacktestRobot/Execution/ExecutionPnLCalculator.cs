using System;
using cAlgo.API;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>Final broker numbers for a closed trade (from HistoricalTrade or closed Position).</summary>
public readonly struct ClosedTradeData
{
    public bool HasData { get; init; }
    public double RawClose { get; init; }
    public double GrossProfit { get; init; }
    public double Commission { get; init; }
    public double NetProfit { get; init; }
    public DateTime CloseTime { get; init; }

    public static ClosedTradeData Empty => new();
}

/// <summary>Raw vs clamped PnL for a closed trade (guards against cTrader TP/SL fill overshoot).</summary>
public readonly struct CloseAuditPnL
{
    public double RawClose { get; init; }
    public double ClampedClose { get; init; }
    public double RawGross { get; init; }
    public double ClampedGross { get; init; }
    public double Commission { get; init; }
    public double RawNet { get; init; }
    public double ClampedNet { get; init; }
    public double RawR { get; init; }
    public double ClampedR { get; init; }
    public bool HasTpOvershoot { get; init; }
    public double TpOvershootPips { get; init; }
}

/// <summary>Computes effective close prices and clamped PnL for execution audit.</summary>
public static class ExecutionPnLCalculator
{
    public static double EffectiveClose(string closeReason, double rawClose, double stopLoss, double takeProfit) =>
        closeReason switch
        {
            "TakeProfit" when takeProfit > 0 => takeProfit,
            "StopLoss" when stopLoss > 0 => stopLoss,
            _ => rawClose,
        };

    public static bool HasTpOvershoot(string closeReason, double rawClose, double takeProfit, bool isBuy)
    {
        if (!string.Equals(closeReason, "TakeProfit", StringComparison.Ordinal) || takeProfit <= 0)
            return false;
        return isBuy ? rawClose > takeProfit + 1e-9 : rawClose < takeProfit - 1e-9;
    }

    public static double TpOvershootPips(double rawClose, double takeProfit, double pipSize, bool isBuy)
    {
        if (pipSize <= 0 || takeProfit <= 0)
            return 0;
        if (isBuy && rawClose <= takeProfit)
            return 0;
        if (!isBuy && rawClose >= takeProfit)
            return 0;
        return Math.Abs(rawClose - takeProfit) / pipSize;
    }

    public static CloseAuditPnL ComputeCloseAudit(
        in PositionProtectionSnapshot protection,
        double rawClose,
        double rawGross,
        double commission,
        double rawNet,
        string closeReason,
        double pipSize)
    {
        var clampedClose = EffectiveClose(closeReason, rawClose, protection.StopLoss, protection.TakeProfit);
        var clampedGross = SignedGross(protection.Entry, clampedClose, protection.VolumeUnits, protection.IsBuy);
        var clampedNet = clampedGross + commission;
        var rawR = protection.PlannedRiskUsd > 0 ? rawNet / protection.PlannedRiskUsd : 0;
        var clampedR = protection.PlannedRiskUsd > 0 ? clampedNet / protection.PlannedRiskUsd : 0;
        var overshoot = HasTpOvershoot(closeReason, rawClose, protection.TakeProfit, protection.IsBuy);

        return new CloseAuditPnL
        {
            RawClose = rawClose,
            ClampedClose = clampedClose,
            RawGross = rawGross,
            ClampedGross = clampedGross,
            Commission = commission,
            RawNet = rawNet,
            ClampedNet = clampedNet,
            RawR = rawR,
            ClampedR = clampedR,
            HasTpOvershoot = overshoot,
            TpOvershootPips = overshoot
                ? TpOvershootPips(rawClose, protection.TakeProfit, pipSize, protection.IsBuy)
                : 0,
        };
    }

    public static PositionProtectionSnapshot FromPlanned(PlannedAuditSnapshot planned, ProtectionAnchorMode anchorMode = ProtectionAnchorMode.ActualFillRelative) => new()
    {
        Label = planned.Label,
        IsBuy = planned.IsBuy,
        Entry = planned.PlannedEntry,
        StopLoss = planned.PlannedSl,
        TakeProfit = planned.PlannedTp,
        VolumeUnits = planned.VolumeUnits,
        PlannedRiskUsd = planned.PlannedRiskUsd,
        PlannedRr = planned.PlannedRr,
        AnchorMode = anchorMode,
        PreFillPlannedEntry = planned.PlannedEntry,
        PreFillPlannedSl = planned.PlannedSl,
        PreFillPlannedTp = planned.PlannedTp,
        PlannedSlPips = planned.PlannedSlPips,
        PlannedTpPips = planned.PlannedTpPips,
    };

    public static PositionProtectionSnapshot FromPosition(
        Position position,
        PlannedAuditSnapshot? planned,
        ProtectionAnchorMode anchorMode) => new()
    {
        Label = position.Label ?? planned?.Label ?? "",
        IsBuy = position.TradeType == TradeType.Buy,
        Entry = position.EntryPrice,
        StopLoss = position.StopLoss ?? planned?.PlannedSl ?? 0,
        TakeProfit = position.TakeProfit ?? planned?.PlannedTp ?? 0,
        VolumeUnits = position.VolumeInUnits,
        PlannedRiskUsd = planned?.PlannedRiskUsd ?? 0,
        PlannedRr = planned?.PlannedRr ?? 0,
        AnchorMode = anchorMode,
        PreFillPlannedEntry = planned?.PlannedEntry ?? 0,
        PreFillPlannedSl = planned?.PlannedSl ?? 0,
        PreFillPlannedTp = planned?.PlannedTp ?? 0,
        PlannedSlPips = planned?.PlannedSlPips ?? 0,
        PlannedTpPips = planned?.PlannedTpPips ?? 0,
    };

    public static PositionProtectionSnapshot FromPending(
        PendingOrder order,
        PlannedAuditSnapshot planned,
        ProtectionAnchorMode anchorMode) => new()
    {
        Label = order.Label ?? planned.Label,
        IsBuy = order.TradeType == TradeType.Buy,
        Entry = order.TargetPrice,
        StopLoss = order.StopLoss ?? planned.PlannedSl,
        TakeProfit = order.TakeProfit ?? 0,
        VolumeUnits = order.VolumeInUnits,
        PlannedRiskUsd = planned.PlannedRiskUsd,
        PlannedRr = planned.PlannedRr,
        AnchorMode = anchorMode,
        PreFillPlannedEntry = planned.PlannedEntry,
        PreFillPlannedSl = planned.PlannedSl,
        PreFillPlannedTp = planned.PlannedTp,
        PlannedSlPips = planned.PlannedSlPips,
        PlannedTpPips = planned.PlannedTpPips,
    };

    public static double SignedGross(double entry, double closePrice, double volumeUnits, bool isBuy)
    {
        if (volumeUnits <= 0)
            return 0;
        var move = isBuy ? closePrice - entry : entry - closePrice;
        return move * volumeUnits;
    }

    /// <summary>
    /// cTrader applies pip-based protection relative to the actual fill price. Given the real
    /// fill entry and the planned pip distances, this returns the SL/TP the broker will set.
    /// </summary>
    public static (double ExpectedSl, double ExpectedTp) ExpectedActualProtection(
        double fillEntry,
        double plannedSlPips,
        double plannedTpPips,
        double pipSize,
        bool isBuy)
    {
        var slDist = plannedSlPips * pipSize;
        var tpDist = plannedTpPips * pipSize;
        return isBuy
            ? (fillEntry - slDist, fillEntry + tpDist)
            : (fillEntry + slDist, fillEntry - tpDist);
    }

    public static double ApproxUsdFromPriceDistance(
        double entry,
        double targetPrice,
        double volumeInUnits,
        in ExecutionAuditMoneyContext money)
    {
        var distance = Math.Abs(targetPrice - entry);
        if (money.UsePriceDistanceTimesVolume)
            return distance * volumeInUnits;

        var pips = money.PipSize > 0 ? distance / money.PipSize : 0;
        return TradePlanRrLog.ApproxUsdFromPips(pips, volumeInUnits, money.PipValuePerLot, money.LotVolumeInUnits);
    }
}

public sealed class PositionProtectionSnapshot
{
    public string Label { get; init; } = "";
    public bool IsBuy { get; init; }
    public double Entry { get; init; }
    public double StopLoss { get; init; }
    public double TakeProfit { get; init; }
    public double VolumeUnits { get; init; }
    public double PlannedRiskUsd { get; init; }
    public double PlannedRr { get; init; }
    public ProtectionAnchorMode AnchorMode { get; init; } = ProtectionAnchorMode.ActualFillRelative;
    public double PreFillPlannedEntry { get; init; }
    public double PreFillPlannedSl { get; init; }
    public double PreFillPlannedTp { get; init; }
    public double PlannedSlPips { get; init; }
    public double PlannedTpPips { get; init; }
    public bool BBrokenMoveTpToEntryApplied { get; init; }
    public double? BBrokenMoveTpToEntryPrice { get; init; }
    public DateTime? BBrokenMoveTpToEntryTime { get; init; }
    public double? BBrokenOriginalTpBeforeMove { get; init; }
    public double? BBrokenOriginalSlAtMove { get; init; }
    public bool VirtualXrExitEnabled { get; init; }
    public bool VirtualXrBrokerTpEnabled { get; init; }
    public double VirtualXrTriggerR { get; init; }
    public double VirtualXrPrice { get; init; }
    public bool VirtualXrTouched { get; init; }
    public DateTime? VirtualXrSignalBarTime { get; init; }
    public double? VirtualXrSignalBarClose { get; init; }
    public double? VirtualXrEffectiveClose { get; init; }
    public bool VirtualXrExitTriggered { get; init; }
    public VirtualXrFillMode VirtualXrFillModeValue { get; init; }

    public PositionProtectionSnapshot WithTakeProfit(double newTakeProfit) => CopyWith(takeProfit: newTakeProfit);

    public PositionProtectionSnapshot WithBBrokenMoveTpToEntry(
        double newTakeProfit,
        double originalTp,
        double stopLossAtMove,
        DateTime moveTime) => CopyWith(
        takeProfit: newTakeProfit,
        bBrokenMoveTpToEntryApplied: true,
        bBrokenMoveTpToEntryPrice: newTakeProfit,
        bBrokenMoveTpToEntryTime: moveTime,
        bBrokenOriginalTpBeforeMove: originalTp,
        bBrokenOriginalSlAtMove: stopLossAtMove);

    public PositionProtectionSnapshot WithVirtualXrPlan(
        bool enabled,
        bool brokerTpEnabled,
        double triggerR,
        double virtualXrPrice,
        VirtualXrFillMode fillMode) => CopyWith(
        virtualXrExitEnabled: enabled,
        virtualXrBrokerTpEnabled: brokerTpEnabled,
        virtualXrTriggerR: triggerR,
        virtualXrPrice: virtualXrPrice,
        virtualXrFillModeValue: fillMode);

    public PositionProtectionSnapshot WithVirtualXrTouched(bool touched) => CopyWith(virtualXrTouched: touched);

    public PositionProtectionSnapshot WithVirtualXrExit(
        double effectiveClose,
        DateTime signalBarTime,
        double signalBarClose,
        bool touched) => CopyWith(
        virtualXrTouched: touched,
        virtualXrSignalBarTime: signalBarTime,
        virtualXrSignalBarClose: signalBarClose,
        virtualXrEffectiveClose: effectiveClose,
        virtualXrExitTriggered: true);

    public PositionProtectionSnapshot PreserveExitMetadata(in PositionProtectionSnapshot source) => CopyWith(
        bBrokenMoveTpToEntryApplied: source.BBrokenMoveTpToEntryApplied,
        bBrokenMoveTpToEntryPrice: source.BBrokenMoveTpToEntryPrice,
        bBrokenMoveTpToEntryTime: source.BBrokenMoveTpToEntryTime,
        bBrokenOriginalTpBeforeMove: source.BBrokenOriginalTpBeforeMove,
        bBrokenOriginalSlAtMove: source.BBrokenOriginalSlAtMove,
        virtualXrExitEnabled: source.VirtualXrExitEnabled,
        virtualXrBrokerTpEnabled: source.VirtualXrBrokerTpEnabled,
        virtualXrTriggerR: source.VirtualXrTriggerR,
        virtualXrPrice: source.VirtualXrPrice,
        virtualXrTouched: source.VirtualXrTouched,
        virtualXrSignalBarTime: source.VirtualXrSignalBarTime,
        virtualXrSignalBarClose: source.VirtualXrSignalBarClose,
        virtualXrEffectiveClose: source.VirtualXrEffectiveClose,
        virtualXrExitTriggered: source.VirtualXrExitTriggered,
        virtualXrFillModeValue: source.VirtualXrFillModeValue);

    PositionProtectionSnapshot CopyWith(
        double? takeProfit = null,
        bool? bBrokenMoveTpToEntryApplied = null,
        double? bBrokenMoveTpToEntryPrice = null,
        DateTime? bBrokenMoveTpToEntryTime = null,
        double? bBrokenOriginalTpBeforeMove = null,
        double? bBrokenOriginalSlAtMove = null,
        bool? virtualXrExitEnabled = null,
        bool? virtualXrBrokerTpEnabled = null,
        double? virtualXrTriggerR = null,
        double? virtualXrPrice = null,
        bool? virtualXrTouched = null,
        DateTime? virtualXrSignalBarTime = null,
        double? virtualXrSignalBarClose = null,
        double? virtualXrEffectiveClose = null,
        bool? virtualXrExitTriggered = null,
        VirtualXrFillMode? virtualXrFillModeValue = null) => new()
    {
        Label = Label,
        IsBuy = IsBuy,
        Entry = Entry,
        StopLoss = StopLoss,
        TakeProfit = takeProfit ?? TakeProfit,
        VolumeUnits = VolumeUnits,
        PlannedRiskUsd = PlannedRiskUsd,
        PlannedRr = PlannedRr,
        AnchorMode = AnchorMode,
        PreFillPlannedEntry = PreFillPlannedEntry,
        PreFillPlannedSl = PreFillPlannedSl,
        PreFillPlannedTp = PreFillPlannedTp,
        PlannedSlPips = PlannedSlPips,
        PlannedTpPips = PlannedTpPips,
        BBrokenMoveTpToEntryApplied = bBrokenMoveTpToEntryApplied ?? BBrokenMoveTpToEntryApplied,
        BBrokenMoveTpToEntryPrice = bBrokenMoveTpToEntryPrice ?? BBrokenMoveTpToEntryPrice,
        BBrokenMoveTpToEntryTime = bBrokenMoveTpToEntryTime ?? BBrokenMoveTpToEntryTime,
        BBrokenOriginalTpBeforeMove = bBrokenOriginalTpBeforeMove ?? BBrokenOriginalTpBeforeMove,
        BBrokenOriginalSlAtMove = bBrokenOriginalSlAtMove ?? BBrokenOriginalSlAtMove,
        VirtualXrExitEnabled = virtualXrExitEnabled ?? VirtualXrExitEnabled,
        VirtualXrBrokerTpEnabled = virtualXrBrokerTpEnabled ?? VirtualXrBrokerTpEnabled,
        VirtualXrTriggerR = virtualXrTriggerR ?? VirtualXrTriggerR,
        VirtualXrPrice = virtualXrPrice ?? VirtualXrPrice,
        VirtualXrTouched = virtualXrTouched ?? VirtualXrTouched,
        VirtualXrSignalBarTime = virtualXrSignalBarTime ?? VirtualXrSignalBarTime,
        VirtualXrSignalBarClose = virtualXrSignalBarClose ?? VirtualXrSignalBarClose,
        VirtualXrEffectiveClose = virtualXrEffectiveClose ?? VirtualXrEffectiveClose,
        VirtualXrExitTriggered = virtualXrExitTriggered ?? VirtualXrExitTriggered,
        VirtualXrFillModeValue = virtualXrFillModeValue ?? VirtualXrFillModeValue,
    };
}
