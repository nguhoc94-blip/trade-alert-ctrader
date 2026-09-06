using System;
using System.Collections.Generic;

namespace TradeAlert.Core.Models.Alerts;

public enum CompoundEventAnchorStatus
{
    Pending,
    Emitted,
    Expired,
}

/// <summary>
/// Pinned primary event with latched / pending state legs for window confirm.
/// </summary>
public sealed class CompoundEventAnchor
{
    public string SourceEventKey { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string RuleId { get; set; } = "";
    public int SlotIndex { get; set; }
    public SignalDirection Direction { get; set; }

    public string SourceTfToken { get; set; } = "";
    public int SourceBarIndex { get; set; }
    public DateTime SourceBarOpenTimeUtc { get; set; }
    public DateTime SourceBarCloseTimeUtc { get; set; }

    public int StagedAtChartBarIndex { get; set; }
    public int ExpiresAtChartBarIndex { get; set; }

    /// <summary>
    /// When >= 0, pending confirm expires by native source-TF bar index (R1/R2 M5 window).
    /// When -1, use <see cref="ExpiresAtChartBarIndex"/> (chart-bar window).
    /// </summary>
    public int ExpiresAtSourceBarIndex { get; set; } = -1;

    public AlertConditionId PrimaryEventCondition { get; set; }

    public CompoundEventAnchorStatus Status { get; set; }

    /// <summary>State legs only — key <c>{tf}:{ConditionId}</c>.</summary>
    public Dictionary<string, CompoundLegLatchState> LegLatchStates { get; set; } =
        new(StringComparer.Ordinal);

    public int EmittedAtChartBarIndex { get; set; } = -1;

    public static string LegKey(string tfToken, AlertConditionId condId) =>
        $"{tfToken}:{condId}";

    public bool AllStateLegsLatched()
    {
        if (LegLatchStates.Count == 0)
            return true;

        foreach (var kv in LegLatchStates)
        {
            if (kv.Value != CompoundLegLatchState.LatchedOk)
                return false;
        }

        return true;
    }

    public IReadOnlyList<string> PendingLegKeys()
    {
        var list = new List<string>();
        foreach (var kv in LegLatchStates)
        {
            if (kv.Value == CompoundLegLatchState.Pending)
                list.Add(kv.Key);
        }

        return list;
    }
}
