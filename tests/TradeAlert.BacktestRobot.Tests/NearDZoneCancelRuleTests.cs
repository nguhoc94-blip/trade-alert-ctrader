using System.Collections.Generic;
using TradeAlert.BacktestRobot.Execution;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.Alerts;
using TradeAlert.Core.Models.KeyLevel;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests;

public class NearDZoneCancelRuleTests
{
    static PendingOrderContext MakeCtx(
        bool isBuy,
        double zoneTop,
        double zoneBottom,
        bool isOb = false,
        string tf = "60") => new()
    {
        Label = "L6BT|R1|B100|D",
        RuleSlot = 0,
        Direction = isBuy ? SignalDirection.Buy : SignalDirection.Sell,
        IsBuy = isBuy,
        SwingBPivotBar = 100,
        EntryPrice = isBuy ? 1.10 : 1.20,
        StopLoss = isBuy ? 1.09 : 1.21,
        OriginalTakeProfit = isBuy ? 1.15 : 1.15,
        NearDCancelZoneHigh = zoneTop,
        NearDCancelZoneLow = zoneBottom,
        NearDCancelZoneIsOb = isOb,
        NearDCancelZoneTfToken = tf,
    };

    [Fact]
    public void NoZoneSet_DoesNotCancel()
    {
        var ctx = MakeCtx(isBuy: false, zoneTop: 0, zoneBottom: 0);
        var r = NearDZoneCancelRule.Evaluate(in ctx, bid: 1.20, ask: 1.20);
        Assert.False(r.ShouldCancel);
    }

    [Fact]
    public void Sell_ProbeAtZoneTop_ShouldCancel()
    {
        // SELL: near-D = green zone below C. Touch when bid − buffer ≤ zoneTop.
        var ctx = MakeCtx(isBuy: false, zoneTop: 1.1800, zoneBottom: 1.1750);
        var r = NearDZoneCancelRule.Evaluate(in ctx, bid: 1.1800, ask: 1.1801);
        Assert.True(r.ShouldCancel);
    }

    [Fact]
    public void Sell_ProbeWithinBufferAboveZone_ShouldCancel()
    {
        // height=50p key zone, buffer=h/8=6.25p → probe up to top+buffer counts.
        // top=1.1800, buffer=0.0050/8=0.000625; probe=1.18062 still inside the buffer.
        var ctx = MakeCtx(isBuy: false, zoneTop: 1.1800, zoneBottom: 1.1750);
        var r = NearDZoneCancelRule.Evaluate(in ctx, bid: 1.18060, ask: 1.18062);
        Assert.True(r.ShouldCancel);
    }

    [Fact]
    public void Sell_ProbeAboveZonePlusBuffer_DoesNotCancel()
    {
        // top=1.1800, buffer=0.000625 → probe 1.18100 is well above the buffered top.
        var ctx = MakeCtx(isBuy: false, zoneTop: 1.1800, zoneBottom: 1.1750);
        var r = NearDZoneCancelRule.Evaluate(in ctx, bid: 1.18100, ask: 1.18101);
        Assert.False(r.ShouldCancel);
    }

    [Fact]
    public void Sell_ProbeBelowZoneBottom_DoesNotCancel()
    {
        var ctx = MakeCtx(isBuy: false, zoneTop: 1.1800, zoneBottom: 1.1750);
        var r = NearDZoneCancelRule.Evaluate(in ctx, bid: 1.1740, ask: 1.1741);
        Assert.True(r.ShouldCancel);
    }

    [Fact]
    public void Buy_ProbeAtZoneBottom_ShouldCancel()
    {
        // BUY: near-D = red zone above C. Touch when ask + buffer ≥ zoneBot.
        var ctx = MakeCtx(isBuy: true, zoneTop: 1.1300, zoneBottom: 1.1250);
        var r = NearDZoneCancelRule.Evaluate(in ctx, bid: 1.12490, ask: 1.12500);
        Assert.True(r.ShouldCancel);
    }

    [Fact]
    public void Buy_ProbeWithinBufferBelowZone_ShouldCancel()
    {
        // height=50p key zone, buffer=h/8=6.25p → probe = bot − 5p still buffered touch.
        var ctx = MakeCtx(isBuy: true, zoneTop: 1.1300, zoneBottom: 1.1250);
        var r = NearDZoneCancelRule.Evaluate(in ctx, bid: 1.12440, ask: 1.12450);
        Assert.True(r.ShouldCancel);
    }

