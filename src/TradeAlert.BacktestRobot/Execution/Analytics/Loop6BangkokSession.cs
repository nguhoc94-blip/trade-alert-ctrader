namespace TradeAlert.BacktestRobot.Execution.Analytics;

/// <summary>Bangkok (UTC+7) session buckets for edge analysis.</summary>
public static class Loop6BangkokSession
{
    public const string Asia1 = "Asia_1";
    public const string Asia2PreLondon = "Asia_2_PreLondon";
    public const string LondonOpen = "London_Open";
    public const string LondonNyOverlap = "London_NY_Overlap";
    public const string NyMid = "NY_Mid";
    public const string LateNy = "Late_NY";
    public const string BlockedOutside = "Blocked/Outside";

    static readonly TimeSpan BangkokOffset = TimeSpan.FromHours(7);

    public static DateTime GetBangkokTime(DateTime serverTimeUtc) =>
        serverTimeUtc.Kind == DateTimeKind.Utc
            ? serverTimeUtc + BangkokOffset
            : serverTimeUtc.ToUniversalTime() + BangkokOffset;

    public static int GetBangkokHour(DateTime bangkokTime) => bangkokTime.Hour;

    /// <summary>Maps Bangkok local time to named session (06:00–03:00 trading window per spec).</summary>
    public static string GetBangkokSession(DateTime bangkokTime)
    {
        var h = bangkokTime.Hour;
        return h switch
        {
            >= 6 and <= 11 => Asia1,
            >= 12 and <= 13 => Asia2PreLondon,
            >= 14 and <= 17 => LondonOpen,
            >= 18 and <= 21 => LondonNyOverlap,
            >= 22 and <= 23 => NyMid,
            >= 0 and <= 2 => LateNy,
            _ => BlockedOutside,
        };
    }
}
