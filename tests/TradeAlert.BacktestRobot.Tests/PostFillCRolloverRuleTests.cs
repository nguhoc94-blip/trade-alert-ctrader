using System.Collections.Generic;
using TradeAlert.BacktestRobot.Execution;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests;

public class PostFillCRolloverRuleTests
{
    static ZoneCandidate Zone(
        ZoneEffectiveColor color, double low, double high, int pivotBar,
        ZoneSourceKind source = ZoneSourceKind.KeyLevel) => new()
    {
        TfToken = "15",
        Source = source,
        EffectiveColor = color,
        PivotBar = pivotBar,
        PivotIndex = pivotBar,
        Low = low,
        High = high,
    };

    // BUY: C is a HIGH (RED). entry below C. A new RED zone between entry and C.bottom → rollover.
    const double BuyEntry = 1.1000;
    const double BuySl = 1.0980;
    const double BuyTrigger = 1.1005; // entry + 0.25R, R = 0.002
    const double BuyCBottom = 1.1050;
    const double BuyCTop = 1.1060;
    const int FillBar = 100;

    static List<ZoneCandidate> Empty() => new();

    // ── trigger gate (0.25R) ────────────────────────────────────────────────

    [Fact]
    public void ComputeTriggerPrice_Buy_EntryPlusQuarterR()
    {
        var trigger = PostFillCRolloverRule.ComputeTriggerPrice(BuyEntry, BuySl, isBuy: true);
        Assert.Equal(BuyTrigger, trigger, 6);
    }

    [Fact]
    public void ComputeTriggerPrice_Sell_EntryMinusQuarterR()
    {
        const double sellEntry = 1.1100;
        const double sellSl = 1.1120;
        var trigger = PostFillCRolloverRule.ComputeTriggerPrice(sellEntry, sellSl, isBuy: false);
        Assert.Equal(1.1095, trigger, 6);
    }

    [Fact]
    public void Buy_TriggerNotReached_NoRollover()
    {
        var zones = new List<ZoneCandidate> { Zone(ZoneEffectiveColor.Red, 1.1020, 1.1030, FillBar + 5) };
        var r = PostFillCRolloverRule.Evaluate(
            isBuy: true, entry: BuyEntry, cEdgeTop: BuyCTop, cEdgeBottom: BuyCBottom,
            fillBarIndex: FillBar, triggerPrice: BuyTrigger,
            bid: 1.1004, ask: 1.1006, m15Zones: zones);

        Assert.False(r.ShouldRollover);
        Assert.False(r.TriggerReached);
    }

    [Fact]
    public void Buy_TriggerReached_NewRedZoneBelowC_Rollover()
    {
        var zones = new List<ZoneCandidate> { Zone(ZoneEffectiveColor.Red, 1.1020, 1.1030, FillBar + 5) };
        var r = PostFillCRolloverRule.Evaluate(
            isBuy: true, entry: BuyEntry, cEdgeTop: BuyCTop, cEdgeBottom: BuyCBottom,
            fillBarIndex: FillBar, triggerPrice: BuyTrigger,
            bid: 1.1006, ask: 1.1007, m15Zones: zones);

        Assert.True(r.ShouldRollover);
        Assert.True(r.TriggerReached);
        Assert.NotNull(r.NewCZone);
        Assert.Equal(1.1020, r.NewCZone!.Low);
    }

    // ── "new" gate (must be after fill bar) ───────────────────────────────────

    [Fact]
    public void Buy_ZoneOlderThanFill_NotRollover()
    {
        // Zone formed before/at fill (e.g. the trade's own C or D) must be ignored.
        var zones = new List<ZoneCandidate> { Zone(ZoneEffectiveColor.Red, 1.1020, 1.1030, FillBar) };
        var r = PostFillCRolloverRule.Evaluate(
            isBuy: true, entry: BuyEntry, cEdgeTop: BuyCTop, cEdgeBottom: BuyCBottom,
            fillBarIndex: FillBar, triggerPrice: BuyTrigger,
            bid: 1.1006, ask: 1.1007, m15Zones: zones);

        Assert.False(r.ShouldRollover);
        Assert.Equal("no-new-zone-below-C", r.Reason);
    }

    // ── color gate ────────────────────────────────────────────────────────────

    [Fact]
    public void Buy_GreenZoneIgnored()
    {
        var zones = new List<ZoneCandidate> { Zone(ZoneEffectiveColor.Green, 1.1020, 1.1030, FillBar + 5) };
        var r = PostFillCRolloverRule.Evaluate(
            isBuy: true, entry: BuyEntry, cEdgeTop: BuyCTop, cEdgeBottom: BuyCBottom,
            fillBarIndex: FillBar, triggerPrice: BuyTrigger,
            bid: 1.1006, ask: 1.1007, m15Zones: zones);

        Assert.False(r.ShouldRollover);
    }

    // ── containment gate ──────────────────────────────────────────────────────

    [Fact]
    public void Buy_ZoneAboveC_NotContained()
    {
        // Zone high pokes above C.bottom → not fully inside [entry, C.bottom].
        var zones = new List<ZoneCandidate> { Zone(ZoneEffectiveColor.Red, 1.1045, 1.1055, FillBar + 5) };
        var r = PostFillCRolloverRule.Evaluate(
            isBuy: true, entry: BuyEntry, cEdgeTop: BuyCTop, cEdgeBottom: BuyCBottom,
            fillBarIndex: FillBar, triggerPrice: BuyTrigger,
            bid: 1.1006, ask: 1.1007, m15Zones: zones);

        Assert.False(r.ShouldRollover);
    }

