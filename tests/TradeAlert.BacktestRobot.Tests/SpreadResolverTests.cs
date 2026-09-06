using TradeAlert.BacktestRobot.Execution;
using TradeAlert.BacktestRobot.Execution.Kl;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests;

public sealed class SpreadResolverTests
{
    const double XauPip = 0.01;

    [Fact]
    public void GoldOverride_0_4_IsPriceDistance_MatchesBrokerAuto()
    {
        var auto = SpreadResolver.Resolve(0, symbolSpreadPrice: 0.40, XauPip, XauPip, "XAUUSD", KlAssetType.XAUUSD);
        var manual = SpreadResolver.Resolve(0.4, symbolSpreadPrice: 0, XauPip, XauPip, "XAUUSD", KlAssetType.XAUUSD);

        Assert.Equal(SpreadTelemetrySources.Symbol, auto.Source);
        Assert.Equal(SpreadTelemetrySources.OverrideGoldPrice, manual.Source);
        Assert.Equal(0.40, auto.Price, 6);
        Assert.Equal(0.40, manual.Price, 6);
        Assert.Equal(40, auto.Pips, 6);
        Assert.Equal(40, manual.Pips, 6);
    }

    [Fact]
    public void GoldOverride_OldPipStyle40_WouldBeWrongSpreadPrice()
    {
        var r = SpreadResolver.Resolve(40, symbolSpreadPrice: 0, XauPip, XauPip, "XAUUSD", KlAssetType.XAUUSD);

        Assert.Equal(SpreadTelemetrySources.OverrideGoldPrice, r.Source);
        Assert.Equal(40, r.Price, 6);
        Assert.Equal(4000, r.Pips, 6);
    }

    [Fact]
    public void ForexOverride_UsesPips_NotPrice()
    {
        var r = SpreadResolver.Resolve(1.2, symbolSpreadPrice: 0, 0.0001, 0.00001, "EURUSD", KlAssetType.Forex);

        Assert.Equal(SpreadTelemetrySources.OverridePips, r.Source);
        Assert.Equal(1.2, r.Pips, 6);
        Assert.Equal(0.00012, r.Price, 8);
    }

    [Fact]
    public void BrokerPath_DividesSymbolSpreadByPipSize()
    {
        var r = SpreadResolver.Resolve(0, symbolSpreadPrice: 0.00015, 0.0001, 0.00001, "EURUSD", KlAssetType.Forex);

        Assert.Equal(SpreadTelemetrySources.Symbol, r.Source);
        Assert.Equal(1.5, r.Pips, 6);
        Assert.Equal(0.00015, r.Price, 8);
    }

    [Theory]
    [InlineData("XAUUSD", KlAssetType.Auto, true)]
    [InlineData("GOLD", KlAssetType.Auto, true)]
    [InlineData("XAGUSD", KlAssetType.Auto, true)]
    [InlineData("EURUSD", KlAssetType.Forex, false)]
    public void UsesPriceSpreadOverride_DetectsMetals(string symbol, KlAssetType assetType, bool expected)
    {
        Assert.Equal(expected, SpreadResolver.UsesPriceSpreadOverride(symbol, assetType));
    }
}
