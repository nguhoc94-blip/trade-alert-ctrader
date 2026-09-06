using KlEntryLotIndicator;
using Xunit;

namespace KlEntryLot.Tests;

public sealed class KlEntryLotCalculatorTests
{
    static KlEntryLotInput BaseInput() => new()
    {
        AssetType = KlAssetType.Forex,
        AccountBalance = 400,
        AccountBalanceFtmo = 20_000,
        RiskPercent = 1,
        EntryPrice = 1.1000,
        StopPrice = 1.0950,
        Tp1 = 1.1100,
        Tp2 = 1.1200,
        SpreadPips = 2,
        RoundPrecision = 1000,
        SymbolName = "EURUSD",
        TickSize = 0.00001,
        PipSize = 0.0001,
        QuoteCurrency = "USD",
        AutoConvToUsd = 1,
    };

    [Fact]
    public void Buy_FtmoUsesFullSpread_NotHalfSpread()
    {
        var r = KlEntryLotCalculator.Compute(BaseInput());

        Assert.True(r.IsBuy);
        Assert.Equal(0.0002, r.SpreadPrice, 6);
        Assert.Equal(1.1002, r.EntryFtmo!.Value, 6);
        Assert.Equal(1.0950, r.SlFtmo!.Value, 6);
        Assert.Equal(1.1100, r.Tp1Ftmo!.Value, 6);
        Assert.Equal(1.1200, r.Tp2Ftmo!.Value, 6);
        Assert.True(r.StopPipsFtmo > r.StopPips);
    }

    [Fact]
    public void Sell_FtmoShiftsSlAndTpsByFullSpread()
    {
        var input = BaseInput();
        input = new KlEntryLotInput
        {
            AssetType = input.AssetType,
            AccountBalance = input.AccountBalance,
            AccountBalanceFtmo = input.AccountBalanceFtmo,
            RiskPercent = input.RiskPercent,
            EntryPrice = 1.1000,
            StopPrice = 1.1050,
            Tp1 = input.Tp1,
            Tp2 = input.Tp2,
            SpreadPips = input.SpreadPips,
            RoundPrecision = input.RoundPrecision,
            SymbolName = input.SymbolName,
            TickSize = input.TickSize,
            QuoteCurrency = input.QuoteCurrency,
            AutoConvToUsd = input.AutoConvToUsd,
        };
        var r = KlEntryLotCalculator.Compute(input);

        Assert.False(r.IsBuy);
        Assert.Equal(1.1000, r.EntryFtmo!.Value, 6);
        Assert.Equal(1.1052, r.SlFtmo!.Value, 6);
        Assert.Equal(1.1102, r.Tp1Ftmo!.Value, 6);
        Assert.Equal(1.1202, r.Tp2Ftmo!.Value, 6);
    }

    [Fact]
    public void FtmoTpPips_UseFtmoPrices()
    {
        var r = KlEntryLotCalculator.Compute(BaseInput());

        Assert.NotNull(r.PipsToTp1Ftmo);
        Assert.NotNull(r.PipsToTp1);
        Assert.NotEqual(r.PipsToTp1Ftmo, r.PipsToTp1);
    }

    [Fact]
    public void JpyPair_PipIs001()
    {
        var input = new KlEntryLotInput
        {
            AssetType = KlAssetType.Forex,
            AccountBalance = 400,
            RiskPercent = 1,
            EntryPrice = 150.00,
            StopPrice = 149.50,
            Tp1 = 151,
            SpreadPips = 2,
            RoundPrecision = 1000,
            SymbolName = "USDJPY",
            TickSize = 0.001,
            PipSize = 0.01,
            QuoteCurrency = "JPY",
            AutoConvToUsd = 1.0 / 150.0,
        };
        var r = KlEntryLotCalculator.Compute(input);

        Assert.Equal(0.01, r.Pip, 6);
    }

