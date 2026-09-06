using System;

namespace TradeAlert.Core.Models.Alerts;

/// <summary>
/// A closed bar on a source timeframe that may trigger compound rule evaluation
/// after dependent TF state caches are refreshed at that bar's close time.
/// </summary>
public sealed class CompoundSourceEventCandidate
{
    public string Symbol { get; init; } = "";
    public string SourceTfToken { get; init; } = "";
    public int SourceBarIndex { get; init; }
    public DateTime SourceBarOpenTimeUtc { get; init; }
    public DateTime SourceBarCloseTimeUtc { get; init; }

    public double Open { get; init; }
    public double High { get; init; }
    public double Low { get; init; }
    public double Close { get; init; }

    public string SourceKeyPrefix => $"{Symbol}|{SourceTfToken}|{SourceBarIndex}";
}