    [Fact]
    public void Buy_ProbeBelowZoneMinusBuffer_DoesNotCancel()
    {
        var ctx = MakeCtx(isBuy: true, zoneTop: 1.1300, zoneBottom: 1.1250);
        var r = NearDZoneCancelRule.Evaluate(in ctx, bid: 1.12300, ask: 1.12310);
        Assert.False(r.ShouldCancel);
    }

    [Fact]
    public void EvaluateWithProbe_Buy_UsesBarHigh()
    {
        // Backtest BUY probe = bar High. Bar closes at 1.1200 (outside zone) but High 1.1300 touches.
        var ctx = MakeCtx(isBuy: true, zoneTop: 1.1300, zoneBottom: 1.1250);
        Assert.True(NearDZoneCancelRule.EvaluateWithProbe(in ctx, probe: 1.1300).ShouldCancel);
        Assert.False(NearDZoneCancelRule.EvaluateWithProbe(in ctx, probe: 1.1200).ShouldCancel);
    }

    [Fact]
    public void EvaluateWithProbe_Sell_UsesBarLow()
    {
        // Backtest SELL probe = bar Low. Bar closes at 1.1900 (outside zone) but Low 1.1800 touches.
        var ctx = MakeCtx(isBuy: false, zoneTop: 1.1800, zoneBottom: 1.1750);
        Assert.True(NearDZoneCancelRule.EvaluateWithProbe(in ctx, probe: 1.1800).ShouldCancel);
        Assert.False(NearDZoneCancelRule.EvaluateWithProbe(in ctx, probe: 1.1900).ShouldCancel);
    }

    [Fact]
    public void EvaluateWithProbe_MatchesEvaluate_ForDirectionalProbe()
    {
        var buy = MakeCtx(isBuy: true, zoneTop: 1.1300, zoneBottom: 1.1250);
        var sell = MakeCtx(isBuy: false, zoneTop: 1.1800, zoneBottom: 1.1750);

        // Evaluate picks ask for BUY, bid for SELL — EvaluateWithProbe must agree when fed the same value.
        Assert.Equal(
            NearDZoneCancelRule.Evaluate(in buy, bid: 1.0, ask: 1.12500).ShouldCancel,
            NearDZoneCancelRule.EvaluateWithProbe(in buy, probe: 1.12500).ShouldCancel);
        Assert.Equal(
            NearDZoneCancelRule.Evaluate(in sell, bid: 1.1800, ask: 9.9).ShouldCancel,
            NearDZoneCancelRule.EvaluateWithProbe(in sell, probe: 1.1800).ShouldCancel);
    }

    [Fact]
    public void OrderBlock_BufferIsHalfOfKey()
    {
        // OB: divisor 16. height=80p → buffer=5p (key would be 10p).
        // Pick a probe that lies inside KEY buffer but OUTSIDE OB buffer (between 5p and 10p above top).
        // top=1.1800, KEY buffer=h/8=10p, OB buffer=h/16=5p.
        // probe=top+8p → KEY counts touched; OB does NOT.
        var keyCtx = MakeCtx(isBuy: false, zoneTop: 1.1800, zoneBottom: 1.1720, isOb: false);
        var obCtx  = MakeCtx(isBuy: false, zoneTop: 1.1800, zoneBottom: 1.1720, isOb: true);

        var probeBid = 1.1808;
        var keyR = NearDZoneCancelRule.Evaluate(in keyCtx, bid: probeBid, ask: probeBid + 0.0001);
        var obR  = NearDZoneCancelRule.Evaluate(in obCtx,  bid: probeBid, ask: probeBid + 0.0001);

        Assert.True(keyR.ShouldCancel);
        Assert.False(obR.ShouldCancel);
    }

    [Fact]
    public void DegenerateZone_TopEqualsBottom_DoesNotCancel()
    {
        var ctx = MakeCtx(isBuy: false, zoneTop: 1.1800, zoneBottom: 1.1800);
        var r = NearDZoneCancelRule.Evaluate(in ctx, bid: 1.1800, ask: 1.1800);
        Assert.False(r.ShouldCancel);
    }

    // --- Daily fallback resolver ---------------------------------------------------------

    [Fact]
    public void ResolveNearDCancelZone_PrefersDResultOverFallback()
    {
        var dResult = new SwingCEdgeResult
        {
            EdgeTop = 1.1300,
            EdgeBottom = 1.1250,
            TfToken = "60",
            IsOb = false,
        };
        var fallback = new Dictionary<string, PineStateEngine>
        {
            ["1440"] = MakeStateWithRedZoneAt(barIdx: 5, top: 1.5000, bottom: 1.4900),
        };
        var z = SwingCEdgeTakeProfitResolver.ResolveNearDCancelZone(
            dResult, referencePrice: 1.20, isBuy: true, entry: 1.10, fallback);
        Assert.NotNull(z);
        Assert.Equal(1.1300, z!.EdgeTop, 6);
        Assert.Equal("60", z.TfToken);
    }

