using TradeAlert.Core.Engines.AlertEvaluators;
using TradeAlert.Core.Models.Alerts;
using Xunit;

namespace TradeAlert.Core.Tests;

/// <summary>Pine plot 5281-5306: REAL uses full-band f_check_touch_zones + dual flag mapping.</summary>
public sealed class RealZoneBufferedTests
{
    static ZoneState Zones(
        (double lo, double hi)[]? greenKey = null,
        (double lo, double hi)[]? greenOb = null,
        (double lo, double hi)[]? redKey = null,
        (double lo, double hi)[]? redOb = null,
        double barClose = 0.0) =>
        new()
        {
            GreenKeyZones = ToBands(greenKey),
            GreenObZones  = ToBands(greenOb),
            RedKeyZones   = ToBands(redKey),
            RedObZones    = ToBands(redOb),
            BarClose      = barClose,
        };

    static (double Low, double High)[] ToBands((double lo, double hi)[]? bands) =>
        bands == null
            ? Array.Empty<(double, double)>()
            : bands.Select(b => (b.lo, b.hi)).ToArray();

    static AlertEvaluationContext BuyCtx(
        double buyBot,
        double buyTop,
        ZoneState zones,
        double? effBuyBreakBot = null,
        bool realZoneDebug = false) =>
        new()
        {
            // ZoneState must mirror RealtimeFilter.Zones (same instance in production via Loop6EvaluationContextFactory).
            ZoneState = zones,
            ActiveZoneCollectionAvailable = true,
            IsRealtime = true,                  // realtime → probe = BarClose (no TouchLtfOpenPrice detour)
            ChartTimeframeToken = "5",
            RealtimeFilterStateAvailable = true,
            RealtimeFilter = new RealtimeFilterState
            {
                LastPushedSwingType = -1,
                RealBuyBot = buyBot,
                RealBuyTop = buyTop,
                EffBuyBreakBot = effBuyBreakBot,
                Zones = zones,
            },
            RealZoneDebug = realZoneDebug,
        };

    static AlertEvaluationContext SellCtx(
        double sellBot,
        double sellTop,
        ZoneState zones,
        double? effSellBreakTop = null,
        bool realZoneDebug = false) =>
        new()
        {
            ZoneState = zones,
            ActiveZoneCollectionAvailable = true,
            IsRealtime = true,
            ChartTimeframeToken = "5",
            RealtimeFilterStateAvailable = true,
            RealtimeFilter = new RealtimeFilterState
            {
                LastPushedSwingType = 1,
                RealSellBot = sellBot,
                RealSellTop = sellTop,
                EffSellBreakTop = effSellBreakTop,
                Zones = zones,
            },
            RealZoneDebug = realZoneDebug,
        };

    [Theory]
    [InlineData(0, true, true)]
    [InlineData(1, true, false)]
    [InlineData(-1, false, true)]
    [InlineData(2, false, false)]
    public void MapPineRealFlags_MatchesPinePlot(int tActive, bool expBuy, bool expSell)
    {
        var (canBuy, canSell) = TouchRealEvaluators.MapPineRealFlags(tActive);
        Assert.Equal(expBuy, canBuy);
        Assert.Equal(expSell, canSell);
    }

    [Fact]
    public void Buy_NoZoneOverlap_BothCanBuyAndCanSellTrue()
    {
        var zones = Zones(redKey: new[] { (1.1100, 1.1150) });
        var detail = TouchRealEvaluators.ComputeRealFlagsDetail(BuyCtx(1.0900, 1.0950, zones));

        Assert.True(detail.CanBuy);
        Assert.True(detail.CanSell);
        Assert.Contains("tActive=0", detail.DebugText);
    }

    [Fact]
    public void Sell_NoZoneOverlap_BothCanBuyAndCanSellTrue()
    {
        var zones = Zones(greenKey: new[] { (1.1100, 1.1150) });
        var detail = TouchRealEvaluators.ComputeRealFlagsDetail(SellCtx(1.1000, 1.1050, zones));

        Assert.True(detail.CanBuy);
        Assert.True(detail.CanSell);
        Assert.Contains("tActive=0", detail.DebugText);
    }

    [Fact]
    public void Buy_BandOverlapsRedOnly_BlocksBuyAllowsSell()
    {
        var zones = Zones(redKey: new[] { (1.0920, 1.0940) });
        var detail = TouchRealEvaluators.ComputeRealFlagsDetail(BuyCtx(1.0900, 1.0950, zones));

        Assert.False(detail.CanBuy);
        Assert.True(detail.CanSell);
        Assert.Contains("tActive=-1", detail.DebugText);
    }

    [Fact]
    public void Buy_BandOverlapsGreenOnly_AllowsBuy_SellFromTouchNoZoneNearClose()
    {
        // barClose=0 far from green zone → tTouch=0 → canSellReal=true (touch says no block).
        var zones = Zones(greenKey: new[] { (1.0900, 1.0950) });
        var detail = TouchRealEvaluators.ComputeRealFlagsDetail(BuyCtx(1.0900, 1.0950, zones));

        Assert.True(detail.CanBuy);
        Assert.True(detail.CanSell);   // opposite uses touch; barClose=0 → tTouch=0 → OK
        Assert.Contains("tActive=1", detail.DebugText);
    }

