using System;
using cAlgo.API;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>Classify closed trades from cTrader <see cref="History"/> for OnStop audit.</summary>
public static class HistoryCloseReasonClassifier
{
    /// <summary>Heuristic when broker SL/TP prices are not on <see cref="HistoricalTrade"/>.</summary>
    public static string Classify(HistoricalTrade trade)
    {
        if (trade.NetProfit > 0)
            return "TakeProfit";
        if (trade.NetProfit < 0)
            return "StopLoss";
        return "Closed";
    }

    public static void AuditHistory(
        History history,
        string labelPrefix,
        string symbolName,
        CloseReasonAuditTracker tracker)
    {
        foreach (var trade in history)
        {
            if (!string.Equals(trade.SymbolName, symbolName, StringComparison.Ordinal))
                continue;

            var label = trade.Label;
            if (string.IsNullOrEmpty(label) || !label.StartsWith(labelPrefix, StringComparison.Ordinal))
                continue;

            tracker.RecordFromHistory(label, Classify(trade));
        }
    }
}
