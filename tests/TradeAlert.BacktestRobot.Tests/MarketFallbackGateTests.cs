using TradeAlert.Core.Models.Alerts;
using TradeAlert.BacktestRobot.Execution;
using TradeAlert.BacktestRobot.Execution.Kl;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests;

public sealed class MarketFallbackGateTests
{
    const double Pip = 0.0001;

    static MarketFillValidationParams DefaultParams(
        bool enableMinSl = false,
        double baseRr = 1.25,
        bool enableSpreadGate = false,
        double maxSpread = 0,
        double spreadGateValue = 0) => new()
    {
        EnableMinSlConstraint = enableMinSl,
        BaseRewardRisk = baseRr,
        AccountBalanceFtmo = 100_000,
        RiskPercentForLeg = 1.0,
        ConvUsdPerQuote = 1.0,
        SpreadPips = 1.0,
        RoundPrecision = 1000,
        SymbolName = "EURUSD",
        TickSize = Pip,
        PipSize = Pip,
        AssetType = KlAssetType.Forex,
        CustomContractSize = 100_000,
        EnableSpreadGate = enableSpreadGate,
        MaxSpreadGate = maxSpread,
        SpreadGateValue = spreadGateValue,
    };

    static TradePlan SellSplitPlan(double entry, double sl, double tpC) => new()
    {
        IsBuy = false,
        Direction = SignalDirection.Sell,
        EntryLimit = entry,
        StopLoss = sl,
        TakeProfit = tpC,
        TpCLegPrice = tpC,
        TpLegTag = "C",
        LotFtmo = 0.5,
    };

    [Fact]
    public void AllowsMarket_WhenMoveEntryOff_AndRrMeetsBase()
    {
        var plan = SellSplitPlan(entry: 1.08095, sl: 1.0816, tpC: 1.08014);
        Assert.True(MarketFallbackGate.TryValidateMarketFill(
            in plan, bid: 1.08158, ask: 1.08161, DefaultParams(), out var result));
        Assert.True(result.Allowed);
        Assert.Equal(1.08158, result.FillPrice, 5);
        Assert.True(result.LotFtmoAtFill is > 0);
    }

    [Fact]
    public void BlocksMarket_WhenRRCBelowBase()
    {
        var plan = SellSplitPlan(entry: 1.08095, sl: 1.08226, tpC: 1.0808);
        Assert.False(MarketFallbackGate.TryValidateMarketFill(
            in plan, bid: 1.08158, ask: 1.08161, DefaultParams(), out var result));
        Assert.Contains("RR at fill", result.RejectReason);
        Assert.Contains("< base", result.RejectReason);
    }

    [Fact]
    public void AllowsMarket_WhenRRCMeetsBase()
    {
        var plan = SellSplitPlan(entry: 1.08095, sl: 1.0820, tpC: 1.0795);
        Assert.True(MarketFallbackGate.TryValidateMarketFill(
            in plan, bid: 1.0810, ask: 1.08103, DefaultParams(), out var result));
        Assert.True(result.Allowed);
        Assert.True(result.RrAtFill >= 1.25);
    }

    [Fact]
    public void BlocksMarket_WhenRrBelowBase_OnRewardRiskPlan()
    {
        var plan = new TradePlan
        {
            IsBuy = true,
            EntryLimit = 1.10,
            StopLoss = 1.09,
            TakeProfit = 1.12,
            TakeProfitSource = TakeProfitSource.RewardRisk,
            LotFtmo = 0.5,
        };
        Assert.False(MarketFallbackGate.TryValidateMarketFill(
            in plan, bid: 1.105, ask: 1.1052, DefaultParams(), out var result));
        Assert.Contains("RR at fill", result.RejectReason);
    }

    [Fact]
    public void BlocksMarket_WhenMinSlFailsAtFill()
    {
        var plan = new TradePlan
        {
            IsBuy = false,
            EntryLimit = 1.0810,
            StopLoss = 1.0816,
            TakeProfit = 1.0800,
            MinSlFloorPips = 5.0,
            StopLossPips = 6.0,
            LotFtmo = 0.5,
        };
        var p = DefaultParams(enableMinSl: true);
        Assert.False(MarketFallbackGate.TryValidateMarketFill(
            in plan, bid: 1.08158, ask: 1.08161, in p, out var result));
        Assert.Contains("min-SL at fill", result.RejectReason);
    }

    [Fact]
    public void BlocksMarket_WhenSpreadGateFailsAtFill()
    {
        var plan = SellSplitPlan(entry: 1.08095, sl: 1.0820, tpC: 1.0795);
        var p = DefaultParams(enableSpreadGate: true, maxSpread: 2.0, spreadGateValue: 3.5);
        Assert.False(MarketFallbackGate.TryValidateMarketFill(
            in plan, bid: 1.0810, ask: 1.08103, in p, out var result));
        Assert.Contains("spread gate", result.RejectReason);
    }

    [Fact]
    public void RecalculatesLot_FromFillSlDistance()
    {
        var plan = new TradePlan
        {
            IsBuy = true,
            EntryLimit = 1.1000,
            StopLoss = 1.0980,
            TakeProfit = 1.1060,
            StopLossPips = 20,
            LotFtmo = 0.50,
        };
        Assert.True(MarketFallbackGate.TryValidateMarketFill(
            in plan, bid: 1.1010, ask: 1.1012, DefaultParams(), out var result));
        Assert.True(result.LotFtmoAtFill is > 0);
        Assert.NotEqual(plan.LotFtmo, result.LotFtmoAtFill);
        Assert.Equal(32.0, result.SlPipsAtFill, 1);
    }

    [Fact]
    public void BlocksMarket_WhenGeometryInvalid()
    {
        var plan = new TradePlan
        {
            IsBuy = true,
            EntryLimit = 1.10,
            StopLoss = 1.11,
            TakeProfit = 1.12,
            LotFtmo = 0.5,
        };
        Assert.False(MarketFallbackGate.TryValidateMarketFill(
            in plan, bid: 1.105, ask: 1.1052, DefaultParams(), out var result));
        Assert.Contains("inconsistent BUY", result.RejectReason);
    }

    [Fact]
    public void MarketEntryPrice_UsesAskForBuy_BidForSell()
    {
        var buy = new TradePlan { IsBuy = true, EntryLimit = 1.10 };
        var sell = new TradePlan { IsBuy = false, EntryLimit = 1.10 };
        Assert.Equal(1.1052, MarketFallbackGate.MarketEntryPrice(in buy, bid: 1.105, ask: 1.1052));
        Assert.Equal(1.105, MarketFallbackGate.MarketEntryPrice(in sell, bid: 1.105, ask: 1.1052));
    }
}
