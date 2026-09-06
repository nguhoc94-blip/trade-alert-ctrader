using System;



namespace TradeAlert.BacktestRobot.Execution;



/// <summary>

/// Classifies closed trades using final protection levels and handler-recorded reasons.

/// Virtual xR bar-close exits use handler reason and effectiveClose, not broker TP or plannedRR.

/// </summary>

public static class InternalCloseReasonClassifier

{

    public static string Classify(

        double rawClose,

        in PositionProtectionSnapshot protection,

        double tolerancePips,

        double pipSize,

        string? handlerReason)

    {

        if (rawClose > 0 && IsAtOrBeyondStopLoss(rawClose, in protection, tolerancePips, pipSize))

            return "StopLoss";



        if (protection.BBrokenMoveTpToEntryApplied)

            return "BBrokenTpEntry";



        var mapped = TradePlanRrLog.MapCloseReason(handlerReason);

        if (IsVirtualXrReason(mapped))

            return mapped;



        if (ShouldAllowBrokerTakeProfit(in protection)

            && rawClose > 0

            && IsAtOrBeyondTakeProfit(rawClose, in protection, tolerancePips, pipSize))

            return "TakeProfit";



        if (mapped is "BBroken" or "SessionStop" or "ManualClose" or "SplitLegPairedClose")

            return mapped;



        return "Unknown";

    }



    public static double ResolveEffectiveClose(

        string closeReason,

        in PositionProtectionSnapshot protection,

        double rawClose)

    {

        if (IsVirtualXrReason(closeReason))

            return protection.VirtualXrEffectiveClose ?? rawClose;



        if (closeReason == "BBrokenTpEntry")

            return protection.TakeProfit > 0 ? protection.TakeProfit : rawClose;



        if (closeReason == "TakeProfit" && protection.TakeProfit > 0)

            return protection.TakeProfit;



        if (closeReason == "StopLoss" && protection.StopLoss > 0)

            return protection.StopLoss;



        return rawClose;

    }



    static bool ShouldAllowBrokerTakeProfit(in PositionProtectionSnapshot protection) =>

        !protection.VirtualXrExitEnabled || protection.VirtualXrBrokerTpEnabled;



    static bool IsVirtualXrReason(string reason) =>

        reason is "XRBarClose" or "XRBarCloseNextOpen" or "XRBarCloseActual";



    static bool IsAtOrBeyondTakeProfit(

        double rawClose,

        in PositionProtectionSnapshot protection,

        double tolerancePips,

        double pipSize)

    {

        if (protection.TakeProfit <= 0 || pipSize <= 0)

            return false;



        var tolerance = tolerancePips * pipSize;

        return protection.IsBuy

            ? rawClose >= protection.TakeProfit - tolerance

            : rawClose <= protection.TakeProfit + tolerance;

    }



    static bool IsAtOrBeyondStopLoss(

        double rawClose,

        in PositionProtectionSnapshot protection,

        double tolerancePips,

        double pipSize)

    {

        if (protection.StopLoss <= 0 || pipSize <= 0)

            return false;



        var tolerance = tolerancePips * pipSize;

        return protection.IsBuy

            ? rawClose <= protection.StopLoss + tolerance

            : rawClose >= protection.StopLoss - tolerance;

    }

}


