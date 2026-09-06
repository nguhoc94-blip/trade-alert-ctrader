using System.Collections.Generic;
using TradeAlert.BacktestRobot.Execution;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models;
using TradeAlert.Core.Models.Alerts;
using TradeAlert.Core.Models.KeyLevel;
using TradeAlert.Core.Series;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests;

public class AtrSlWidthMultiplierTests
{
    [Theory]
    [InlineData(1.0, 2.0, 1.0, 2.0)]
    [InlineData(1.5, 2.0, 1.0, 3.0)]
    [InlineData(0.75, 2.0, 1.0, 1.5)]
    public void ComputeDynamicMultRaw_Formula(double atrRatio, double baseMult, double factor, double expected)
    {
        var raw = AtrSlWidthMultiplierCalculator.ComputeDynamicMultRaw(baseMult, atrRatio, factor);
        Assert.Equal(expected, raw, 6);
    }

    [Fact]
    public void ComputeDynamicMultRaw_FactorDampens()
    {
        var full = AtrSlWidthMultiplierCalculator.ComputeDynamicMultRaw(2.0, 1.5, 1.0);
        var half = AtrSlWidthMultiplierCalculator.ComputeDynamicMultRaw(2.0, 1.5, 0.5);
        Assert.Equal(3.0, full, 6);
        Assert.Equal(2.5, half, 6);
    }

    [Theory]
    [InlineData(5.0, 1.0, 4.0, 4.0)]
    [InlineData(0.5, 1.0, 4.0, 1.0)]
    [InlineData(2.5, 1.0, 4.0, 2.5)]
    public void ClampMult_RespectsBounds(double raw, double min, double max, double expected)
    {
        Assert.Equal(expected, AtrSlWidthMultiplierCalculator.ClampMult(raw, min, max), 6);
    }

    [Fact]
    public void Resolve_FallbackWhenSeriesNull()
    {
        var cfg = new AtrSlWidthAdjustConfig
        {
            UseAtrAdjustedSlWidthMultiplier = true,
            BaseSlWidthMult = 2.0,
            AtrAdjustFallbackToBase = true,
        };

        var result = AtrSlWidthMultiplierCalculator.Resolve(in cfg, series: null);

        Assert.True(result.Success);
        Assert.True(result.UsedFallback);
        Assert.Equal(2.0, result.DynamicSlWidthMult, 6);
    }

    [Fact]
    public void Resolve_SkipsWhenSeriesNullAndNoFallback()
    {
        var cfg = new AtrSlWidthAdjustConfig
        {
            UseAtrAdjustedSlWidthMultiplier = true,
            AtrAdjustFallbackToBase = false,
        };

        var result = AtrSlWidthMultiplierCalculator.Resolve(in cfg, series: null);

        Assert.False(result.Success);
        Assert.Equal("ATR adjustment unavailable", result.SkipReason);
    }

    static PineStateEngine StateWithSwing(int type, int bar, double keyTop, double keyBottom)
    {
        var s = new PineStateEngine();
        var idx = s.Pivots.PushPivot(keyBottom, bar, type, 0, 1, 1);
        s.Pivots.SetFlag(idx, 1);
        s.Pivots.SetHasKey(idx, true);
        s.Pivots.SetKeyBox(idx, new KeyBoxRef { Spec = new KeyBoxSpec { Top = keyTop, Bottom = keyBottom } });
        return s;
    }

    static TradeMapperConfig BuyCfg(double slMult) => new()
    {
        SymbolName = "XAUUSD",
        TickSize = 0.01,
        PipSize = 0.01,
        RewardRisk = 2.0,
        SlWidthMult = slMult,
        LabelPrefix = "L6BT|",
        UseSwingCEdgeTakeProfit = false,
        AtrSlWidthAdjust = new AtrSlWidthAdjustConfig { UseAtrAdjustedSlWidthMultiplier = false },
    };

    static CompoundFireEvent BuyFire() =>
        new(0, "R1", SignalDirection.Buy, "15", 100, System.DateTime.UnixEpoch, 100, 100);

    [Fact]
    public void Mapper_Buy_SlFromEntryMinusKeyWidthTimesMult()
    {
        var s = StateWithSwing(SwingBResolver.TypeLow, 50, 100, 99);
        var plan = CompoundFireTradeMapper.TryBuild(BuyFire(), s, BuyCfg(slMult: 2.0), out _);

        Assert.NotNull(plan);
        Assert.Equal(100.0, plan!.EntryLimit, 6);
        Assert.Equal(98.0, plan.StopLoss, 6);
        Assert.Equal(2.0, plan.DynamicSlWidthMult, 6);
    }

    [Fact]
    public void Mapper_Sell_SlFromEntryPlusKeyWidthTimesMult()
    {
        var s = StateWithSwing(SwingBResolver.TypeHigh, 50, 101, 100);
        var plan = CompoundFireTradeMapper.TryBuild(
            new(0, "R1", SignalDirection.Sell, "15", 100, System.DateTime.UnixEpoch, 100, 100),
            s, BuyCfg(slMult: 2.0), out _);

        Assert.NotNull(plan);
        Assert.Equal(100.0, plan!.EntryLimit, 6);
        Assert.Equal(102.0, plan.StopLoss, 6);
    }

    static SeriesBuffer BuildFlatRangeSeries(int barCount, double range)
    {
        var buf = new SeriesBuffer();
        for (var i = 0; i < barCount; i++)
        {
            var snap = new BarSnapshot(
                OpenChartTimeLocal: new System.DateTime(2020, 1, 1).AddMinutes(i * 15),
                Open: 100,
                High: 100 + range,
                Low: 100,
                Close: 100 + range * 0.5);
            buf.Upsert(i, snap, default);
        }

        return buf;
    }

    [Fact]
    public void Resolve_FlatAtrSeries_RatioNearOne()
    {
        var series = BuildFlatRangeSeries(200, range: 1.0);
        var cfg = new AtrSlWidthAdjustConfig
        {
            BaseSlWidthMult = 2.0,
            AtrAdjustPeriod = 14,
            AtrBaselinePeriod = 20,
            AtrAdjustmentFactor = 1.0,
            AtrAdjustUseClosedBar = false,
        };

        var result = AtrSlWidthMultiplierCalculator.Resolve(in cfg, series);

        Assert.True(result.Success);
        Assert.InRange(result.AtrRatio, 0.95, 1.05);
        Assert.Equal(2.0, result.DynamicSlWidthMult, 1);
    }
}
