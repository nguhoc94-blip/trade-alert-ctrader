using System;
using System.Collections.Generic;
using TradeAlert.Core.Models.Alerts;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>
/// A fire that is being held until swing-C confirms so the SplitTpAtCAndD plan can be assembled.
/// Captured at fire time when <c>SwingCTpMode == SplitTpAtCAndD</c>, <c>SwingCEdgeTpFallbackToRR == false</c>,
/// and no confirmed swing-C exists yet — the fire is parked instead of being skipped, then revisited
/// every subsequent bar by <see cref="Loop6BacktestTradingBot"/> until either the two-leg split can be
/// placed or a terminal drop condition occurs (B confirmed broken, B moved, session cutoff, etc.).
/// </summary>
public sealed class DeferredSplitFire
{
    /// <summary>The original fire event (slot, direction, fire bar index, etc.).</summary>
    public CompoundFireEvent Ev { get; init; }

    /// <summary>Pivot bar index of the swing-B resolved at defer time (-1 when unknown).</summary>
    public int OriginalSwingBPivotBar { get; init; }

    /// <summary>Pivot pool index of the swing-B at defer time (informational; may shift as the pool trims).</summary>
    public int OriginalSwingBPivotIndex { get; init; }

    /// <summary>KeyTop of the swing-B keylevel at defer time — used to build a synthetic
    /// <see cref="PendingOrderContext"/> for pending-invalidation rules.</summary>
    public double SwingBKeyTop { get; init; }

    /// <summary>KeyBottom of the swing-B keylevel at defer time.</summary>
    public double SwingBKeyBottom { get; init; }

    /// <summary>Chart TF token of the swing-B (e.g. "15" for M15).</summary>
    public string SwingBTfToken { get; init; } = "15";

    /// <summary>R1/R2: swing C on M5; <see cref="OriginalSwingBPivotBar"/> stays chart-anchored.</summary>
    public bool UsesM5SwingC { get; init; }

    /// <summary>True when B at defer time came from M5 anchored fallback.</summary>
    public bool SwingBFromM5Anchor { get; init; }

    /// <summary>M5 bar for C resolution / B-broken when applicable.</summary>
    public int StructureSwingBBar { get; init; }

    /// <summary>Swing type of B at defer time (<see cref="SwingBResolver.TypeHigh"/> or <see cref="SwingBResolver.TypeLow"/>).</summary>
    public int OriginalSwingBType { get; init; }

    /// <summary>How B was resolved at defer time (ACTIVE vs MAIN_C fallback).</summary>
    public SwingBSource OriginalSwingBSource { get; init; } = SwingBSource.Active;

    /// <summary>Chart bar index when the fire was first deferred.</summary>
    public int DeferredAtBar { get; init; }

    /// <summary>UTC wall-clock when the fire was first deferred.</summary>
    public DateTime DeferredAtTimeUtc { get; init; }

    /// <summary>D zone resolved at fire bar (HIGH/LOW reference), reused when C confirms.</summary>
    public SwingCEdgeResult? CachedDFire { get; init; }

    /// <summary>Gồng lời: D' (M15/H1/H4) locked at fire for TP leg D on R3–R6.</summary>
    public SwingCEdgeResult? CachedDPrimeFire { get; init; }

    /// <summary>Fire-bar reference price used for D scan (HIGH for BUY, LOW for SELL).</summary>
    public double FireBarDReferencePrice { get; init; }

    /// <summary>Near-D cancel zone geometry locked at fire time (for defer invalidation).</summary>
    public double NearDCancelZoneHigh { get; init; }
    public double NearDCancelZoneLow { get; init; }
    public bool NearDCancelZoneIsOb { get; init; }
    public string NearDCancelZoneTfToken { get; init; } = "";
}

/// <summary>
/// Bounded queue of deferred split fires. Held in trade-bot state, processed once per bar in
/// <c>Loop6BacktestTradingBot</c>. Mutated on the trade-bot thread only — no thread safety.
/// </summary>
public sealed class DeferredSplitFireQueue
{
    readonly List<DeferredSplitFire> _items = new();

    public int Count => _items.Count;

    public IReadOnlyList<DeferredSplitFire> Items => _items;

    public DeferredSplitFire this[int index] => _items[index];

    public void Enqueue(DeferredSplitFire item)
    {
        if (item == null)
            throw new ArgumentNullException(nameof(item));
        _items.Add(item);
    }

    public void RemoveAt(int index) => _items.RemoveAt(index);

    public void Clear() => _items.Clear();

