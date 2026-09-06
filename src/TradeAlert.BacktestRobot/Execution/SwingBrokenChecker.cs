using TradeAlert.Core.Engines;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>
/// Exit rule — determines whether swing B (identified by its stable chart bar index) has become
/// broken / broken-done.
/// </summary>
public static class SwingBrokenChecker
{
    public static bool IsConfirmedBrokenFlag(int flag) =>
        flag is 2 or -1 or -3;

    public static int FindByBar(PineStateEngine state, int pivotBar)
    {
        var pivots = state.Pivots;
        for (var i = pivots.Count - 1; i >= 0; i--)
        {
            if (pivots.GetSnapshot(i).BarIndex == pivotBar)
                return i;
        }
        return -1;
    }

    public static bool IsBroken(PineStateEngine state, int pivotBar)
    {
        var idx = FindByBar(state, pivotBar);
        if (idx < 0)
            return false;

        var pivots = state.Pivots;
        if (IsConfirmedBrokenFlag(pivots.GetFlag(idx)))
            return true;
        if (pivots.GetKeyBreakLabeled(idx))
            return true;
        return false;
    }
}
