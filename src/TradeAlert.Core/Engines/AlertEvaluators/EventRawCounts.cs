namespace TradeAlert.Core.Engines.AlertEvaluators;

/// <summary>Counts from <c>f_detect_events_raw</c> — HL = Event A; NG = Event B + C.</summary>
public readonly struct EventRawCounts
{
    public int BuyHL { get; init; }
    public int SellHL { get; init; }
    public int BuyNG { get; init; }
    public int SellNG { get; init; }
}
