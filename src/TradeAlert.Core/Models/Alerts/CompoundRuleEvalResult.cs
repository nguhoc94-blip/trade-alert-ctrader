using System.Collections.Generic;

namespace TradeAlert.Core.Models.Alerts;

public sealed class CompoundRuleEvalResult
{
    public bool AllTrue { get; init; }
    public IReadOnlyList<CompoundLegEval> Legs { get; init; } = new List<CompoundLegEval>();
    public IReadOnlyList<string> MissingLegs { get; init; } = new List<string>();
}
