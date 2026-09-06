using System.Collections.Generic;

namespace TradeAlert.Core.Models.Alerts;

/// <summary>One compound AND rule: all <see cref="Entries"/> must be active simultaneously.</summary>
public readonly record struct CompoundConditionEntry(string TfToken, AlertConditionId ConditionId);

public sealed class CompoundAlertRule
{
    /// <summary>1-based index matching <c>Compound Rule N</c> parameter slot.</summary>
    public int SlotIndex { get; init; }

    public string RuleName { get; init; } = "";

    public List<CompoundConditionEntry> Entries { get; init; } = new();

    public bool IsEmpty => Entries.Count == 0;

    /// <summary>
    /// Trade direction derived from the first EVENT entry's <see cref="AlertConditionId"/>
    /// via <see cref="AlertConditionIdExtensions.GetDirection"/>. Set by
    /// <see cref="CompoundRuleParser.TryParse"/> after building <see cref="Entries"/>.
    /// </summary>
    public SignalDirection Direction { get; init; } = SignalDirection.Unknown;
}
