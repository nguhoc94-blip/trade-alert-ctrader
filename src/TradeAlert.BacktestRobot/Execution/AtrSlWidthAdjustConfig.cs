namespace TradeAlert.BacktestRobot.Execution;

/// <summary>ATR-ratio adjustment for keylevel-width SL multiplier.</summary>
public sealed class AtrSlWidthAdjustConfig
{
    public bool UseAtrAdjustedSlWidthMultiplier { get; init; } = true;
    public double BaseSlWidthMult { get; init; } = 2.0;
    public string AtrAdjustTf { get; init; } = "15";
    public int AtrAdjustPeriod { get; init; } = 14;
    public int AtrBaselinePeriod { get; init; } = 100;
    public double AtrAdjustmentFactor { get; init; } = 1.0;
    public double MinDynamicSlWidthMult { get; init; } = 1.0;
    public double MaxDynamicSlWidthMult { get; init; } = 4.0;
    public bool AtrAdjustUseClosedBar { get; init; } = true;
    public bool AtrAdjustFallbackToBase { get; init; } = true;
}

public sealed class AtrSlWidthAdjustResult
{
    public bool Success { get; init; }
    public bool UsedFallback { get; init; }
    public string? SkipReason { get; init; }
    public double DynamicSlWidthMult { get; init; }
    public double DynamicSlWidthMultRaw { get; init; }
    public double CurrentAtr { get; init; }
    public double BaselineAtr { get; init; }
    public double AtrRatio { get; init; }
    public string AtrTf { get; init; } = "";
}
