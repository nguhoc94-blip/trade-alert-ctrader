using System;
using System.Collections.Generic;

namespace TradeAlert.Core.Models.Alerts;

/// <summary>
/// Compound fire ledger keyed by <see cref="CompoundSourceEventKey"/>.
/// </summary>
public sealed class CompoundFireStageLedger
{
    readonly List<CompoundFireStageRecord> _items = new();
    readonly Dictionary<string, CompoundFireStageRecord> _bySourceKey = new(StringComparer.Ordinal);

    public int Count => _items.Count;

    public IReadOnlyList<CompoundFireStageRecord> Snapshot() => _items;

    public int PendingCount
    {
        get
        {
            var n = 0;
            foreach (var item in _items)
            {
                if (item.Status == CompoundFireStageStatus.Pending)
                    n++;
            }

            return n;
        }
    }

    public void Clear()
    {
        _items.Clear();
        _bySourceKey.Clear();
    }

    static string KeyOf(in CompoundFireStageRecord record) => record.SourceEventKey;

    public bool IsSourceEventEmitted(string sourceEventKey)
    {
        if (string.IsNullOrWhiteSpace(sourceEventKey))
            return false;

        return _bySourceKey.TryGetValue(sourceEventKey, out var item)
            && item.Status == CompoundFireStageStatus.Emitted;
    }

    /// <summary>Legacy slot+eventBar check — requires matching source bar on record.</summary>
    public bool IsEventEmitted(int slotIndex, int eventBarIndex)
    {
        if (eventBarIndex < 0)
            return false;

        foreach (var item in _items)
        {
            if (item.SlotIndex == slotIndex
                && item.SourceBarIndex == eventBarIndex
                && item.Status == CompoundFireStageStatus.Emitted)
                return true;
        }

        return false;
    }

    public bool IsEventPending(int slotIndex, int eventBarIndex)
    {
        if (eventBarIndex < 0)
            return false;

        foreach (var item in _items)
        {
            if (item.SlotIndex == slotIndex
                && item.SourceBarIndex == eventBarIndex
                && item.Status == CompoundFireStageStatus.Pending)
                return true;
        }

        return false;
    }

    public void UpsertPending(CompoundFireStageRecord record)
    {
        var key = KeyOf(record);
        if (IsSourceEventEmitted(key))
            return;

        if (_bySourceKey.TryGetValue(key, out var existing))
        {
            if (existing.Status == CompoundFireStageStatus.Emitted)
                return;

            existing.Status = CompoundFireStageStatus.Pending;
            CopyStagingFields(existing, record);
            return;
        }

        record.Status = CompoundFireStageStatus.Pending;
        AddRecord(record);
    }

    public IReadOnlyList<CompoundFireStageRecord> GetPendingForConfirm(
        int currentChartBarIndex,
        int eventValidBars)
    {
        var result = new List<CompoundFireStageRecord>();
        foreach (var item in _items)
        {
            if (item.Status != CompoundFireStageStatus.Pending)
                continue;

            if (!IsWithinConfirmWindow(item.StagedAtChartBarIndex, currentChartBarIndex, eventValidBars))
                continue;

            result.Add(item);
        }

        return result;
    }

    public static bool IsWithinConfirmWindow(
        int stagedAtChartBarIndex,
        int currentChartBarIndex,
        int eventValidBars)
    {
        if (stagedAtChartBarIndex < 0 || currentChartBarIndex < 0 || eventValidBars < 1)
            return false;

        var delta = currentChartBarIndex - stagedAtChartBarIndex;
        return delta >= 0 && delta <= eventValidBars;
    }

    public void MarkSourceEmitted(CompoundFireStageRecord record)
    {
        record.Status = CompoundFireStageStatus.Emitted;
        var key = KeyOf(record);

        if (_bySourceKey.TryGetValue(key, out var existing))
        {
            existing.Status = CompoundFireStageStatus.Emitted;
            existing.EmittedAtChartBarIndex = record.EmittedAtChartBarIndex;
            existing.EmittedAtChartBarOpenTime = record.EmittedAtChartBarOpenTime;
            return;
        }

        AddRecord(record);
    }

    public void MarkEmitted(
        int slotIndex,
        int eventBarIndex,
        int emittedAtChartBarIndex,
        DateTime emittedAtChartBarOpenTime)
    {
        foreach (var item in _items)
        {
            if (item.SlotIndex != slotIndex || item.SourceBarIndex != eventBarIndex)
                continue;

            item.Status = CompoundFireStageStatus.Emitted;
            item.EmittedAtChartBarIndex = emittedAtChartBarIndex;
            item.EmittedAtChartBarOpenTime = emittedAtChartBarOpenTime;
            _bySourceKey[item.SourceEventKey] = item;
            return;
        }

        var fallback = new CompoundFireStageRecord
        {
            SlotIndex = slotIndex,
            SourceBarIndex = eventBarIndex,
            Status = CompoundFireStageStatus.Emitted,
            EmittedAtChartBarIndex = emittedAtChartBarIndex,
            EmittedAtChartBarOpenTime = emittedAtChartBarOpenTime,
        };
        AddRecord(fallback);
    }

    public void ExpireOutsideWindow(int currentChartBarIndex, int eventValidBars)
    {
        if (currentChartBarIndex < 0 || eventValidBars < 1)
            return;

        for (var i = 0; i < _items.Count; i++)
        {
            if (_items[i].Status != CompoundFireStageStatus.Pending)
                continue;

            if (IsWithinConfirmWindow(_items[i].StagedAtChartBarIndex, currentChartBarIndex, eventValidBars))
                continue;

            _items[i].Status = CompoundFireStageStatus.Expired;
        }
    }

    public void PurgeHistory(int currentChartBarIndex, int eventValidBars)
    {
        if (currentChartBarIndex < 0 || eventValidBars < 1)
            return;

        var minKeep = currentChartBarIndex - eventValidBars - 1;
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            var keepBar = _items[i].Status == CompoundFireStageStatus.Emitted
                ? _items[i].EmittedAtChartBarIndex
                : _items[i].StagedAtChartBarIndex;

            if (keepBar >= 0 && keepBar < minKeep
                && _items[i].Status != CompoundFireStageStatus.Pending)
            {
                _bySourceKey.Remove(_items[i].SourceEventKey);
                _items.RemoveAt(i);
            }
        }
    }

    public void RemovePending(int slotIndex, int eventBarIndex)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            if (_items[i].SlotIndex != slotIndex || _items[i].SourceBarIndex != eventBarIndex)
                continue;
            if (_items[i].Status != CompoundFireStageStatus.Pending)
                continue;

            _bySourceKey.Remove(_items[i].SourceEventKey);
            _items.RemoveAt(i);
        }
    }

    void CopyStagingFields(CompoundFireStageRecord target, CompoundFireStageRecord source)
    {
        target.StagedAtChartBarIndex = source.StagedAtChartBarIndex;
        target.StagedAtMinTfBarIndex = source.StagedAtMinTfBarIndex;
        target.StagedAtChartBarOpenTime = source.StagedAtChartBarOpenTime;
        target.StagedAtMinTfBarOpenTime = source.StagedAtMinTfBarOpenTime;
    }

    void AddRecord(CompoundFireStageRecord record)
    {
        _items.Add(record);
        _bySourceKey[KeyOf(record)] = record;
    }
}
