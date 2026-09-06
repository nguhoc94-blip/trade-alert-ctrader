using TradeAlert.BacktestRobot.Execution;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.Alerts;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests;

public class StalePendingSwingCancelRuleTests
{
    static PendingOrderContext Ctx(int fireBar = 100, int slot = 0) => new()
    {
        Label = "L6BT|R1|B50|C",
        RuleSlot = slot,
        Direction = SignalDirection.Buy,
        IsBuy = true,
        SwingBPivotBar = 50,
        SwingBTfToken = "15",
        FireChartBarIndex = fireBar,
    };

    static StalePendingSwingCancelConfig Cfg(int threshold = 10) => new()
    {
        RuleSlotIndices = { 0 },
        ActiveSwingThreshold = threshold,
    };

    static int AddPivot(PineStateEngine s, int type, int bar, int flag = SwingBResolver.FlagActive)
    {
        var idx = s.Pivots.PushPivot(100, bar, type, 0, 1, 1);
        s.Pivots.SetFlag(idx, flag);
        return idx;
    }

    [Fact]
    public void NoCancel_WhenFireBarMissing()
    {
        var s = new PineStateEngine();
        var ctx = Ctx(fireBar: -1);
        var cfg = Cfg();
        var r = StalePendingSwingCancelRule.Evaluate(s, in ctx, in cfg);
        Assert.False(r.ShouldCancel);
    }

    [Fact]
    public void NoCancel_WhenFewNewActiveSwings()
    {
        var s = new PineStateEngine();
        for (var bar = 101; bar <= 109; bar++)
            AddPivot(s, SwingBResolver.TypeHigh, bar);

        var ctx = Ctx();
        var cfg = Cfg();
        var r = StalePendingSwingCancelRule.Evaluate(s, in ctx, in cfg);
        Assert.False(r.ShouldCancel);
        Assert.Equal(9, r.NewSwingCount);
    }

    [Fact]
    public void Cancels_WhenTenNewSwingsSinceFire()
    {
        var s = new PineStateEngine();
        for (var bar = 101; bar <= 110; bar++)
            AddPivot(s, SwingBResolver.TypeHigh, bar);

        var ctx = Ctx();
        var cfg = Cfg();
        var r = StalePendingSwingCancelRule.Evaluate(s, in ctx, in cfg);
        Assert.True(r.ShouldCancel);
        Assert.Equal(StalePendingSwingCancelTrigger.TooManyNewSwings, r.Trigger);
        Assert.Equal(10, r.NewSwingCount);
    }

    [Fact]
    public void IgnoresSwingsAtOrBeforeFireBar()
    {
        var s = new PineStateEngine();
        for (var bar = 90; bar <= 100; bar++)
            AddPivot(s, SwingBResolver.TypeHigh, bar);
        for (var bar = 101; bar <= 105; bar++)
            AddPivot(s, SwingBResolver.TypeHigh, bar);

        var ctx = Ctx(fireBar: 100);
        var cfg = Cfg();
        var r = StalePendingSwingCancelRule.Evaluate(s, in ctx, in cfg);
        Assert.False(r.ShouldCancel);
        Assert.Equal(5, r.NewSwingCount);
    }

    [Fact]
    public void NoCancel_WhenOnlyDSwingAfterFire()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeHigh, 105, SwingBResolver.FlagDSwing);

        var ctx = Ctx();
        var cfg = Cfg();
        var r = StalePendingSwingCancelRule.Evaluate(s, in ctx, in cfg);
        Assert.False(r.ShouldCancel);
        Assert.Equal(0, r.NewSwingCount);
    }

    [Fact]
    public void NoCancel_WhenOnlyBrokenSwingsAfterFire()
    {
        var s = new PineStateEngine();
        for (var bar = 101; bar <= 110; bar++)
            AddPivot(s, SwingBResolver.TypeHigh, bar, flag: 2);

        var ctx = Ctx();
        var cfg = Cfg();
        var r = StalePendingSwingCancelRule.Evaluate(s, in ctx, in cfg);
        Assert.False(r.ShouldCancel);
        Assert.Equal(0, r.NewSwingCount);
    }

    [Fact]
    public void ThresholdIsConfigurable()
    {
        var s = new PineStateEngine();
        for (var bar = 101; bar <= 105; bar++)
            AddPivot(s, SwingBResolver.TypeHigh, bar);

        var ctx = Ctx();
        var cfg = Cfg(threshold: 5);
        var r = StalePendingSwingCancelRule.Evaluate(s, in ctx, in cfg);
        Assert.True(r.ShouldCancel);
        Assert.Equal(StalePendingSwingCancelTrigger.TooManyNewSwings, r.Trigger);
    }
}