    [Fact]
    public void Buy_BandOverlapsBothColors_BlocksBuy_SellFromTouchNoZoneNearClose()
    {
        // Band overlaps both → tActive=2 → canBuy=false.
        // barClose=0 far from all zones → tTouch=0 → canSellReal=true.
        var zones = Zones(
            greenKey: new[] { (1.0900, 1.0920) },
            redKey: new[] { (1.0930, 1.0950) });

        var detail = TouchRealEvaluators.ComputeRealFlagsDetail(BuyCtx(1.0900, 1.0950, zones));

        Assert.False(detail.CanBuy);
        Assert.True(detail.CanSell);   // opposite uses touch; barClose=0 → tTouch=0 → OK
        Assert.Contains("tActive=2", detail.DebugText);
    }

    [Fact]
    public void Sell_BandOverlapsGreenOnly_BlocksSellAllowsBuy()
    {
        var zones = Zones(greenKey: new[] { (1.1000, 1.1010) });
        var detail = TouchRealEvaluators.ComputeRealFlagsDetail(SellCtx(1.1000, 1.1050, zones));

        Assert.True(detail.CanBuy);
        Assert.False(detail.CanSell);
        Assert.Contains("tActive=1", detail.DebugText);
    }

    [Fact]
    public void Sell_BandOverlapsRedOnly_AllowsSell_BuyFromTouchNoZoneNearClose()
    {
        // Band overlaps red → tActive=-1 → canSell=true.
        // barClose=0 far from all zones → tTouch=0 → canBuyReal=true.
        var zones = Zones(redKey: new[] { (1.1000, 1.1020) });
        var detail = TouchRealEvaluators.ComputeRealFlagsDetail(SellCtx(1.1000, 1.1050, zones));

        Assert.True(detail.CanBuy);   // opposite uses touch; barClose=0 → tTouch=0 → OK
        Assert.True(detail.CanSell);
        Assert.Contains("tActive=-1", detail.DebugText);
    }

    [Fact]
    public void Buy_EffBreakExpandsBand_StillUsesPineTouch()
    {
        var zones = Zones(redKey: new[] { (1.0880, 1.0890) });
        var detail = TouchRealEvaluators.ComputeRealFlagsDetail(
            BuyCtx(1.0900, 1.0950, zones, effBuyBreakBot: 1.0870));

        Assert.False(detail.CanBuy);
        Assert.True(detail.CanSell);
    }

    // ── New: opposite-direction uses touch at barClose ───────────────────────

    [Fact]
    public void BuyBand_CloseInsideGreenZone_BlocksSellViaTouch()
    {
        // Buy band [1.09-1.095] away from green [1.10-1.105] → tActive=0 → canBuy=true.
        // barClose=1.102 inside green zone → tTouch=1 → canSellReal=false.
        var zones = Zones(greenKey: new[] { (1.1000, 1.1050) }, barClose: 1.1020);
        var detail = TouchRealEvaluators.ComputeRealFlagsDetail(BuyCtx(1.0900, 1.0950, zones));

        Assert.True(detail.CanBuy);
        Assert.False(detail.CanSell);
        Assert.Contains("tActive=0", detail.DebugText);
        Assert.Contains("tTouchSell=1", detail.DebugText);   // buy-band path labels touch as tTouchSell
    }

    [Fact]
    public void SellBand_CloseInsideRedZone_BlocksBuyViaTouch()
    {
        // Sell band [1.10-1.11] away from red [1.08-1.085] → tActive=0 → canSell=true.
        // barClose=1.082 inside red zone → tTouchBuy=-1 → canBuyReal=false.
        var zones = Zones(redKey: new[] { (1.0800, 1.0850) }, barClose: 1.0820);
        var detail = TouchRealEvaluators.ComputeRealFlagsDetail(SellCtx(1.1000, 1.1100, zones));

        Assert.False(detail.CanBuy);
        Assert.True(detail.CanSell);
        Assert.Contains("tActive=0", detail.DebugText);
        Assert.Contains("tTouchBuy=-1", detail.DebugText);   // sell-band path labels touch as tTouchBuy
    }

    [Fact]
    public void BuyBand_BandBlockedAndCloseTouchesGreen_BothFalse()
    {
        // Buy band overlaps red → canBuy=false.
        // barClose inside green zone → tTouch=1 → canSell=false.
        var zones = Zones(
            greenKey: new[] { (1.0800, 1.0850) },
            redKey: new[] { (1.0920, 1.0940) },
            barClose: 1.0825);
        var detail = TouchRealEvaluators.ComputeRealFlagsDetail(BuyCtx(1.0900, 1.0950, zones));

        Assert.False(detail.CanBuy);
        Assert.False(detail.CanSell);
    }

    [Fact]
    public void RealZoneDebug_AppendsDetailToReasonText()
    {
        var zones = Zones(redKey: new[] { (1.0920, 1.0940) });
        var ctxOff = BuyCtx(1.0900, 1.0950, zones, realZoneDebug: false);
        var ctxOn  = BuyCtx(1.0900, 1.0950, zones, realZoneDebug: true);

        var rOff = TouchRealEvaluators.EvaluateRealBuy(ctxOff);
        var rOn  = TouchRealEvaluators.EvaluateRealBuy(ctxOn);

        Assert.DoesNotContain("buy band=", rOff.ReasonText);
        Assert.Contains("buy band=", rOn.ReasonText);
        Assert.Contains("tActive=-1", rOn.ReasonText);
        Assert.Contains("tTouchSell=", rOn.ReasonText);   // buy-band path uses tTouchSell label
    }
}
