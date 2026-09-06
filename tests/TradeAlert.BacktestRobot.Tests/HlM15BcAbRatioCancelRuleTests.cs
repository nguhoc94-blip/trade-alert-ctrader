using TradeAlert.BacktestRobot.Execution;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.Alerts;
using TradeAlert.Core.Models.KeyLevel;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests;

public class HlM15BcAbRatioCancelRuleTests
{
    static void AddPivot(
        PineStateEngine s,
        int bar,
        int type,
        double price,
        double? keyTop = null,
        double? keyBottom = null)
    {
        var idx = s.Pivots.PushPivot(price, bar, type, 0, 1, 1);
        if (keyTop.HasValue && keyBottom.HasValue)
        {
            s.Pivots.SetHasKey(idx, true);
            s.Pivots.SetKeyExtending(idx, true);
            s.Pivots.SetKeyBox(idx, new KeyBoxRef { Spec = new KeyBoxSpec { Top = keyTop.Value, Bottom = keyBottom.Value } });
        }
    }

    static PineStateEngine ChartBuyAbc(int aBar, double aTop, double bBottom, int bBar, int cBar, double cTop)
    {
        var s = new PineStateEngine();
        AddPivot(s, aBar, SwingBResolver.TypeHigh, aTop, aTop, aTop - 1);
        AddPivot(s, bBar, SwingBResolver.TypeLow, bBottom, bBottom + 1, bBottom);
        AddPivot(s, cBar, SwingBResolver.TypeHigh, cTop, cTop, cTop - 1);
        return s;
    }

    static PineStateEngine ChartSellAbc(int aBar, double aBottom, double bTop, int bBar, int cBar, double cBottom)
    {
        var s = new PineStateEngine();
        AddPivot(s, aBar, SwingBResolver.TypeLow, aBottom, aBottom + 1, aBottom);
        AddPivot(s, bBar, SwingBResolver.TypeHigh, bTop, bTop, bTop - 1);
        AddPivot(s, cBar, SwingBResolver.TypeLow, cBottom, cBottom + 1, cBottom);
        return s;
    }

    static HlM15BcAbRatioCancelRuleConfig Cfg(double threshold = 2.6, bool verbose = false) => new()
    {
        RuleSlotIndices = new System.Collections.Generic.HashSet<int> { 2, 3 },
        BcAbRatioThreshold = threshold,
        VerboseSkipLog = verbose,
    };

    static PendingOrderContext BuyR3(int bBar = 50, double bLow = 100, double bHigh = 101) => new()
    {
        Label = "L6BT|R3|B50",
        RuleSlot = 2,
        Direction = SignalDirection.Buy,
        IsBuy = true,
        SwingBPivotBar = bBar,
        SwingBTfToken = "15",
        SwingBKeyLow = bLow,
        SwingBKeyHigh = bHigh,
    };

    static PendingOrderContext SellR4(int bBar = 50, double bLow = 99, double bHigh = 100) => new()
    {
        Label = "L6BT|R4|B50",
        RuleSlot = 3,
        Direction = SignalDirection.Sell,
        IsBuy = false,
        SwingBPivotBar = bBar,
        SwingBTfToken = "15",
        SwingBKeyLow = bLow,
        SwingBKeyHigh = bHigh,
    };

    [Fact]
    public void ParseRulesMask_HlM15_IncludesR3R4Only()
    {
        var slots = HlM15BcAbRatioCancelRuleConfig.ParseRulesMask("hlM15");
        Assert.Equal(2, slots.Count);
        Assert.Contains(2, slots);
        Assert.Contains(3, slots);
    }

