namespace TradeAlert.BacktestRobot.Execution.News;

/// <summary>Result of a news filter check — whether to block entry or force-close.</summary>
public sealed class NewsBlockDecision
{
    public static readonly NewsBlockDecision NotBlocked = new() { IsBlocked = false, ShouldForceCloseNow = false };

    public bool IsBlocked          { get; init; }
    public bool ShouldForceCloseNow { get; init; }
    public NewsEvent? Event         { get; init; }
    public string ReasonCode        { get; init; } = "";
    public string ReasonDetail      { get; init; } = "";
    public double MinutesToEvent    { get; init; }
    public bool UsedCache           { get; init; }
    public double CacheAgeHours     { get; init; }
    public DateTime? BlockStartUtc  { get; init; }
    public DateTime? BlockEndUtc    { get; init; }
    public DateTime? ForceCloseStartUtc { get; init; }

    public string BuildSkipDetail()
    {
        if (Event == null)
            return ReasonDetail;

        var eventBkk = Event.EventTimeUtc.AddHours(7);
        return string.Join("|",
            $"event={Escape(Event.EventName)}",
            $"currency={Event.Currency}",
            $"impact={Event.Impact}",
            $"event_utc={Event.EventTimeUtc:yyyy-MM-ddTHH:mm:ssZ}",
            $"event_bkk={eventBkk:yyyy-MM-ddTHH:mm:ss}",
            $"minutes={MinutesToEvent:0.#}",
            $"provider={Escape(Event.Provider)}",
            $"used_cache={UsedCache}",
            $"cache_age_hours={CacheAgeHours:0.#}");
    }

    static string Escape(string s) => s.Replace("|", "/").Replace(",", ";");
}
