namespace TradeAlert.BacktestRobot.Execution.News;

/// <summary>
/// Prevents repeated force-close calls for the same event.
/// Tracks which event keys have already triggered a force-close in the current session.
/// Also maps position/order labels -> close reason for audit classification.
/// </summary>
public sealed class NewsForceCloseTracker
{
    readonly HashSet<string> _firedEventKeys = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> _labelToCloseReason = new(StringComparer.Ordinal);

    /// <summary>Returns true if this event has NOT been force-closed yet (i.e. should fire now).</summary>
    public bool ShouldFire(NewsEvent ev)
    {
        var key = BuildEventKey(ev);
        return !_firedEventKeys.Contains(key);
    }

    /// <summary>Mark event as fired and record the close reason for all provided labels.</summary>
    public void MarkFired(NewsEvent ev, IEnumerable<string> labels, string closeReason)
    {
        var key = BuildEventKey(ev);
        _firedEventKeys.Add(key);
        foreach (var label in labels)
            _labelToCloseReason[label] = closeReason;
    }

    /// <summary>Look up the news close reason for a label (for audit classification). Returns null if unknown.</summary>
    public string? GetCloseReason(string label) =>
        _labelToCloseReason.TryGetValue(label, out var r) ? r : null;

    public void Clear() { _firedEventKeys.Clear(); _labelToCloseReason.Clear(); }

    static string BuildEventKey(NewsEvent ev) =>
        $"{ev.Currency}|{ev.Category}|{ev.EventTimeUtc:yyyyMMddHHmm}";
}
