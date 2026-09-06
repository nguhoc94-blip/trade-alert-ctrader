using System;
using System.Collections.Generic;

namespace TradeAlert.Core.Models.Alerts;

/// <summary>Compound rule evaluation result ready for emit/dedup policy.</summary>
public sealed class CompoundSignal
{
    public string Symbol { get; init; } = "";
    public string RuleId { get; init; } = "";
    public SignalDirection Direction { get; init; }

    public string SourceTfToken { get; init; } = "";
    public int SourceEventBarIndex { get; init; }
    public DateTime SourceEventOpenTimeUtc { get; init; }
    public DateTime SourceEventCloseTimeUtc { get; init; }

    public int ChartBarIndex { get; init; }
    public DateTime EvaluatedAtUtc { get; init; }

    public bool AllTrue { get; init; }
    public IReadOnlyList<CompoundLegEval> Legs { get; init; } = new List<CompoundLegEval>();

    public string SourceEventKey =>
        CompoundSourceEventKey.Build(Symbol, RuleId, Direction, SourceTfToken, SourceEventBarIndex);
}
