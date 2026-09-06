using System.Collections.Generic;
using TradeAlert.BacktestRobot.Execution;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.Alerts;
using TradeAlert.Core.Models.KeyLevel;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests;

public class SwingCEdgeTpUpdaterTests
{
    static readonly HashSet<int> AllSlots = new() { 0, 1, 2, 3, 4, 5 };

    static void AddPivot(
        PineStateEngine s,
        int type,
        int bar,
        double price,
        double? keyTop = null,
        double? keyBottom = null,
        int flag = 1,
        bool brokenWaitingD = false)
    {
        var idx = s.Pivots.PushPivot(price, bar, type, 0, 1, 1);
        s.Pivots.SetFlag(idx, flag);
        if (brokenWaitingD)
            s.Pivots.SetFirstDIdx(idx, PivotStateStore.FirstDWaiting);
        if (keyTop is not null && keyBottom is not null)
        {
            s.Pivots.SetHasKey(idx, true);
            s.Pivots.SetKeyBox(idx, new KeyBoxRef
            {
                Spec = new KeyBoxSpec { Top = keyTop.Value, Bottom = keyBottom.Value },
            });
        }
    }

    static TrackedSetup BuySetup(double entry = 100, double initialTp = 110, int slot = 0, double sl = 98) => new()
    {
        Context = new PendingOrderContext
        {
            Label = "L6BT|R1|B50",
            RuleSlot = slot,
            Direction = SignalDirection.Buy,
            IsBuy = true,
            SwingBPivotBar = 50,
            EntryPrice = entry,
            StopLoss = sl,
            OriginalTakeProfit = initialTp,
        },
        CurrentTakeProfit = initialTp,
    };