    /// <summary>True when a deferred fire already exists for the same slot + swing-B pivot.</summary>
    public bool Contains(int slotIndex, int swingBPivotBar)
    {
        foreach (var it in _items)
        {
            if (it.Ev.SlotIndex == slotIndex && it.OriginalSwingBPivotBar == swingBPivotBar)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Enqueue with optional tier filter keyed by (swing B pivot, direction). Reflects only the
    /// current queue — when higher-tier entries are dropped, lower-tier rules may enqueue on a
    /// later fire. Same-tier entries (e.g. R3 + R4) are kept together.
    /// </summary>
    public DeferredEnqueueResult TryEnqueueWithTierFilter(
        DeferredSplitFire item,
        bool tierFilterEnabled,
        Action<string>? log = null)
    {
        if (item == null)
            throw new ArgumentNullException(nameof(item));

        var slot = item.Ev.SlotIndex;
        var bPivot = item.OriginalSwingBPivotBar;
        if (bPivot > 0 && Contains(slot, bPivot))
            return DeferredEnqueueResult.DuplicateSlotAndB;

        if (!tierFilterEnabled || bPivot <= 0)
        {
            Enqueue(item);
            return DeferredEnqueueResult.Enqueued;
        }

        var dir = item.Ev.Direction;
        var incomingTier = CompoundFirePriorityFilter.GetPriorityTier(slot);

        for (var j = _items.Count - 1; j >= 0; j--)
        {
            var existing = _items[j];
            if (existing.OriginalSwingBPivotBar != bPivot || existing.Ev.Direction != dir)
                continue;

            var existingTier = CompoundFirePriorityFilter.GetPriorityTier(existing.Ev.SlotIndex);
            if (incomingTier > existingTier)
            {
                log?.Invoke(
                    $"[L6BT] DEFERRED-SPLIT priority evict R{existing.Ev.SlotIndex + 1} {existing.Ev.Direction} B={bPivot} — tier {existingTier} < incoming {incomingTier}");
                _items.RemoveAt(j);
            }
            else if (incomingTier < existingTier)
            {
                log?.Invoke(
                    $"[L6BT] DEFERRED-SPLIT priority skip enqueue R{slot + 1} {item.Ev.Direction} B={bPivot} — tier {incomingTier} < {existingTier} same B/direction");
                return DeferredEnqueueResult.SkippedLowerTier;
            }
        }

        Enqueue(item);
        return DeferredEnqueueResult.Enqueued;
    }

    /// <summary>
    /// True when this queued item is among the highest tier(s) for its (B, direction) group.
    /// </summary>
    public bool CanExecuteAtIndex(int index, bool tierFilterEnabled)
    {
        if (!tierFilterEnabled || index < 0 || index >= _items.Count)
            return true;

        var item = _items[index];
        var bPivot = item.OriginalSwingBPivotBar;
        if (bPivot <= 0)
            return true;

        var dir = item.Ev.Direction;
        var myTier = CompoundFirePriorityFilter.GetPriorityTier(item.Ev.SlotIndex);
        var maxTier = GetMaxTierForSetup(bPivot, dir);
        return myTier >= maxTier;
    }

    /// <summary>
    /// After a higher-tier deferred fire executes, drop any lower-tier waits on the same setup.
    /// </summary>
    public int EvictLowerTierSameSetup(
        int swingBPivotBar,
        SignalDirection direction,
        int executedTier,
        bool tierFilterEnabled,
        Action<string>? log = null)
    {
        if (!tierFilterEnabled || swingBPivotBar <= 0)
            return 0;

        var removed = 0;
        for (var j = _items.Count - 1; j >= 0; j--)
        {
            var existing = _items[j];
            if (existing.OriginalSwingBPivotBar != swingBPivotBar || existing.Ev.Direction != direction)
                continue;

            var tier = CompoundFirePriorityFilter.GetPriorityTier(existing.Ev.SlotIndex);
            if (tier >= executedTier)
                continue;

            log?.Invoke(
                $"[L6BT] DEFERRED-SPLIT priority evict R{existing.Ev.SlotIndex + 1} {existing.Ev.Direction} B={swingBPivotBar} — tier {tier} < executed {executedTier}");
            _items.RemoveAt(j);
            removed++;
        }

        return removed;
    }

    int GetMaxTierForSetup(int swingBPivotBar, SignalDirection direction)
    {
        var maxTier = 0;
        foreach (var it in _items)
        {
            if (it.OriginalSwingBPivotBar != swingBPivotBar || it.Ev.Direction != direction)
                continue;
            maxTier = Math.Max(maxTier, CompoundFirePriorityFilter.GetPriorityTier(it.Ev.SlotIndex));
        }

        return maxTier;
    }
}

public enum DeferredEnqueueResult
{
    Enqueued,
    DuplicateSlotAndB,
    SkippedLowerTier,
}