    [Fact]
    public void BuyR3_BcGreaterThanThreshold_Cancels()
    {
        // AB=10 (110-100), BC=30 (130-100), 30 > 2.6*10
        var chart = ChartBuyAbc(aBar: 40, aTop: 110, bBottom: 100, bBar: 50, cBar: 55, cTop: 130);
        var result = HlM15BcAbRatioCancelRule.Evaluate(chart, BuyR3(), Cfg());

        Assert.True(result.ShouldCancel);
        Assert.Equal(40, result.ABar);
        Assert.Equal(50, result.BBar);
        Assert.Equal(55, result.CBar);
        Assert.Equal(10, result.AB, 6);
        Assert.Equal(30, result.BC, 6);
        Assert.Equal(3, result.BcAbRatio, 6);
    }

    [Fact]
    public void BuyR3_BcBelowThreshold_DoesNotCancel()
    {
        // AB=10, BC=25, 25 <= 26
        var chart = ChartBuyAbc(aBar: 40, aTop: 110, bBottom: 100, bBar: 50, cBar: 55, cTop: 125);
        var result = HlM15BcAbRatioCancelRule.Evaluate(chart, BuyR3(), Cfg());

        Assert.False(result.ShouldCancel);
    }

    [Fact]
    public void SellR4_BcGreaterThanThreshold_Cancels()
    {
        // AB=10 (100-90), BC=30 (100-70)
        var chart = ChartSellAbc(aBar: 40, aBottom: 90, bTop: 100, bBar: 50, cBar: 55, cBottom: 70);
        var result = HlM15BcAbRatioCancelRule.Evaluate(chart, SellR4(), Cfg());

        Assert.True(result.ShouldCancel);
        Assert.Equal(3, result.BcAbRatio, 6);
    }

    [Fact]
    public void NoA_DoesNotCancel_VerboseSkip()
    {
        var s = new PineStateEngine();
        AddPivot(s, 50, SwingBResolver.TypeLow, 100, 101, 100);
        AddPivot(s, 55, SwingBResolver.TypeHigh, 130, 130, 129);

        var result = HlM15BcAbRatioCancelRule.Evaluate(s, BuyR3(), Cfg(verbose: true));

        Assert.False(result.ShouldCancel);
        Assert.Contains("BCAB SKIP no A", result.VerboseSkip);
    }

    [Fact]
    public void NoC_DoesNotCancel()
    {
        var s = new PineStateEngine();
        AddPivot(s, 40, SwingBResolver.TypeHigh, 110, 110, 109);
        AddPivot(s, 50, SwingBResolver.TypeLow, 100, 101, 100);

        var result = HlM15BcAbRatioCancelRule.Evaluate(s, BuyR3(), Cfg(verbose: true));

        Assert.False(result.ShouldCancel);
        Assert.Null(result.VerboseSkip);
    }

    [Fact]
    public void InvalidGeometry_DoesNotCancel_VerboseSkip()
    {
        // A above B bottom -> negative AB for buy if ATop < BBottom
        var chart = ChartBuyAbc(aBar: 40, aTop: 95, bBottom: 100, bBar: 50, cBar: 55, cTop: 130);
        var result = HlM15BcAbRatioCancelRule.Evaluate(chart, BuyR3(), Cfg(verbose: true));

        Assert.False(result.ShouldCancel);
        Assert.Contains("BCAB SKIP invalid geometry", result.VerboseSkip);
    }

    [Fact]
    public void FormatCancelLog_UsesBcAbRatio()
    {
        var chart = ChartBuyAbc(aBar: 40, aTop: 110, bBottom: 100, bBar: 50, cBar: 55, cTop: 130);
        var ctx = BuyR3();
        var cfg = Cfg();
        var result = HlM15BcAbRatioCancelRule.Evaluate(chart, ctx, cfg);
        var log = HlM15BcAbRatioCancelRule.FormatCancelLog(in ctx, in result, in cfg);

        Assert.Contains("(hl-m15-bcab-ratio)", log);
        Assert.Contains("rule=R3 dir=BUY", log);
        Assert.Contains("BC_AB=3", log);
        Assert.Contains("threshold=2.6", log);
    }
}
