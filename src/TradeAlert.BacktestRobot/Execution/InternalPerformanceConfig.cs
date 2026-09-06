namespace TradeAlert.BacktestRobot.Execution;

/// <summary>Configuration for internal R-score close classification and slippage model.</summary>
public readonly struct InternalPerformanceConfig
{
    public double CloseReasonTolerancePips { get; init; }
    public double InternalTpSlippagePips { get; init; }
    public double InternalSlSlippagePips { get; init; }

    public static InternalPerformanceConfig Default => new()
    {
        CloseReasonTolerancePips = 2,
        InternalTpSlippagePips = 0,
        InternalSlSlippagePips = 0,
    };
}
