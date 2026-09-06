using System.Collections.Generic;

namespace TradeAlert.Core.Models.KeyLevel;

/// <summary>
/// Replaces Pine penHBar/penHPrice/penHBBar/penHBTier/penHLbl/penHIsGap (and L equivalents).
/// One entry per pending swing candidate. Labels are omitted (UI handled by cTrader host).
/// </summary>
public sealed class PendingSwingEntry
{
    public int    Bar          { get; init; }   // penHBar / penLBar
    public double Price        { get; init; }   // penHPrice / penLPrice
    public int    BBar         { get; init; }   // penHBBar / penLBBar
    public int    BTier        { get; init; }   // penHBTier / penLBTier
    public bool   IsGap        { get; init; }   // penHIsGap / penLIsGap
    /// <summary>Gap 1.1: Pine A4 guard — cleanHigh of the bar BEFORE bar A (highUsed[1]).</summary>
    public double PrevCleanHigh { get; init; } = double.NaN;
    /// <summary>Gap 1.1: Pine A4 guard — cleanLow of the bar BEFORE bar A (lowUsed[1]).</summary>
    public double PrevCleanLow  { get; init; } = double.NaN;
}

/// <summary>
/// Mutable queue for pending HIGH or LOW swing candidates.
/// Mirrors Pine array mutation: Remove at index from end → back-to-front sweep.
/// </summary>
public sealed class PendingSwingQueue
{
    readonly List<PendingSwingEntry> _entries = new();

    public int Count => _entries.Count;

    public PendingSwingEntry this[int i] => _entries[i];

    public void Push(PendingSwingEntry e) => _entries.Add(e);

    public void Set(int index, PendingSwingEntry entry) => _entries[index] = entry;

    public void RemoveAt(int index) => _entries.RemoveAt(index);

    public void Clear() => _entries.Clear();

    /// <summary>Remove entries whose Bar is strictly inside (fromBarExcl, toBarExcl).</summary>
    public void ClearRange(int fromBarExcl, int toBarExcl)
    {
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            var bar = _entries[i].Bar;
            if (bar > fromBarExcl && bar < toBarExcl)
                _entries.RemoveAt(i);
        }
    }

    /// <summary>
    /// Remove all entries where Bar &lt;= <paramref name="bar"/>.
    /// Entries with Bar &gt; <paramref name="bar"/> are kept (they belong to the next cycle).
    /// </summary>
    public void RemoveUpToInclusive(int bar)
    {
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].Bar <= bar)
                _entries.RemoveAt(i);
        }
    }
}
