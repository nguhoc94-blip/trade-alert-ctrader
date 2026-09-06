using System.Collections.Generic;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>One tracked live setup (pending order and/or open position) keyed by its order label.</summary>
public sealed class TrackedSetup
{
    public PendingOrderContext Context { get; init; } = null!;
    public double CurrentTakeProfit { get; set; }
    public bool TpMovedToEntryOnBBroken { get; set; }

    /// <summary>True once the TP has been re-pointed to the confirmed swing-C edge.</summary>
    public bool TpUpdatedToSwingC { get; set; }

    /// <summary>Latch: set to true when the near-B touch condition fires for the first time.</summary>
    public bool NearBZoneTouched { get; set; }

    /// <summary>Chart-state bar index at which this setup's order first became an open position.
    /// -1 until observed. Used by the post-fill C-rollover rule to only treat zones that appeared
    /// AFTER the fill as "new" obstacles below C.</summary>
    public int FillBarIndex { get; set; } = -1;
}

/// <summary>
/// In-memory map of order label → setup, used for dedup (one entry per swing-B + rule), flip and
/// swing-broken exit. Reconciled against live cTrader positions/pending orders every bar.
/// </summary>
public sealed class OpenPositionBook
{
    readonly Dictionary<string, TrackedSetup> _byLabel = new();

    public IReadOnlyCollection<TrackedSetup> Entries => _byLabel.Values;

    public void Track(PendingOrderContext context, double initialTakeProfit)
    {
        _byLabel[context.Label] = new TrackedSetup
        {
            Context = context,
            CurrentTakeProfit = initialTakeProfit,
        };
    }

    public bool TryGetSetup(string label, out TrackedSetup setup)
    {
        if (_byLabel.TryGetValue(label, out var s))
        {
            setup = s;
            return true;
        }

        setup = null!;
        return false;
    }

    public void MarkTpMovedToEntry(string label, double newTakeProfit)
    {
        if (!_byLabel.TryGetValue(label, out var setup))
            return;

        setup.CurrentTakeProfit = newTakeProfit;
        setup.TpMovedToEntryOnBBroken = true;
    }

    public void MarkTpUpdatedToSwingC(string label, double newTakeProfit)
    {
        if (!_byLabel.TryGetValue(label, out var setup))
            return;

        setup.CurrentTakeProfit = newTakeProfit;
        setup.TpUpdatedToSwingC = true;
    }

    public void MarkNearBZoneTouched(string label)
    {
        if (_byLabel.TryGetValue(label, out var setup))
            setup.NearBZoneTouched = true;
    }

    /// <summary>Record the chart-bar index at which a tracked order first became an open position
    /// (only set once — first observation wins).</summary>
    public void MarkFillBar(string label, int barIndex)
    {
        if (_byLabel.TryGetValue(label, out var setup) && setup.FillBarIndex < 0)
            setup.FillBarIndex = barIndex;
    }

    public bool HasLabel(string label) => _byLabel.ContainsKey(label);

    public bool TryGetContext(string label, out PendingOrderContext context)
    {
        if (_byLabel.TryGetValue(label, out var setup))
        {
            context = setup.Context;
            return true;
        }

        context = null!;
        return false;
    }

    public bool HasDedup(TradeDedupKey key)
    {
        foreach (var s in _byLabel.Values)
            if (s.Context.DedupKey == key)
                return true;
        return false;
    }

    public void RemoveByLabel(string label) => _byLabel.Remove(label);

    /// <summary>Drop any tracked setup whose label is no longer live (closed by SL/TP externally).</summary>
    public IReadOnlyList<string> RetainOnly(ISet<string> liveLabels)
    {
        var stale = new List<string>();
        foreach (var label in _byLabel.Keys)
            if (!liveLabels.Contains(label))
                stale.Add(label);
        foreach (var label in stale)
            _byLabel.Remove(label);
        return stale;
    }

    public void Clear() => _byLabel.Clear();
}