    [Fact]
    public void Buy_ZoneBelowEntry_NotContained()
    {
        var zones = new List<ZoneCandidate> { Zone(ZoneEffectiveColor.Red, 1.0980, 1.0990, FillBar + 5) };
        var r = PostFillCRolloverRule.Evaluate(
            isBuy: true, entry: BuyEntry, cEdgeTop: BuyCTop, cEdgeBottom: BuyCBottom,
            fillBarIndex: FillBar, triggerPrice: BuyTrigger,
            bid: 1.1006, ask: 1.1007, m15Zones: zones);

        Assert.False(r.ShouldRollover);
    }

    // ── nearest-to-entry selection ────────────────────────────────────────────

    [Fact]
    public void Buy_PicksNearestToEntry()
    {
        var zones = new List<ZoneCandidate>
        {
            Zone(ZoneEffectiveColor.Red, 1.1035, 1.1045, FillBar + 5),
            Zone(ZoneEffectiveColor.Red, 1.1010, 1.1018, FillBar + 6), // nearest to entry
        };
        var r = PostFillCRolloverRule.Evaluate(
            isBuy: true, entry: BuyEntry, cEdgeTop: BuyCTop, cEdgeBottom: BuyCBottom,
            fillBarIndex: FillBar, triggerPrice: BuyTrigger,
            bid: 1.1006, ask: 1.1007, m15Zones: zones);

        Assert.True(r.ShouldRollover);
        Assert.Equal(1.1010, r.NewCZone!.Low);
    }

    // ── OB included ───────────────────────────────────────────────────────────

    [Fact]
    public void Buy_NewRedOb_Rollover()
    {
        var zones = new List<ZoneCandidate>
        {
            Zone(ZoneEffectiveColor.Red, 1.1020, 1.1030, FillBar + 5, ZoneSourceKind.OrderBlock),
        };
        var r = PostFillCRolloverRule.Evaluate(
            isBuy: true, entry: BuyEntry, cEdgeTop: BuyCTop, cEdgeBottom: BuyCBottom,
            fillBarIndex: FillBar, triggerPrice: BuyTrigger,
            bid: 1.1006, ask: 1.1007, m15Zones: zones);

        Assert.True(r.ShouldRollover);
        Assert.Equal(ZoneSourceKind.OrderBlock, r.NewCZone!.Source);
    }

    // ── SELL mirror ───────────────────────────────────────────────────────────

    [Fact]
    public void Sell_TriggerReached_NewGreenZoneAboveC_Rollover()
    {
        // SELL: C is a LOW (GREEN). entry above C. New GREEN zone between C.top and entry.
        const double sellEntry = 1.1100;
        const double sellTrigger = 1.1095;
        const double sellCTop = 1.1050;
        const double sellCBottom = 1.1040;
        var zones = new List<ZoneCandidate> { Zone(ZoneEffectiveColor.Green, 1.1070, 1.1080, FillBar + 5) };

        var r = PostFillCRolloverRule.Evaluate(
            isBuy: false, entry: sellEntry, cEdgeTop: sellCTop, cEdgeBottom: sellCBottom,
            fillBarIndex: FillBar, triggerPrice: sellTrigger,
            bid: 1.1094, ask: 1.1095, m15Zones: zones);

        Assert.True(r.ShouldRollover);
        Assert.Equal(1.1080, r.NewCZone!.High);
    }

    [Fact]
    public void Sell_TriggerNotReached_NoRollover()
    {
        const double sellEntry = 1.1100;
        const double sellTrigger = 1.1095;
        const double sellCTop = 1.1050;
        const double sellCBottom = 1.1040;
        var zones = new List<ZoneCandidate> { Zone(ZoneEffectiveColor.Green, 1.1070, 1.1080, FillBar + 5) };

        var r = PostFillCRolloverRule.Evaluate(
            isBuy: false, entry: sellEntry, cEdgeTop: sellCTop, cEdgeBottom: sellCBottom,
            fillBarIndex: FillBar, triggerPrice: sellTrigger,
            bid: 1.1099, ask: 1.1100, m15Zones: zones);

        Assert.False(r.ShouldRollover);
    }

    // ── degenerate inputs ─────────────────────────────────────────────────────

    [Fact]
    public void InvalidCGeometry_NoRollover()
    {
        var zones = new List<ZoneCandidate> { Zone(ZoneEffectiveColor.Red, 1.1020, 1.1030, FillBar + 5) };
        var r = PostFillCRolloverRule.Evaluate(
            isBuy: true, entry: BuyEntry, cEdgeTop: 0, cEdgeBottom: 0,
            fillBarIndex: FillBar, triggerPrice: BuyTrigger,
            bid: 1.1006, ask: 1.1007, m15Zones: zones);

        Assert.False(r.ShouldRollover);
        Assert.Equal("invalid-C-geometry", r.Reason);
    }

    [Fact]
    public void NoZones_NoRollover()
    {
        var r = PostFillCRolloverRule.Evaluate(
            isBuy: true, entry: BuyEntry, cEdgeTop: BuyCTop, cEdgeBottom: BuyCBottom,
            fillBarIndex: FillBar, triggerPrice: BuyTrigger,
            bid: 1.1006, ask: 1.1007, m15Zones: Empty());

        Assert.False(r.ShouldRollover);
    }

    [Fact]
    public void FormatTriggerReachedLog_ContainsKeyFields()
    {
        var log = PostFillCRolloverRule.FormatTriggerReachedLog(
            "L6BT|R3|B100|C", isBuy: true, entry: 1900, sl: 1898, trigger: 1900.5,
            bid: 1900.6, ask: 1900.8, fillBar: 200,
            cEdgeTop: 1910, cEdgeBottom: 1905);

        Assert.Contains("ROLLOVER 0.25R-REACHED", log);
        Assert.Contains("trigger=1900.5", log);
        Assert.Contains("profitR=0.25", log);
        Assert.Contains("sl=1898", log);
    }
}