    [Fact]
    public void EurJpy_WithUsdJpyConv_LotSizedInUsd()
    {
        const double usdJpy = 126.0;
        var input = new KlEntryLotInput
        {
            AssetType = KlAssetType.Forex,
            AccountBalanceFtmo = 10_000,
            RiskPercent = 1,
            EntryPrice = 126.20,
            StopPrice = 126.06,
            Tp1 = 126.48,
            SpreadPips = 0.5,
            RoundPrecision = 1000,
            SymbolName = "EURJPY",
            TickSize = 0.001,
            PipSize = 0.01,
            QuoteCurrency = "JPY",
            AutoConvToUsd = 1.0 / usdJpy,
        };
        var r = KlEntryLotCalculator.Compute(input);

        Assert.NotNull(r.RoundDisplayedFtmo);
        Assert.True(r.RoundDisplayedFtmo > 0.5,
            $"EURJPY lot should be ~0.8+ with conv, got {r.RoundDisplayedFtmo}");
        Assert.True(r.EstLossFtmo is > 90 and < 110,
            $"Expected ~$100 risk, got estLoss={r.EstLossFtmo}");
    }

    [Fact]
    public void JpyQuote_WithoutConv_NoLot()
    {
        var input = new KlEntryLotInput
        {
            AssetType = KlAssetType.Forex,
            AccountBalanceFtmo = 10_000,
            RiskPercent = 1,
            EntryPrice = 126.20,
            StopPrice = 126.06,
            Tp1 = 126.48,
            SpreadPips = 0.5,
            RoundPrecision = 1000,
            SymbolName = "EURJPY",
            TickSize = 0.001,
            PipSize = 0.01,
            QuoteCurrency = "JPY",
        };
        var r = KlEntryLotCalculator.Compute(input);

        Assert.Null(r.RoundDisplayedFtmo);
    }

    [Fact]
    public void FtmoLot_UsesFtmoBalance_NotMidAccount()
    {
        var r = KlEntryLotCalculator.Compute(BaseInput());

        Assert.Equal(20_000, r.AccForFtmo);
        Assert.Equal(200, r.TargetRiskUsdFtmo!.Value, 1);
        Assert.Equal(4, r.EstLoss!.Value, 1);
        Assert.True(r.RoundDisplayedFtmo > r.RoundDisplayed * 40);
        Assert.True(r.EstLossFtmo > r.EstLoss * 40);
    }

    [Fact]
    public void FtmoMetrics_EmptyWhenFtmoBalanceZero()
    {
        var input = BaseInput();
        input = new KlEntryLotInput
        {
            AssetType = input.AssetType,
            AccountBalance = input.AccountBalance,
            AccountBalanceFtmo = 0,
            RiskPercent = input.RiskPercent,
            EntryPrice = input.EntryPrice,
            StopPrice = input.StopPrice,
            Tp1 = input.Tp1,
            Tp2 = input.Tp2,
            SpreadPips = input.SpreadPips,
            RoundPrecision = input.RoundPrecision,
            SymbolName = input.SymbolName,
            TickSize = input.TickSize,
            QuoteCurrency = input.QuoteCurrency,
            AutoConvToUsd = input.AutoConvToUsd,
        };
        var r = KlEntryLotCalculator.Compute(input);

        Assert.Equal(0, r.AccForFtmo);
        Assert.Null(r.TargetRiskUsdFtmo);
        Assert.Null(r.RoundDisplayedFtmo);
        Assert.Null(r.EstLossFtmo);
    }

    [Fact]
    public void BtcUsd_UsesSymbolPipSize_NotTickSize()
    {
        var r = KlEntryLotCalculator.Compute(new KlEntryLotInput
        {
            AssetType = KlAssetType.BTCUSD,
            AccountBalance = 400,
            AccountBalanceFtmo = 20_000,
            RiskPercent = 1,
            EntryPrice = 100_000,
            StopPrice = 99_000,
            Tp1 = 102_000,
            SpreadPips = 1,
            RoundPrecision = 100_000,
            SymbolName = "BTCUSD",
            TickSize = 0.01,
            PipSize = 1.0,
            IsCryptoSymbol = true,
            QuoteCurrency = "USD",
            AutoConvToUsd = 1,
        });

        Assert.Equal(1.0, r.Pip, 6);
        Assert.Equal(1.0, r.SpreadPrice, 6);
    }

