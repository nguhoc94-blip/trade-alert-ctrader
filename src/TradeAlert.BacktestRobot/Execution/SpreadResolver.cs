using TradeAlert.BacktestRobot.Execution.Kl;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>Resolved spread in both pips and price, with source tag for telemetry.</summary>
public readonly struct SpreadResolution
{
    public double Pips { get; init; }
    public double Price { get; init; }
    public string Source { get; init; }
}

/// <summary>
/// Unifies spread resolution for broker auto-read vs manual override.
/// Forex override values are cTrader pips; gold/silver (XAU/XAG) override values match
/// the cTrader UI price distance (e.g. 0.4 → $0.40 spread, not 0.4 pips).
/// </summary>
public static class SpreadResolver
{
    /// <summary>
    /// Metals whose cTrader UI shows spread as a price distance (0.40), not a pip count (40).
    /// </summary>
    public static bool UsesPriceSpreadOverride(string symbolName, KlAssetType assetType)
    {
        if (assetType == KlAssetType.XAUUSD)
            return true;

        var sym = symbolName ?? "";
        return sym.Contains("XAU", StringComparison.OrdinalIgnoreCase)
               || sym.Contains("GOLD", StringComparison.OrdinalIgnoreCase)
               || sym.Contains("XAG", StringComparison.OrdinalIgnoreCase)
               || sym.Contains("SILVER", StringComparison.OrdinalIgnoreCase);
    }

    public static SpreadResolution Resolve(
        double spreadOverride,
        double symbolSpreadPrice,
        double pipSize,
        double tickSize,
        string symbolName,
        KlAssetType assetType = KlAssetType.Auto)
    {
        var pip = pipSize > 0 ? pipSize : tickSize;

        if (spreadOverride > 0)
        {
            if (UsesPriceSpreadOverride(symbolName, assetType))
            {
                var price = spreadOverride;
                return new SpreadResolution
                {
                    Price = price,
                    Pips = pip > 0 ? price / pip : 0,
                    Source = SpreadTelemetrySources.OverrideGoldPrice,
                };
            }

            return new SpreadResolution
            {
                Pips = spreadOverride,
                Price = spreadOverride * pip,
                Source = SpreadTelemetrySources.OverridePips,
            };
        }

        var brokerPrice = symbolSpreadPrice;
        return new SpreadResolution
        {
            Price = brokerPrice,
            Pips = pip > 0 ? brokerPrice / pip : 0,
            Source = SpreadTelemetrySources.Symbol,
        };
    }
}
