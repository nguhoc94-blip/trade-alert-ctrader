namespace TradeAlert.BacktestRobot.Execution;

/// <summary>
/// Identifies a single trade setup so the same swing B + rule combination is not entered twice.
/// <see cref="SlotIndex"/> = compound rule slot (R1=0..R6=5); <see cref="SwingBPivotBar"/> = chart
/// bar index of the originating active swing B.
/// </summary>
public readonly record struct TradeDedupKey(int SlotIndex, int SwingBPivotBar, string TpLeg = "")
{
    public override string ToString() =>
        string.IsNullOrEmpty(TpLeg)
            ? $"R{SlotIndex + 1}@B{SwingBPivotBar}"
            : $"R{SlotIndex + 1}@B{SwingBPivotBar}@{TpLeg}";
}
