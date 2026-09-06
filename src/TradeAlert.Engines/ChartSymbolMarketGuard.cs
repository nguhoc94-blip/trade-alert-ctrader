using System;
using cAlgo.API;
using cAlgo.API.Internals;

namespace TradeAlert.Indicator;

/// <summary>
/// Restricts MarketData / Symbols access to a single bound chart symbol (BacktestRobot).
/// When <paramref name="boundSymbol"/> is set, non-matching symbol requests are rejected and logged.
/// </summary>
public static class ChartSymbolMarketGuard
{
    public static bool IsAllowed(string? requestedSymbol, string? boundSymbol)
    {
        if (string.IsNullOrWhiteSpace(boundSymbol))
            return true;
        if (string.IsNullOrWhiteSpace(requestedSymbol))
            return true;
        return string.Equals(requestedSymbol.Trim(), boundSymbol.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static Bars? GetBars(
        MarketData marketData,
        TimeFrame timeFrame,
        string? boundSymbol,
        Action<string>? warnLog = null)
    {
        if (string.IsNullOrWhiteSpace(boundSymbol))
            return marketData.GetBars(timeFrame);

        return marketData.GetBars(timeFrame, boundSymbol);
    }

    public static Symbol? TryGetSymbol(
        Symbols symbols,
        string requestedSymbol,
        string? boundSymbol,
        bool allowCrossSymbol,
        Action<string>? warnLog = null)
    {
        if (!allowCrossSymbol && !IsAllowed(requestedSymbol, boundSymbol))
        {
            warnLog?.Invoke(FormatIgnoredWarning(requestedSymbol));
            return null;
        }

        try
        {
            return symbols.GetSymbol(requestedSymbol);
        }
        catch
        {
            return null;
        }
    }

    public static string FormatIgnoredWarning(string requestedSymbol) =>
        $"[L6BT] WARNING ignored non-chart symbol request: {requestedSymbol}";
}