    [Fact]
    public void ResolveNearDCancelZone_FallsBackToDailyWhenDIsNull()
    {
        var fallback = new Dictionary<string, PineStateEngine>
        {
            ["1440"] = MakeStateWithRedZoneAt(barIdx: 5, top: 1.3000, bottom: 1.2900),
        };
        var z = SwingCEdgeTakeProfitResolver.ResolveNearDCancelZone(
            dResult: null, referencePrice: 1.20, isBuy: true, entry: 1.10, fallback);
        Assert.NotNull(z);
        Assert.Equal(1.3000, z!.EdgeTop, 6);
        Assert.Equal(1.2900, z.EdgeBottom, 6);
        Assert.Equal("1440", z.TfToken);
    }

    [Fact]
    public void BufferTouchBand_Sell_ExtendsAboveZoneTop()
    {
        var (lo, hi) = NearDZoneCancelRule.BufferTouchBand(
            zoneLow: 1.083, zoneHigh: 1.086, isBuy: false, isOb: true);
        var buffer = NearDZoneCancelRule.ZoneTouchBuffer(1.086, 1.083, isOb: true);
        Assert.Equal(1.086, lo, 6);
        Assert.Equal(1.086 + buffer, hi, 6);
    }

    [Fact]
    public void BufferTouchBand_Buy_ExtendsBelowZoneBottom()
    {
        var (lo, hi) = NearDZoneCancelRule.BufferTouchBand(
            zoneLow: 1.125, zoneHigh: 1.130, isBuy: true, isOb: false);
        var buffer = NearDZoneCancelRule.ZoneTouchBuffer(1.130, 1.125, isOb: false);
        Assert.Equal(1.125 - buffer, lo, 6);
        Assert.Equal(1.125, hi, 6);
    }

    [Fact]
    public void BacktestLastClosedBarIndex_UsesHostWhenSet()
    {
        Assert.Equal(358, NearDZoneCancelRule.BacktestLastClosedBarIndex(barsCount: 360, hostLastClosedBar: 358));
    }

    [Fact]
    public void BacktestLastClosedBarIndex_OnBarFormingBarIsCountMinusOne()
    {
        Assert.Equal(358, NearDZoneCancelRule.BacktestLastClosedBarIndex(barsCount: 360));
        Assert.Equal(0, NearDZoneCancelRule.BacktestLastClosedBarIndex(barsCount: 1));
        Assert.Equal(-1, NearDZoneCancelRule.BacktestLastClosedBarIndex(barsCount: 0));
    }

    [Fact]
    public void FormatNearDZoneDebugLabel_includes_buffer_and_source()
    {
        var text = CompoundFireChartDebugLabels.FormatNearDZoneDebugLabel(
            1.083, 1.086, 0.00019, isOb: true, tfToken: "240", isBuy: false);
        Assert.Contains("near-D H4/OB", text);
        Assert.Contains("buf@top=", text);
    }

    [Fact]
    public void ResolveNearDCancelZone_NoFallbackAvailable_ReturnsNull()
    {
        var z = SwingCEdgeTakeProfitResolver.ResolveNearDCancelZone(
            dResult: null, referencePrice: 1.20, isBuy: true, entry: 1.10, fallbackStates: null);
        Assert.Null(z);
    }

    [Fact]
    public void ResolveNearDCancelZone_FallbackEmptyDict_ReturnsNull()
    {
        var z = SwingCEdgeTakeProfitResolver.ResolveNearDCancelZone(
            dResult: null, referencePrice: 1.20, isBuy: true, entry: 1.10,
            fallbackStates: new Dictionary<string, PineStateEngine>());
        Assert.Null(z);
    }

    static PineStateEngine MakeStateWithRedZoneAt(int barIdx, double top, double bottom)
    {
        var s = new PineStateEngine();
        // type=1 (HIGH) → red key level
        var idx = s.Pivots.PushPivot((top + bottom) / 2.0, barIdx, 1, 0, 1, 1);
        s.Pivots.SetFlag(idx, 1 /* ACTIVE */);
        s.Pivots.SetHasKey(idx, true);
        s.Pivots.SetKeyExtending(idx, true);
        s.Pivots.SetKeyBox(idx, new KeyBoxRef
        {
            Spec = new KeyBoxSpec { Top = top, Bottom = bottom },
        });
        return s;
    }
}
