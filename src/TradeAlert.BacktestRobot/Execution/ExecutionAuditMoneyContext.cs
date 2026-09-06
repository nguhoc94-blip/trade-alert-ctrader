namespace TradeAlert.BacktestRobot.Execution;

/// <summary>Pip/money context from cTrader Symbol for USD approximations in execution audit.</summary>
public readonly struct ExecutionAuditMoneyContext
{
    public double PipSize { get; init; }
    public double PipValuePerLot { get; init; }
    public double LotVolumeInUnits { get; init; }

    /// <summary>
    /// When true (XAUUSD/metals), USD ≈ priceDistance × volumeUnits.
    /// Otherwise fall back to pip-value scaling.
    /// </summary>
    public bool UsePriceDistanceTimesVolume { get; init; }

    public double ApproxUsdFromPips(double pips, double volumeInUnits) =>
        TradePlanRrLog.ApproxUsdFromPips(pips, volumeInUnits, PipValuePerLot, LotVolumeInUnits);

    public double ApproxUsdFromPrices(double entry, double targetPrice, double volumeInUnits) =>
        ExecutionPnLCalculator.ApproxUsdFromPriceDistance(entry, targetPrice, volumeInUnits, in this);
}
