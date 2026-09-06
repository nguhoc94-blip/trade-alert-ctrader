using TradeAlert.BacktestRobot.Execution;
using TradeAlert.Core.Models.Alerts;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests;

public class NearBThenCTpCancelRuleTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    static PendingOrderContext MakeCtx(
        bool isBuy,
        double bLow, double bHigh,
        double tpC,
        bool bIsOb = false,
        double? entry = null,
        double? nearBLow = null,
        double? nearBHigh = null)
    {
        var resolvedEntry = entry ?? (isBuy ? bHigh : bLow);
        var zoneLow = nearBLow ?? bLow;
        var zoneHigh = nearBHigh ?? bHigh;
        return new PendingOrderContext
        {
            Label = "L6BT|R2|B100|C",
            RuleSlot = 1,
            Direction = isBuy ? SignalDirection.Buy : SignalDirection.Sell,
            IsBuy = isBuy,
            SwingBPivotBar = 100,
            EntryPrice = resolvedEntry,
            StopLoss = isBuy ? bLow - 0.001 : bHigh + 0.001,
            OriginalTakeProfit = tpC,
            SwingBKeyLow = bLow,
            SwingBKeyHigh = bHigh,
            SwingBKeyIsOb = bIsOb,
            NearBCancelZoneLow = zoneLow,
            NearBCancelZoneHigh = zoneHigh,
            NearBCancelZoneIsOb = bIsOb,
            TpCLegPrice = tpC,
        };
    }

    // ── Buffer formula ────────────────────────────────────────────────────────

    [Fact]
    public void Buffer_KeyZone_IsHeightDiv4()
    {
        var buf = NearBThenCTpCancelRule.ZoneTouchBuffer(top: 1.0020, bot: 1.0000, isOb: false);
        Assert.Equal((1.0020 - 1.0000) / 4.0, buf, 10);
    }

    [Fact]
    public void Buffer_ObZone_IsHeightDiv8()
    {
        var buf = NearBThenCTpCancelRule.ZoneTouchBuffer(top: 1.0020, bot: 1.0000, isOb: true);
        Assert.Equal((1.0020 - 1.0000) / 8.0, buf, 10);
    }

    // ── Touch B — no TpCLegPrice ──────────────────────────────────────────────

    [Fact]
    public void EvaluateTouch_NoTpCSet_ReturnsFalse()
    {
        var ctx = new PendingOrderContext
        {
            Label = "L6BT|R1|B10|C",
            IsBuy = true,
            SwingBKeyLow = 1.0000,
            SwingBKeyHigh = 1.0020,
            TpCLegPrice = 0, // not a split plan
        };
        var r = NearBThenCTpCancelRule.EvaluateTouch(in ctx, bid: 1.0001, ask: 1.0001);
        Assert.False(r.TouchedB);
    }

    // ── Touch B — BUY ────────────────────────────────────────────────────────

    [Fact]
    public void EvaluateTouch_Buy_AskAtBTop_Touched()
    {
        // bZone=[1.0000..1.0020], h=0.0020, buffer=0.0005 (Key h/4)
        var ctx = MakeCtx(isBuy: true, bLow: 1.0000, bHigh: 1.0020, tpC: 1.0100);
        var r = NearBThenCTpCancelRule.EvaluateTouch(in ctx, bid: 1.0019, ask: 1.0020);
        Assert.True(r.TouchedB);
    }

    [Fact]
    public void EvaluateTouch_Buy_AskBelowBotPlusBuffer_Touched()
    {
        // buffer = 0.0005; ask = bBot + buffer = 1.0005 → probe + buffer = 1.0010 ≥ bBot OK
        var ctx = MakeCtx(isBuy: true, bLow: 1.0000, bHigh: 1.0020, tpC: 1.0100);
        var r = NearBThenCTpCancelRule.EvaluateTouch(in ctx, bid: 1.0004, ask: 1.0005);
        Assert.True(r.TouchedB);
    }

    [Fact]
    public void EvaluateTouch_Buy_AskAboveBTop_NotTouched()
    {
        // ask > bHigh: probe not ≤ top
        var ctx = MakeCtx(isBuy: true, bLow: 1.0000, bHigh: 1.0020, tpC: 1.0100);
        var r = NearBThenCTpCancelRule.EvaluateTouch(in ctx, bid: 1.0025, ask: 1.0025);
        Assert.False(r.TouchedB);
    }

    [Fact]
    public void EvaluateTouch_Buy_AskFarBelowBuffer_NotTouched()
    {
        // ask + buffer < bBot
        var ctx = MakeCtx(isBuy: true, bLow: 1.0000, bHigh: 1.0020, tpC: 1.0100);
        var r = NearBThenCTpCancelRule.EvaluateTouch(in ctx, bid: 0.9990, ask: 0.9990);
        Assert.False(r.TouchedB);
    }

    // ── Touch B — SELL ────────────────────────────────────────────────────────

    [Fact]
    public void EvaluateTouch_Sell_BidAtBBot_Touched()
    {
        var ctx = MakeCtx(isBuy: false, bLow: 1.0000, bHigh: 1.0020, tpC: 0.9900);
        var r = NearBThenCTpCancelRule.EvaluateTouch(in ctx, bid: 1.0000, ask: 1.0001);
        Assert.True(r.TouchedB);
    }

    [Fact]
    public void EvaluateTouch_Sell_BidAboveTopMinusBuffer_Touched()
    {
        // buffer = 0.0005; bid = bTop - buffer = 1.0015 → bid - buffer = 1.0010 ≤ top OK
        var ctx = MakeCtx(isBuy: false, bLow: 1.0000, bHigh: 1.0020, tpC: 0.9900);
        var r = NearBThenCTpCancelRule.EvaluateTouch(in ctx, bid: 1.0015, ask: 1.0016);
        Assert.True(r.TouchedB);
    }

    [Fact]
    public void EvaluateTouch_Sell_BidFarAboveTop_NotTouched()
    {
        var ctx = MakeCtx(isBuy: false, bLow: 1.0000, bHigh: 1.0020, tpC: 0.9900);
        var r = NearBThenCTpCancelRule.EvaluateTouch(in ctx, bid: 1.0060, ask: 1.0061);
        Assert.False(r.TouchedB);
    }

    // ── OB buffer (h/8 instead of h/4) ───────────────────────────────────────

    [Fact]
    public void EvaluateTouch_Buy_ObBuffer_HalfOfKey()
    {
        // h=0.0020, OB buffer=0.0020/8=0.00025; ask=bBot+0.0003 → probe+buffer=0.0003+0.00025=0.00055 ≥ bBot? No (0.00055 < 0.0020)
        // so ask must be ≥ bBot to trigger: ask=1.0002 → probe=1.0002 ≤ top(1.0020) AND probe+buf=1.00020+0.00025=1.00045 ≥ bot(1.0000) ✓
        var ctx = MakeCtx(isBuy: true, bLow: 1.0000, bHigh: 1.0020, tpC: 1.0100, bIsOb: true);
        var r = NearBThenCTpCancelRule.EvaluateTouch(in ctx, bid: 1.0001, ask: 1.0002);
        Assert.True(r.TouchedB);
        Assert.Equal(0.0020 / 8.0, r.Buffer, 10);
    }

    // ── EvaluateCancel — no latch ─────────────────────────────────────────────

    [Fact]
    public void EvaluateCancel_LatchFalse_NoCancel()
    {
        var ctx = MakeCtx(isBuy: true, bLow: 1.0000, bHigh: 1.0020, tpC: 1.0100);
        var r = NearBThenCTpCancelRule.EvaluateCancel(in ctx, nearBTouched: false, bid: 1.0110, ask: 1.0111);
        Assert.False(r.ShouldCancel);
    }

    [Fact]
    public void EvaluateCancel_NoTpCLegPrice_NoCancel()
    {
        var ctx = new PendingOrderContext { IsBuy = true, TpCLegPrice = 0 };
        var r = NearBThenCTpCancelRule.EvaluateCancel(in ctx, nearBTouched: true, bid: 1.0200, ask: 1.0201);
        Assert.False(r.ShouldCancel);
    }

    // ── EvaluateCancel — BUY ─────────────────────────────────────────────────

    [Fact]
    public void EvaluateCancel_Buy_BidReachesTpC_Cancel()
    {
        var ctx = MakeCtx(isBuy: true, bLow: 1.0000, bHigh: 1.0020, tpC: 1.0100);
        var r = NearBThenCTpCancelRule.EvaluateCancel(in ctx, nearBTouched: true, bid: 1.0100, ask: 1.0101);
        Assert.True(r.ShouldCancel);
    }

    [Fact]
    public void EvaluateCancel_Buy_BidAboveTpC_Cancel()
    {
        var ctx = MakeCtx(isBuy: true, bLow: 1.0000, bHigh: 1.0020, tpC: 1.0100);
        var r = NearBThenCTpCancelRule.EvaluateCancel(in ctx, nearBTouched: true, bid: 1.0120, ask: 1.0121);
        Assert.True(r.ShouldCancel);
    }

    [Fact]
    public void EvaluateCancel_Buy_BidBelowTpC_NoCancel()
    {
        var ctx = MakeCtx(isBuy: true, bLow: 1.0000, bHigh: 1.0020, tpC: 1.0100);
        var r = NearBThenCTpCancelRule.EvaluateCancel(in ctx, nearBTouched: true, bid: 1.0090, ask: 1.0091);
        Assert.False(r.ShouldCancel);
    }

    // ── EvaluateCancel — SELL ─────────────────────────────────────────────────

    [Fact]
    public void EvaluateCancel_Sell_AskReachesTpC_Cancel()
    {
        var ctx = MakeCtx(isBuy: false, bLow: 1.0000, bHigh: 1.0020, tpC: 0.9900);
        var r = NearBThenCTpCancelRule.EvaluateCancel(in ctx, nearBTouched: true, bid: 0.9899, ask: 0.9900);
        Assert.True(r.ShouldCancel);
    }

    [Fact]
    public void EvaluateCancel_Sell_AskBelowTpC_Cancel()
    {
        var ctx = MakeCtx(isBuy: false, bLow: 1.0000, bHigh: 1.0020, tpC: 0.9900);
        var r = NearBThenCTpCancelRule.EvaluateCancel(in ctx, nearBTouched: true, bid: 0.9880, ask: 0.9885);
        Assert.True(r.ShouldCancel);
    }

    [Fact]
    public void EvaluateCancel_Sell_AskAboveTpC_NoCancel()
    {
        var ctx = MakeCtx(isBuy: false, bLow: 1.0000, bHigh: 1.0020, tpC: 0.9900);
        var r = NearBThenCTpCancelRule.EvaluateCancel(in ctx, nearBTouched: true, bid: 0.9910, ask: 0.9912);
        Assert.False(r.ShouldCancel);
    }

    // ── Two-step flow: touch then cancel ─────────────────────────────────────

    [Fact]
    public void FullFlow_Buy_TouchB_ThenHitTpC_Cancels()
    {
        var ctx = MakeCtx(isBuy: true, bLow: 1.0000, bHigh: 1.0020, tpC: 1.0100);

        // Tick 1: price drops to B zone
        var touch = NearBThenCTpCancelRule.EvaluateTouch(in ctx, bid: 1.0010, ask: 1.0011);
        Assert.True(touch.TouchedB);
        bool latch = touch.TouchedB; // simulate _book.MarkNearBZoneTouched

        // Tick 2: price not yet at TP C — no cancel
        var c1 = NearBThenCTpCancelRule.EvaluateCancel(in ctx, nearBTouched: latch, bid: 1.0050, ask: 1.0051);
        Assert.False(c1.ShouldCancel);

        // Tick 3: price reaches TP C — cancel
        var c2 = NearBThenCTpCancelRule.EvaluateCancel(in ctx, nearBTouched: latch, bid: 1.0100, ask: 1.0101);
        Assert.True(c2.ShouldCancel);
    }

    [Fact]
    public void FullFlow_Buy_NoTouchB_PriceReachesTpC_NoCancel()
    {
        var ctx = MakeCtx(isBuy: true, bLow: 1.0000, bHigh: 1.0020, tpC: 1.0100);

        // Latch never set (price never touched B)
        var r = NearBThenCTpCancelRule.EvaluateCancel(in ctx, nearBTouched: false, bid: 1.0100, ask: 1.0101);
        Assert.False(r.ShouldCancel);
    }

    // ── D leg uses C leg TpC (not D's own TP) ─────────────────────────────────

    [Fact]
    public void EvaluateCancel_DLeg_UsesTpCPrice_NotDsTp()
    {
        // D leg: its own TP is 1.0130, but TpCLegPrice = 1.0100 (C's TP)
        var ctx = new PendingOrderContext
        {
            Label = "L6BT|R2|B100|D",
            IsBuy = true,
            SwingBKeyLow = 1.0000,
            SwingBKeyHigh = 1.0020,
            TpCLegPrice = 1.0100,     // C's TP
            OriginalTakeProfit = 1.0130, // D's own TP — gate ignores this
        };

        // Bid at C's TP → cancel
        var r = NearBThenCTpCancelRule.EvaluateCancel(in ctx, nearBTouched: true, bid: 1.0100, ask: 1.0101);
        Assert.True(r.ShouldCancel);

        // Bid between C and D TP range — still cancel (already at C)
        var r2 = NearBThenCTpCancelRule.EvaluateCancel(in ctx, nearBTouched: true, bid: 1.0115, ask: 1.0116);
        Assert.True(r2.ShouldCancel);
    }

    [Fact]
    public void EvaluateTouch_Buy_ObCandidate_UsesObBoxNotExpandedKeyBox()
    {
        // KLV [1.0000..1.0020]; winning OB box [1.0005..1.0030], entry at obTop.
        var ctx = MakeCtx(
            isBuy: true, bLow: 1.0000, bHigh: 1.0020, tpC: 1.0100,
            bIsOb: true, entry: 1.0030, nearBLow: 1.0005, nearBHigh: 1.0030);

        var aboveObTop = NearBThenCTpCancelRule.EvaluateTouch(in ctx, bid: 1.0030, ask: 1.0031);
        Assert.False(aboveObTop.TouchedB);

        var atObTop = NearBThenCTpCancelRule.EvaluateTouch(in ctx, bid: 1.0029, ask: 1.0030);
        Assert.True(atObTop.TouchedB);
        Assert.Equal(1.0005, atObTop.BZoneLow, 6);
        Assert.Equal(1.0030, atObTop.BZoneHigh, 6);
        Assert.Equal((1.0030 - 1.0005) / 8.0, atObTop.Buffer, 10);
    }

    [Fact]
    public void EvaluateTouch_Sell_ObCandidate_UsesObBoxNotExpandedKeyBox()
    {
        // KLV [1.0020..1.0000] inverted low/high params: bLow=1.0000 bHigh=1.0020
        // Winning OB box [0.9970..1.0005], entry at obBot 0.9970.
        var ctx = MakeCtx(
            isBuy: false, bLow: 1.0000, bHigh: 1.0020, tpC: 0.9900,
            bIsOb: true, entry: 0.9970, nearBLow: 0.9970, nearBHigh: 1.0005);

        var belowObBot = NearBThenCTpCancelRule.EvaluateTouch(in ctx, bid: 0.9969, ask: 0.9970);
        Assert.False(belowObBot.TouchedB);

        var atObBot = NearBThenCTpCancelRule.EvaluateTouch(in ctx, bid: 0.9970, ask: 0.9971);
        Assert.True(atObBot.TouchedB);
        Assert.Equal(0.9970, atObBot.BZoneLow, 6);
        Assert.Equal(1.0005, atObBot.BZoneHigh, 6);
        Assert.Equal((1.0005 - 0.9970) / 8.0, atObBot.Buffer, 10);
    }

    [Fact]
    public void FullFlow_Sell_TouchB_ThenHitTpC_Cancels()
    {
        var ctx = MakeCtx(isBuy: false, bLow: 1.0000, bHigh: 1.0020, tpC: 0.9900);

        var touch = NearBThenCTpCancelRule.EvaluateTouch(in ctx, bid: 1.0000, ask: 1.0001);
        Assert.True(touch.TouchedB);

        var c1 = NearBThenCTpCancelRule.EvaluateCancel(in ctx, nearBTouched: true, bid: 0.9950, ask: 0.9951);
        Assert.False(c1.ShouldCancel);

        var c2 = NearBThenCTpCancelRule.EvaluateCancel(in ctx, nearBTouched: true, bid: 0.9899, ask: 0.9900);
        Assert.True(c2.ShouldCancel);
    }

    [Fact]
    public void EvaluateTouchWithProbe_Buy_BacktestLowTouchesB()
    {
        // Bar close above B but Low dips into zone.
        var ctx = MakeCtx(isBuy: true, bLow: 1.0000, bHigh: 1.0020, tpC: 1.0100);
        Assert.True(NearBThenCTpCancelRule.EvaluateTouchWithProbe(in ctx, probe: 1.0005).TouchedB);
        Assert.False(NearBThenCTpCancelRule.EvaluateTouchWithProbe(in ctx, probe: 1.0030).TouchedB);
    }

    [Fact]
    public void EvaluateTouchWithProbe_Sell_BacktestHighTouchesB()
    {
        var ctx = MakeCtx(isBuy: false, bLow: 1.0000, bHigh: 1.0020, tpC: 0.9900);
        Assert.True(NearBThenCTpCancelRule.EvaluateTouchWithProbe(in ctx, probe: 1.0015).TouchedB);
        Assert.False(NearBThenCTpCancelRule.EvaluateTouchWithProbe(in ctx, probe: 0.9990).TouchedB);
    }

    [Fact]
    public void EvaluateCancelWithProbe_Buy_BacktestHighReachesTpC()
    {
        var ctx = MakeCtx(isBuy: true, bLow: 1.0000, bHigh: 1.0020, tpC: 1.0100);
        Assert.True(NearBThenCTpCancelRule.EvaluateCancelWithProbe(in ctx, nearBTouched: true, probe: 1.0100).ShouldCancel);
        Assert.False(NearBThenCTpCancelRule.EvaluateCancelWithProbe(in ctx, nearBTouched: true, probe: 1.0050).ShouldCancel);
    }

    [Fact]
    public void EvaluateCancelWithProbe_Sell_BacktestLowReachesTpC()
    {
        var ctx = MakeCtx(isBuy: false, bLow: 1.0000, bHigh: 1.0020, tpC: 0.9900);
        Assert.True(NearBThenCTpCancelRule.EvaluateCancelWithProbe(in ctx, nearBTouched: true, probe: 0.9900).ShouldCancel);
        Assert.False(NearBThenCTpCancelRule.EvaluateCancelWithProbe(in ctx, nearBTouched: true, probe: 0.9950).ShouldCancel);
    }

    [Fact]
    public void EvaluateTouchWithProbe_MatchesEvaluate_ForDirectionalProbe()
    {
        var buy = MakeCtx(isBuy: true, bLow: 1.0000, bHigh: 1.0020, tpC: 1.0100);
        Assert.Equal(
            NearBThenCTpCancelRule.EvaluateTouch(in buy, bid: 1.0, ask: 1.0020).TouchedB,
            NearBThenCTpCancelRule.EvaluateTouchWithProbe(in buy, probe: 1.0020).TouchedB);
    }

    [Fact]
    public void ParseRulesMask_AllAndCondM5_IncludesExpectedSlots()
    {
        var all = PostBPushObstacleCancelRuleConfig.ParseRulesMask("all");
        Assert.Equal(6, all.Count);

        var condM5 = PostBPushObstacleCancelRuleConfig.ParseRulesMask("condM5");
        Assert.Contains(0, condM5);
        Assert.Contains(1, condM5);
        Assert.DoesNotContain(2, condM5);
    }
}