    [Fact]
    public void Update_Buy_RepointsTpToConfirmedCLowerEdge()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);
        AddPivot(s, SwingBResolver.TypeHigh, 70, 105, keyTop: 106, keyBottom: 104, flag: 1);

        var action = SwingCEdgeTpUpdater.Evaluate(
            BuySetup(entry: 100, initialTp: 110), s,
            wantsLiveTpUpdate: true, useSwingC: true, AllSlots, tickSize: 0.01);

        Assert.True(action.ShouldUpdate);
        Assert.Equal(104, action.NewTakeProfit, 6);
        Assert.Equal(70, action.SwingC!.PivotBar);
    }

    [Fact]
    public void Update_SkipsWhenNoConfirmedC()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);

        var action = SwingCEdgeTpUpdater.Evaluate(
            BuySetup(), s, wantsLiveTpUpdate: true, useSwingC: true, AllSlots, tickSize: 0.01);

        Assert.False(action.ShouldUpdate);
        Assert.Contains("no confirmed swing C", action.Reason);
    }

    [Fact]
    public void Update_SkipsWhenAlreadyUpdated()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);
        AddPivot(s, SwingBResolver.TypeHigh, 70, 105, keyTop: 106, keyBottom: 104, flag: 1);

        var setup = BuySetup();
        setup.TpUpdatedToSwingC = true;

        var action = SwingCEdgeTpUpdater.Evaluate(
            setup, s, wantsLiveTpUpdate: true, useSwingC: true, AllSlots, tickSize: 0.01);

        Assert.False(action.ShouldUpdate);
        Assert.Equal("already-updated", action.Reason);
    }

    [Fact]
    public void Update_SkipsWhenBBrokenTpLocked()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);
        AddPivot(s, SwingBResolver.TypeHigh, 70, 105, keyTop: 106, keyBottom: 104, flag: 1);

        var setup = BuySetup();
        setup.TpMovedToEntryOnBBroken = true;

        var action = SwingCEdgeTpUpdater.Evaluate(
            setup, s, wantsLiveTpUpdate: true, useSwingC: true, AllSlots, tickSize: 0.01);

        Assert.False(action.ShouldUpdate);
        Assert.Equal("B-broken-tp-locked", action.Reason);
    }

    [Fact]
    public void Update_SkipsWhenMaskExcludesSlot()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);
        AddPivot(s, SwingBResolver.TypeHigh, 70, 105, keyTop: 106, keyBottom: 104, flag: 1);

        var action = SwingCEdgeTpUpdater.Evaluate(
            BuySetup(slot: 2), s, wantsLiveTpUpdate: true, useSwingC: true,
            new HashSet<int> { 0 }, tickSize: 0.01);

        Assert.False(action.ShouldUpdate);
        Assert.Equal("mask-not-applied", action.Reason);
    }

    [Fact]
    public void Update_SkipsWhenDisabled()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);
        AddPivot(s, SwingBResolver.TypeHigh, 70, 105, keyTop: 106, keyBottom: 104, flag: 1);

        var action = SwingCEdgeTpUpdater.Evaluate(
            BuySetup(), s, wantsLiveTpUpdate: false, useSwingC: true, AllSlots, tickSize: 0.01);

        Assert.False(action.ShouldUpdate);
        Assert.Equal("disabled", action.Reason);
    }

    [Fact]
    public void Update_SkipsWhenAlreadyAtCEdge()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);
        AddPivot(s, SwingBResolver.TypeHigh, 70, 105, keyTop: 106, keyBottom: 104, flag: 1);

        var action = SwingCEdgeTpUpdater.Evaluate(
            BuySetup(entry: 100, initialTp: 104), s,
            wantsLiveTpUpdate: true, useSwingC: true, AllSlots, tickSize: 0.01);

        Assert.False(action.ShouldUpdate);
        Assert.Equal("already-at-c-edge", action.Reason);
    }

    [Fact]
    public void Update_NoTpUntilC_UpdatesEvenWhenCurrentTpIsZero()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);
        AddPivot(s, SwingBResolver.TypeHigh, 70, 105, keyTop: 106, keyBottom: 104, flag: 1);

        // NoTpUntilC: initial TP = 0 (no TP placed).
        var action = SwingCEdgeTpUpdater.Evaluate(
            BuySetup(entry: 100, initialTp: 0), s,
            wantsLiveTpUpdate: true, useSwingC: true, AllSlots, tickSize: 0.01,
            tpMode: SwingCTpMode.NoTpUntilC);

        Assert.True(action.ShouldUpdate);
        Assert.Equal(104, action.NewTakeProfit, 6);
    }

    [Fact]
    public void Update_CancelsWhenSwingCRrBelowBaseAndSkipEnabled()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);
        AddPivot(s, SwingBResolver.TypeHigh, 70, 101.5, keyTop: 102, keyBottom: 101, flag: 1);

        var action = SwingCEdgeTpUpdater.Evaluate(
            BuySetup(entry: 100, initialTp: 0, sl: 98), s,
            wantsLiveTpUpdate: true, useSwingC: true, AllSlots, tickSize: 0.01,
            tpMode: SwingCTpMode.NoTpUntilC, minRewardRisk: 2.0, skipIfSwingCRrBelowBase: true);

        Assert.True(action.ShouldCancel);
        Assert.False(action.ShouldUpdate);
        Assert.Contains("Swing-C RR", action.Reason);
    }

    [Fact]
    public void Update_Buy_BumpsTpToBaseRrWhenCEdgeTooClose()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);
        // C lower edge 101 ? 0.5R with entry=100 sl=98; base RR=2 ? TP=104
        AddPivot(s, SwingBResolver.TypeHigh, 70, 101.5, keyTop: 102, keyBottom: 101, flag: 1);

        var action = SwingCEdgeTpUpdater.Evaluate(
            BuySetup(entry: 100, initialTp: 110, sl: 98), s,
            wantsLiveTpUpdate: true, useSwingC: true, AllSlots, tickSize: 0.01,
            minRewardRisk: 2.0);

        Assert.True(action.ShouldUpdate);
        Assert.Equal(104, action.NewTakeProfit, 6);
    }
}
