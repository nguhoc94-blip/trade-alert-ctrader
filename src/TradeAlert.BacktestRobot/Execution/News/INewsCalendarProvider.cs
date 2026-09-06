namespace TradeAlert.BacktestRobot.Execution.News;

/// <summary>Abstraction for fetching economic calendar events from an external source.</summary>
public interface INewsCalendarProvider
{
    /// <summary>
    /// Fetch high-impact news events for the configured currencies and lookahead period.
    /// Must never throw — returns empty list and sets <paramref name="error"/> on failure.
    /// </summary>
    IReadOnlyList<NewsEvent> Fetch(NewsFilterConfig cfg, DateTime utcNow, out string error);
}
