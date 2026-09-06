namespace TradeAlert.BacktestRobot.Execution.FridayFlat;

/// <summary>
/// Immutable config for the Friday Flat Guard.
/// Parse once in InitFridayFlatGuard() and pass to <see cref="FridayFlatGuardService"/>.
/// </summary>
public sealed class FridayFlatGuardConfig
{
    public bool   EnableFridayFlatGuard          { get; init; }
    public TimeSpan FridayFlatTime               { get; init; }   // Bangkok local ToD
    public TimeSpan FridayBlockNewEntriesTime    { get; init; }   // Bangkok local ToD
    public TimeSpan MondayResumeTime             { get; init; }   // Bangkok local ToD

    /// <summary>Parse time-of-day string "HH:mm". Returns <see cref="TimeSpan.Zero"/> on parse failure.</summary>
    public static TimeSpan ParseTime(string s)
    {
        if (TimeSpan.TryParseExact(s?.Trim() ?? "", @"hh\:mm", null, out var t))
            return t;
        if (TimeSpan.TryParse(s?.Trim() ?? "", out var t2))
            return t2;
        return TimeSpan.Zero;
    }

    public static FridayFlatGuardConfig Build(
        bool   enableFridayFlatGuard,
        string fridayFlatTimeBangkok,
        string fridayBlockNewEntriesTimeBangkok,
        string mondayResumeTimeBangkok) =>
        new()
        {
            EnableFridayFlatGuard       = enableFridayFlatGuard,
            FridayFlatTime              = ParseTime(fridayFlatTimeBangkok),
            FridayBlockNewEntriesTime   = ParseTime(fridayBlockNewEntriesTimeBangkok),
            MondayResumeTime            = ParseTime(mondayResumeTimeBangkok),
        };
}
