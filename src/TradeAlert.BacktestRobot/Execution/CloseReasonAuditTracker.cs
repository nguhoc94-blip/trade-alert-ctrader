using System;
using System.Collections.Generic;
using System.Linq;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>Counts how positions closed (broker SL/TP vs bot-initiated reasons).</summary>
public sealed class CloseReasonAuditTracker
{
    readonly Dictionary<string, string> _manualReasonByLabel = new(StringComparer.Ordinal);
    readonly Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);

    public void RecordManualClose(string label, string reason)
    {
        if (string.IsNullOrWhiteSpace(label))
            return;
        _manualReasonByLabel[label] = string.IsNullOrWhiteSpace(reason) ? "Closed" : reason;
    }

    public void RecordFromHistory(string label, string inferredReason)
    {
        if (_manualReasonByLabel.TryGetValue(label, out var manual))
        {
            Record(manual);
            _manualReasonByLabel.Remove(label);
            return;
        }

        Record(inferredReason);
    }

    public void Record(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            reason = "Unknown";
        _counts.TryGetValue(reason, out var n);
        _counts[reason] = n + 1;
    }

    public int Get(string reason) =>
        _counts.TryGetValue(reason, out var n) ? n : 0;

    public int Total => _counts.Values.Sum();

    public void PrintSummary(Action<string> print)
    {
        print("[L6BT] Close reason summary:");
        if (_counts.Count == 0)
        {
            print("[L6BT]   (no position closes recorded)");
            return;
        }

        foreach (var kv in _counts.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            print($"[L6BT]   {kv.Key}={kv.Value}");

        var tp = Get("TakeProfit");
        var sl = Get("StopLoss");
        var beforeTp = Total - tp;
        print($"[L6BT]   closedBeforeTP={beforeTp} hitTP={tp} hitSL={sl}");
    }
}
