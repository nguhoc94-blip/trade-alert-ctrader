using System;

namespace TradeAlert.Core.Engines.AlertEvaluators;

public enum EventAlertTrackingKind
{
    EventA,
    EventB,
    EventC,
    CondSummary,
}

/// <summary>One Pine-style alert tracking label (FIRE/SKIP or cond summary).</summary>
public readonly struct EventAlertTrackingLabel
{
    public int SourceBarIndex { get; init; }
    public DateTime SourceBarOpenTime { get; init; }
    public string TfToken { get; init; }
    public double Price { get; init; }
    public string Text { get; init; }
    public EventAlertTrackingKind Kind { get; init; }
    public bool IsM15 { get; init; }
    public bool Fired { get; init; }
}
