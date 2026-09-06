namespace TradeAlert.Core.Models.Alerts;

/// <summary>
/// Pine alert timing parity — phân loại tick realtime vs bar close (EVENT / PKL / M15 edge).
/// Pine: EVENT M5 = <c>currentTF=="5" &amp;&amp; m5_just_closed</c> (cạnh nến M5 mới);
/// TOUCH/REAL = mọi lần script chạy trên bar[0].
/// </summary>
public static class AlertBarTiming
{
    public static bool IsStateEveryTick(AlertTimingClass timing) =>
        timing is AlertTimingClass.RealtimeTouchBar0
            or AlertTimingClass.RealtimeFilterBar0
            or AlertTimingClass.RealtimeFilterBar0WithM15CloseEdge;

    public static bool IsBarCloseOnly(AlertTimingClass timing) =>
        timing is AlertTimingClass.BarCloseM5Event
            or AlertTimingClass.BarCloseM15Event
            or AlertTimingClass.BarCloseAnyTfPhaKhungLon;

    /// <summary>M5 event family — Pine <c>m5_just_closed</c> trên chart M5.</summary>
    public static bool IsM5JustClosedGate(string tfTok, bool isBarClosed, bool isNewBarOnRealtimeForming) =>
        tfTok == "5" && (isBarClosed || isNewBarOnRealtimeForming);

    /// <summary>M15 event family — Pine <c>m15_just_closed</c> trên chart M15 hoặc cạnh M15 trên M5.</summary>
    public static bool IsM15JustClosedGate(
        string tfTok,
        bool isBarClosed,
        bool isNewBarOnRealtimeForming,
        int barOpenMinute)
    {
        var onM15Boundary = barOpenMinute % 15 == 0;
        return tfTok switch
        {
            "15" => isBarClosed || isNewBarOnRealtimeForming,
            "5" => (isBarClosed || isNewBarOnRealtimeForming) && onM15Boundary,
            _ => false,
        };
    }

    /// <summary>Pine <c>m15CloseNow = ta.change(time("15")) != 0 and barstate.isnew</c>.</summary>
    public static bool ComputeM15CloseEdge(
        string tfTok,
        bool isBarClosed,
        bool isRealtime,
        bool isNewBarOnRealtimeForming,
        int barOpenMinute,
        bool hostInjected)
    {
        if (hostInjected)
            return true;

        var onM15Boundary = barOpenMinute % 15 == 0;
        if (isRealtime)
        {
            return isNewBarOnRealtimeForming
                   && (tfTok == "15" || (tfTok == "5" && onM15Boundary));
        }

        return isBarClosed && (tfTok == "15" || (tfTok == "5" && onM15Boundary));
    }

    /// <summary>PKL / generic confirmed bar — Pine <c>isConfirmed</c>.</summary>
    public static bool IsConfirmedBarGate(bool isBarClosed, bool isNewBarOnRealtimeForming) =>
        isBarClosed || isNewBarOnRealtimeForming;

    public static bool ShouldEvaluateOnPass(
        AlertTimingClass timing,
        bool passIsBarCloseEval,
        bool passIsRealtimeStateEval,
        bool passIsEventFireTouchEval = false)
    {
        if (passIsEventFireTouchEval && timing is AlertTimingClass.RealtimeTouchBar0)
            return true;
        if (passIsRealtimeStateEval && IsStateEveryTick(timing))
            return true;
        if (passIsBarCloseEval && IsBarCloseOnly(timing))
            return true;
        return false;
    }
}
