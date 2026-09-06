using TradeAlert.BacktestRobot.Execution.Kl;

namespace TradeAlert.BacktestRobot.Execution.Analytics;

public static class Loop6AnalyticsMath
{
    public static SpreadResolution ResolveSpreadSnapshotForLog(
        double spreadOverride,
        double symbolSpreadPrice,
        double pipSize,
        double tickSize,
        string symbolName,
        KlAssetType assetType) =>
        SpreadResolver.Resolve(spreadOverride, symbolSpreadPrice, pipSize, tickSize, symbolName, assetType);

    public static double ComputeSpreadToSlRatio(double spreadPips, double slStructurePips) =>
        slStructurePips > 0 ? spreadPips / slStructurePips : 0;

    public static double ComputeSlExpandRatio(double slFinalPips, double slStructurePips) =>
        slStructurePips > 0 ? slFinalPips / slStructurePips : 0;

    public static double ComputeResultRByPrice(
        double entry,
        double initialSl,
        double closePrice,
        bool isBuy)
    {
        var risk = isBuy ? entry - initialSl : initialSl - entry;
        if (risk <= 0)
            return 0;

        var pnl = isBuy ? closePrice - entry : entry - closePrice;
        return pnl / risk;
    }

    public static double ComputeResultRByMoney(double netProfit, double plannedRiskMoney) =>
        plannedRiskMoney > 0 ? netProfit / plannedRiskMoney : 0;

    public static string MapSpreadSourceForLog(string source) => source switch
    {
        SpreadTelemetrySources.Symbol => "Broker",
        SpreadTelemetrySources.OverridePips => "Override",
        SpreadTelemetrySources.OverrideGoldPrice => "Override",
        _ => source,
    };
}
