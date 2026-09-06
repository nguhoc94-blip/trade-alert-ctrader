using TradeAlert.Core.Models.KeyLevel;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>Đọc <see cref="PivotStateStore.GetBreakDepthDist"/> (latched Step A) và chuyển sang pip.</summary>
public static class BreakDepthPivotHelper
{
    public static bool TryGetBreakDepthPips(PivotStateStore pivots, int i, double pipSize, out double pips)
    {
        pips = 0;
        var dist = pivots.GetBreakDepthDist(i);
        if (double.IsNaN(dist) || dist <= 0 || pipSize <= 0)
            return false;

        pips = dist / pipSize;
        return pips > 0;
    }
}
