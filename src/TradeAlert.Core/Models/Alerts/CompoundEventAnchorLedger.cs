using System;
using System.Collections.Generic;

namespace TradeAlert.Core.Models.Alerts;

/// <summary>Ledger for <see cref="CompoundEventAnchor"/> confirm window.</summary>
public sealed class CompoundEventAnchorLedger
{
    readonly Dictionary<string, CompoundEventAnchor> _byKey = new(StringComparer.Ordinal);
    readonly List<CompoundEventAnchor> _items = new();

    public int Count => _items.Count;

    public IReadOnlyList<CompoundEventAnchor> Snapshot() => _items;

    public int PendingCount
    {
        get
        {
            var n = 0;
            foreach (var a in _items)
            {
                if (a.Status == CompoundEventAnchorStatus.Pending)
                    n++;
            }

            return n;
        }
    }

    public void Clear()
    {
        _byKey.Clear();
        _items.Clear();
    }

    public bool HasActive(string sourceEventKey) =>
        _byKey.TryGetValue(sourceEventKey, out var a)
        && a.Status is CompoundEventAnchorStatus.Pending or CompoundEventAnchorStatus.Emitted;

    public bool IsEmitted(string sourceEventKey) =>
        _byKey.TryGetValue(sourceEventKey, out var a)
        && a.Status == CompoundEventAnchorStatus.Emitted;

    public bool TryGet(string sourceEventKey, out CompoundEventAnchor anchor) =>
        _byKey.TryGetValue(sourceEventKey, out anchor!);

    public void UpsertPending(CompoundEventAnchor anchor)
    {
        if (IsEmitted(anchor.SourceEventKey))
            return;

        anchor.Status = CompoundEventAnchorStatus.Pending;
        if (_byKey.TryGetValue(anchor.SourceEventKey, out var existing))
        {
            foreach (var kv in anchor.LegLatchStates)
            {
                if (existing.LegLatchStates.TryGetValue(kv.Key, out var prior)
                    && prior == CompoundLegLatchState.LatchedOk)
                    continue;

                existing.LegLatchStates[kv.Key] = kv.Value;
            }

            existing.StagedAtChartBarIndex = anchor.StagedAtChartBarIndex;
            existing.ExpiresAtChartBarIndex = anchor.ExpiresAtChartBarIndex;
            existing.ExpiresAtSourceBarIndex = anchor.ExpiresAtSourceBarIndex;
            return;
        }

        _items.Add(anchor);
        _byKey[anchor.SourceEventKey] = anchor;
    }

    public IReadOnlyList<CompoundEventAnchor> GetPendingForConfirm(
        int currentChartBarIndex,
        Func<CompoundEventAnchor, int>? getCurrentSourceBarIndex = null)
    {
        var result = new List<CompoundEventAnchor>();
        foreach (var a in _items)
        {
            if (a.Status != CompoundEventAnchorStatus.Pending)
                continue;

            if (currentChartBarIndex < a.StagedAtChartBarIndex)
                continue;

            if (a.ExpiresAtSourceBarIndex >= 0)
            {
                var curSource = getCurrentSourceBarIndex?.Invoke(a) ?? -1;
                if (curSource < a.SourceBarIndex || curSource > a.ExpiresAtSourceBarIndex)
                    continue;
            }
            else if (currentChartBarIndex > a.ExpiresAtChartBarIndex)
            {
                continue;
            }

            result.Add(a);
        }

        return result;
    }

    public static int ComputeExpiresAtChartBar(int stagedAtChartBarIndex, int eventValidBars) =>
        stagedAtChartBarIndex + eventValidBars;

    public static int ComputeExpiresAtSourceBar(int sourceBarIndex, int eventValidBars) =>
        sourceBarIndex + eventValidBars;

    /// <summary>R1/R2 (M5 primary): EventValidWindow counts M5 bars, not M15 chart bars.</summary>
    public static bool UsesSourceBarConfirmWindow(CompoundEventAnchor anchor) =>
        UsesSourceBarConfirmWindow(anchor.SourceTfToken, anchor.SlotIndex);

    public static bool UsesSourceBarConfirmWindow(string sourceTfToken, int slotIndex) =>
        string.Equals(sourceTfToken, "5", StringComparison.Ordinal)
        && slotIndex is 0 or 1;

    public void MarkEmitted(string sourceEventKey, int emittedAtChartBarIndex)
    {
        if (!_byKey.TryGetValue(sourceEventKey, out var a))
            return;

        a.Status = CompoundEventAnchorStatus.Emitted;
        a.EmittedAtChartBarIndex = emittedAtChartBarIndex;
    }

    public IReadOnlyList<CompoundEventAnchor> ExpireOutsideWindow(
        int currentChartBarIndex,
        Func<CompoundEventAnchor, int>? getCurrentSourceBarIndex = null)
    {
        var expired = new List<CompoundEventAnchor>();
        foreach (var a in _items)
        {
            if (a.Status != CompoundEventAnchorStatus.Pending)
                continue;

            var outside = a.ExpiresAtSourceBarIndex >= 0
                ? (getCurrentSourceBarIndex?.Invoke(a) ?? -1) > a.ExpiresAtSourceBarIndex
                : currentChartBarIndex > a.ExpiresAtChartBarIndex;

            if (!outside)
                continue;

            a.Status = CompoundEventAnchorStatus.Expired;
            foreach (var key in new List<string>(a.LegLatchStates.Keys))
            {
                if (a.LegLatchStates[key] == CompoundLegLatchState.Pending)
                    a.LegLatchStates[key] = CompoundLegLatchState.Expired;
            }

            expired.Add(a);
        }

        return expired;
    }

    public void PurgeHistory(int currentChartBarIndex, int eventValidBars)
    {
        var minKeep = currentChartBarIndex - eventValidBars - 2;
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            var a = _items[i];
            if (a.Status == CompoundEventAnchorStatus.Pending)
                continue;

            var keepBar = a.Status == CompoundEventAnchorStatus.Emitted
                ? a.EmittedAtChartBarIndex
                : a.StagedAtChartBarIndex;

            if (keepBar >= 0 && keepBar < minKeep)
            {
                _byKey.Remove(a.SourceEventKey);
                _items.RemoveAt(i);
            }
        }
    }
}
