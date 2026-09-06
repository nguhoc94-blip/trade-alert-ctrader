using TradeAlert.BacktestRobot.Execution;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.Alerts;
using TradeAlert.Core.Models.KeyLevel;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests;

public class SwingCBrokenWaitDCancelRuleTests
{
    static PendingOrderContext Ctx(int cBar = 70, int slot = 0) => new()
    {
        Label = "L6BT|R1|B50|C",
        RuleSlot = slot,
        Direction = SignalDirection.Buy,
        IsBuy = true,
        SwingBPivotBar = 50,
        SwingCPivotBar = cBar,
        SwingBTfToken = "15",
    };

    static int AddPivot(PineStateEngine s, int type, int bar, int flag, bool waitD = false)
    {
        var idx = s.Pivots.PushPivot(100, bar, type, 0, 1, 1);
        s.Pivots.SetFlag(idx, flag);
        if (waitD)
            s.Pivots.SetFirstDIdx(idx, PivotStateStore.FirstDWaiting);
        return idx;
    }

    [Fact]
    public void NoCancel_WhenSwingCBarMissing()
    {
        var s = new PineStateEngine();
        var ctx = Ctx(cBar: 0);
        var r = SwingCBrokenWaitDCancelRule.Evaluate(s, ctx);
        Assert.False(r.ShouldCancel);
    }

    [Fact]
    public void NoCancel_WhenCStillActive()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeHigh, 70, SwingBResolver.FlagActive);
        var ctx = Ctx();
        var r = SwingCBrokenWaitDCancelRule.Evaluate(s, ctx);
        Assert.False(r.ShouldCancel);
    }

    [Fact]
    public void NoCancel_WhenCIsDSwing()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeHigh, 70, SwingCEdgeTakeProfitResolver.FlagDSwing);
        var ctx = Ctx();
        var r = SwingCBrokenWaitDCancelRule.Evaluate(s, ctx);
        Assert.False(r.ShouldCancel);
    }

    [Fact]
    public void Cancels_WhenCTransitionsToBrokenWaitD()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeHigh, 70, flag: 2, waitD: true);
        var ctx = Ctx();
        var r = SwingCBrokenWaitDCancelRule.Evaluate(s, ctx);
        Assert.True(r.ShouldCancel);
        Assert.Equal(70, r.SwingCPivotBar);
        Assert.Equal(2, r.SwingCFlag);
        Assert.Equal(PivotStateStore.FirstDWaiting, r.SwingCFirstDIdx);
    }

    [Fact]
    public void NoCancel_WhenBrokenButNotWaitingD()
    {
        var s = new PineStateEngine();
        var idx = AddPivot(s, SwingBResolver.TypeHigh, 70, flag: 2);
        s.Pivots.SetFirstDIdx(idx, PivotStateStore.FirstDNa);
        var ctx = Ctx();
        var r = SwingCBrokenWaitDCancelRule.Evaluate(s, ctx);
        Assert.False(r.ShouldCancel);
    }

    [Fact]
    public void Cancels_DLeg_WhenEntryCPivotBrokenViaSwingCEntryPivotBar()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeHigh, 70, flag: 2, waitD: true);
        AddPivot(s, SwingBResolver.TypeHigh, 90, SwingBResolver.FlagActive);

        var ctx = new PendingOrderContext
        {
            Label = "L6BT|R1|B50|D",
            RuleSlot = 0,
            Direction = SignalDirection.Buy,
            IsBuy = true,
            SwingBPivotBar = 50,
            SwingCPivotBar = 90,
            SwingCEntryPivotBar = 70,
            SwingBTfToken = "15",
        };

        var r = SwingCBrokenWaitDCancelRule.Evaluate(s, ctx);
        Assert.True(r.ShouldCancel);
        Assert.Equal(70, r.SwingCPivotBar);
    }

    [Fact]
    public void ResolveSwingCEntryPivotBar_PrefersExplicitEntryBar()
    {
        var ctx = new PendingOrderContext { SwingCPivotBar = 90, SwingCEntryPivotBar = 70 };
        Assert.Equal(70, SwingCBrokenWaitDCancelRule.ResolveSwingCEntryPivotBar(in ctx));

        var legacy = new PendingOrderContext { SwingCPivotBar = 70, SwingCEntryPivotBar = 0 };
        Assert.Equal(70, SwingCBrokenWaitDCancelRule.ResolveSwingCEntryPivotBar(in legacy));
    }

    [Fact]
    public void FormatPairCancelLog()
    {
        var log = SwingCBrokenWaitDCancelRule.FormatPairCancelLog("L6BT|R1|B50|C", "L6BT|R1|B50|D");
        Assert.Contains("PAIR-CANCEL", log);
        Assert.Contains("L6BT|R1|B50|D", log);
        Assert.Contains(SwingCBrokenWaitDCancelRule.CancelReason, log);
    }

    [Fact]
    public void Resolver_RejectsBrokenWaitD_AsEntryC()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, 50, SwingBResolver.FlagActive);
        AddPivot(s, SwingBResolver.TypeHigh, 70, flag: 2, waitD: true);
        s.Pivots.SetHasKey(1, true);
        s.Pivots.SetKeyBox(1, new KeyBoxRef { Spec = new KeyBoxSpec { Top = 106, Bottom = 104 } });

        var result = SwingCEdgeTakeProfitResolver.TryResolve(s, isBuy: true, swingBBar: 50, entry: 100, out var reason);
        Assert.Null(result);
        Assert.Contains("no confirmed swing C", reason);
    }

    [Fact]
    public void Resolver_AcceptsDSwing_AsEntryC()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, 50, SwingBResolver.FlagActive);
        var cIdx = AddPivot(s, SwingBResolver.TypeHigh, 70, SwingCEdgeTakeProfitResolver.FlagDSwing);
        s.Pivots.SetHasKey(cIdx, true);
        s.Pivots.SetKeyBox(cIdx, new KeyBoxRef { Spec = new KeyBoxSpec { Top = 106, Bottom = 104 } });

        var result = SwingCEdgeTakeProfitResolver.TryResolve(s, isBuy: true, swingBBar: 50, entry: 100, out var reason);
        Assert.NotNull(result);
        Assert.Equal("ok", reason);
        Assert.Equal(70, result!.PivotBar);
    }
}
