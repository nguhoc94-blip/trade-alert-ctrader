using System;
using System.Collections.Generic;
using System.Linq;
using TradeAlert.Core.Models.Alerts;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>
/// When multiple compound rules fire on the same chart bar and direction, suppress lower-priority
/// tiers so only the strongest tier's events survive. Tiering (highest first):
/// <list type="number">
///   <item>Tier 2 — R3, R4 (M15 HL Real)</item>
///   <item>Tier 1 — R5, R6 (M15 NG)</item>
///   <item>Tier 0 — R1, R2 (M5)</item>
/// </list>
/// Within the same bar+direction group, only events whose tier == the max tier present are kept.
/// Execution order: tier desc → within-tier rank (R1 before R2, R5 before R6, R4 before R3) → bar asc.
/// </summary>
public static class CompoundFirePriorityFilter
{
    /// <summary>0-based slot index of R3 — legacy constant for M15 HL rules.</summary>
    public const int M15RuleSlotMin = 2;

    /// <summary>True for rules above the lowest tier (R3,R4,R5,R6).</summary>
    public static bool IsHighPriority(int slotIndex) => GetPriorityTier(slotIndex) >= 1;

    /// <summary>
    /// Tier of a 0-based slot index:
    ///   <list type="bullet">
    ///     <item>R3 (2), R4 (3) → 2 (M15 HL Real — top)</item>
    ///     <item>R5 (4), R6 (5) → 1 (M15 NG — mid)</item>
    ///     <item>R1 (0), R2 (1) → 0 (M5 — bottom)</item>
    ///   </list>
    /// Out-of-range slots get tier 0 as a safe default.
    /// </summary>
    public static int GetPriorityTier(int slotIndex) => slotIndex switch
    {
        2 or 3 => 2,
        4 or 5 => 1,
        0 or 1 => 0,
        _ => 0,
    };

    /// <summary>Lower rank = executes first within the same tier (R4 before R3, etc.).</summary>
    static int GetWithinTierRank(int slotIndex) => slotIndex switch
    {
        3 => 0, // R4
        2 => 1, // R3
        4 => 0, // R5
        5 => 1, // R6
        0 => 0, // R1
        1 => 1, // R2
        _ => slotIndex,
    };

    public static List<CompoundFireEvent> Apply(
        IReadOnlyList<CompoundFireEvent> events,
        bool enabled,
        Action<string>? log = null)
    {
        if (events.Count == 0)
            return new List<CompoundFireEvent>();

        if (!enabled)
            return SortForExecution(events);

        var maxTierByGroup = new Dictionary<(int Bar, SignalDirection Dir), int>();
        foreach (var ev in events)
        {
            var key = (ev.ChartBarIndex, ev.Direction);
            var tier = GetPriorityTier(ev.SlotIndex);
            if (!maxTierByGroup.TryGetValue(key, out var cur) || tier > cur)
                maxTierByGroup[key] = tier;
        }

        var result = new List<CompoundFireEvent>(events.Count);
        foreach (var ev in events)
        {
            var tier = GetPriorityTier(ev.SlotIndex);
            var maxTier = maxTierByGroup[(ev.ChartBarIndex, ev.Direction)];
            if (tier < maxTier)
            {
                log?.Invoke(
                    $"[L6BT] priority skip R{ev.SlotIndex + 1} {ev.Direction} bar={ev.ChartBarIndex} — tier {tier} < {maxTier} same bar/direction");
                continue;
            }

            result.Add(ev);
        }

        return SortForExecution(result);
    }

    static List<CompoundFireEvent> SortForExecution(IReadOnlyList<CompoundFireEvent> events) =>
        events
            .OrderByDescending(ev => GetPriorityTier(ev.SlotIndex))
            .ThenBy(ev => GetWithinTierRank(ev.SlotIndex))
            .ThenBy(ev => ev.ChartBarIndex)
            .ToList();
}
