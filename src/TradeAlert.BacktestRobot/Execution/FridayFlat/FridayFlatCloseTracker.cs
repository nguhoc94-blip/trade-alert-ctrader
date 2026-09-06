namespace TradeAlert.BacktestRobot.Execution.FridayFlat;

/// <summary>
/// Tracks which Friday has already been force-closed so the close executes at most once per week.
/// A "Friday key" is the ISO year-week number, ensuring a new key each calendar week.
/// </summary>
public sealed class FridayFlatCloseTracker
{
    // Key = "yyyy-Www" (ISO 8601 week). Set when force-close fires for that week.
    readonly HashSet<string> _firedWeeks = new(StringComparer.Ordinal);

    static string WeekKey(DateTime bangkokTime)
    {
        var cal = System.Globalization.CultureInfo.InvariantCulture.Calendar;
        var week = cal.GetWeekOfYear(
            bangkokTime,
            System.Globalization.CalendarWeekRule.FirstFourDayWeek,
            DayOfWeek.Monday);
        return $"{bangkokTime.Year}-W{week:D2}";
    }

    /// <summary>Returns true if force-close has not yet fired for the current calendar week.</summary>
    public bool ShouldFire(DateTime bangkokTime) =>
        !_firedWeeks.Contains(WeekKey(bangkokTime));

    /// <summary>Mark the current week's close as executed.</summary>
    public void MarkFired(DateTime bangkokTime) =>
        _firedWeeks.Add(WeekKey(bangkokTime));

    /// <summary>Reset (e.g. on bot restart) — each bot session starts fresh.</summary>
    public void Reset() => _firedWeeks.Clear();
}