    [Fact]
    public void ResolveAutoSpreadPips_ConvertsSpreadPriceToPips()
    {
        Assert.Equal(1.5, KlEntryLotCalculator.ResolveAutoSpreadPips(0.00015, 0.0001), 6);
        Assert.Equal(12.0, KlEntryLotCalculator.ResolveAutoSpreadPips(12.0, 1.0), 6);
    }

    [Fact]
    public void RewardToRisk_DividesTpPipsBySlPips()
    {
        var rr = KlEntryLotCalculator.RewardToRisk(100, 50);
        Assert.Equal(2.0, rr!.Value, 4);
    }

    [Fact]
    public void ForwardSpreadOnBidLevels_BuyShiftsEntryOnly()
    {
        var entry = 1.1000;
        var stop = 1.0950;
        var tp1 = 1.1100;
        var tp2 = 1.1200;

        KlEntryLotCalculator.ForwardSpreadOnBidLevels(ref entry, ref stop, ref tp1, ref tp2, 2, 0.0001, isBuy: true);

        Assert.Equal(1.1002, entry, 6);
        Assert.Equal(1.0950, stop, 6);
        Assert.Equal(1.1100, tp1, 6);
        Assert.Equal(1.1200, tp2, 6);
    }

    [Fact]
    public void ForwardSpreadOnBidLevels_SellShiftsSlAndTps()
    {
        var entry = 1.1000;
        var stop = 1.1050;
        var tp1 = 1.0900;
        var tp2 = 1.0800;

        KlEntryLotCalculator.ForwardSpreadOnBidLevels(ref entry, ref stop, ref tp1, ref tp2, 2, 0.0001, isBuy: false);

        Assert.Equal(1.1000, entry, 6);
        Assert.Equal(1.1052, stop, 6);
        Assert.Equal(1.0902, tp1, 6);
        Assert.Equal(1.0802, tp2, 6);
    }

    [Fact]
    public void ReverseSpreadOnBidLevels_RoundTripsForward()
    {
        var baseEntry = 1.1000;
        var baseStop = 1.0950;
        var baseTp1 = 1.1100;
        var baseTp2 = 1.1200;

        var entry = baseEntry;
        var stop = baseStop;
        var tp1 = baseTp1;
        var tp2 = baseTp2;
        KlEntryLotCalculator.ForwardSpreadOnBidLevels(ref entry, ref stop, ref tp1, ref tp2, 2, 0.0001, isBuy: true);
        KlEntryLotCalculator.ReverseSpreadOnBidLevels(ref entry, ref stop, ref tp1, ref tp2, 2, 0.0001, isBuy: true);

        Assert.Equal(baseEntry, entry, 6);
        Assert.Equal(baseStop, stop, 6);
        Assert.Equal(baseTp1, tp1, 6);
        Assert.Equal(baseTp2, tp2, 6);
    }

    [Fact]
    public void DefaultSeedDistances_AtrMultiples_ConvertToPips()
    {
        var d = KlEntryLotCalculator.ComputeAtrSeedDistances(
            atrPrice: 0.0010,
            pip: 0.0001,
            slAtrMult: 1.0,
            tp1AtrMult: 2.0,
            tp2AtrMult: 4.0);

        Assert.Equal(10, d.SlPips);
        Assert.Equal(20, d.Tp1Pips);
        Assert.Equal(40, d.Tp2Pips);
    }

    [Fact]
    public void DefaultSeedDistances_AtrFallbackWhenZero()
    {
        var d = KlEntryLotCalculator.ComputeAtrSeedDistances(0, 0.0001, 1, 2, 4);
        Assert.Equal(50, d.SlPips);
        Assert.Equal(100, d.Tp1Pips);
        Assert.Equal(200, d.Tp2Pips);
    }

    [Fact]
    public void DefaultSeedDistances_ManualPipsOverrideAtr()
    {
        var d = KlEntryLotCalculator.ResolveDefaultSeedDistancesFromAtr(
            0.0010, 0.0001, 1, 2, 4,
            overrideSlPips: 30, overrideTp1Pips: 60, overrideTp2Pips: 90);

        Assert.Equal(30, d.SlPips);
        Assert.Equal(60, d.Tp1Pips);
        Assert.Equal(90, d.Tp2Pips);
    }
}
