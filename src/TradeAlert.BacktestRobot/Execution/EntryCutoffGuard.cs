using TradeAlert.BacktestRobot.Execution.Analytics;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>
/// Blocks new entries after a Bangkok-time cutoff while the trading session is still open.
/// Open/pending orders keep running until session stop, news force-close, etc.
/// </summary>
public static class EntryCutoffGuard
{
    /// <summary>
    /// True when new entries must not be placed (Bangkok hour in [cutoff, sessionStop) when cutoff wraps midnight).
    /// </summary>
    public static bool IsEntryBlocked(
        DateTime utcNow,
        bool enabled,
        int cutoffHourBangkok,
        int sessionStopHourBangkok)
    {
        if (!enabled)
            return false;

        var bkkHour = Loop6BangkokSession.GetBangkokTime(utcNow).Hour;
        return IsInNoEntryBand(bkkHour, cutoffHourBangkok, sessionStopHourBangkok);
    }

    /// <summary>
    /// Cyclic no-entry band from cutoff through session stop (exclusive), e.g. 22:00–03:00 Bangkok.
    /// </summary>
    internal static bool IsInNoEntryBand(int bangkokHour, int cutoff, int sessionStop)
    {
        cutoff = NormalizeHour(cutoff);
        sessionStop = NormalizeHour(sessionStop);

        if (cutoff == sessionStop)
            return false;

        if (cutoff < sessionStop)
            return bangkokHour >= cutoff && bangkokHour < sessionStop;

        return bangkokHour >= cutoff || bangkokHour < sessionStop;
    }

    static int NormalizeHour(int hour) => ((hour % 24) + 24) % 24;
}
