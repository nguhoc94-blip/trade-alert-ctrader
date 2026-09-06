using TradeAlert.Core.Engines.AlertEvaluators;
using TradeAlert.Core.Models.Alerts;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class TouchRealEvaluatorsTests
{
    static ZoneState GreenZoneOnly(double lo, double hi, int touchResult, double barClose) =>
        new()
        {
            GreenKeyZones = new[] { (lo, hi) },
            BarClose = barClose,
            ZoneTouchResult = touchResult,
        };

    [Fact]
    public void ResolveZoneTouchT_HtfBacktest_UsesLtfOpenNotHtfClose()
    {
        var zones = GreenZoneOnly(100, 110, touchResult: -1, barClose: 999);
        var ctx = new AlertEvaluationContext
        {
            ChartTimeframeToken = "60",
            IsRealtime = false,
            ActiveZoneCollectionAvailable = true,
            ZoneState = zones,
            TouchLtfOpenPrice = 105,
        };

        var (t, price, kind) = TouchRealEvaluators.ResolveZoneTouchT(ctx, AlertConditionId.CanBuyTouchM5);

        Assert.Equal(1, t);
        Assert.Equal(105, price);
        Assert.Equal("ltfOpenM5", kind);
    }

    [Fact]
    public void ResolveZoneTouchT_HtfBacktest_UsesLtfHighProbeKind()
    {
        var zones = GreenZoneOnly(100, 110, touchResult: -1, barClose: 999);
        var ctx = new AlertEvaluationContext
        {
            ChartTimeframeToken = "60",
            IsRealtime = false,
            ActiveZoneCollectionAvailable = true,
            ZoneState = zones,
            TouchLtfOpenPrice = 108,
            TouchLtfProbeKind = "ltfHighM5",
        };

        var (_, price, kind) = TouchRealEvaluators.ResolveZoneTouchT(ctx, AlertConditionId.CanBuyTouchM5);

        Assert.Equal(108, price);
        Assert.Equal("ltfHighM5", kind);
    }

    [Fact]
    public void ResolveZoneTouchT_M5ChartBacktest_UsesPrecomputedClose()
    {
        var zones = GreenZoneOnly(100, 110, touchResult: 1, barClose: 105);
        var ctx = new AlertEvaluationContext
        {
            ChartTimeframeToken = "5",
            IsRealtime = false,
            ActiveZoneCollectionAvailable = true,
            ZoneState = zones,
            TouchLtfOpenPrice = 50,
        };

        var (t, price, kind) = TouchRealEvaluators.ResolveZoneTouchT(ctx, AlertConditionId.CanBuyTouchM5);

        Assert.Equal(1, t);
        Assert.Equal(105, price);
        Assert.Equal("close", kind);
    }

    [Fact]
    public void EvaluateTouchBuy_H4Backtest_FiresWhenM5OpenInGreenZone()
    {
        var zones = GreenZoneOnly(100, 110, touchResult: -1, barClose: 120);
        var ctx = new AlertEvaluationContext
        {
            ChartTimeframeToken = "240",
            IsRealtime = false,
            ActiveZoneCollectionAvailable = true,
            ZoneState = zones,
            TouchLtfOpenPrice = 105,
        };

        var r = TouchRealEvaluators.EvaluateTouchBuy(ctx);

        Assert.True(r.Fired);
        Assert.Contains("ltfOpenM5", r.ReasonText);
    }

    /// <summary>
    /// ResolveZoneTouchT on HTF with two green zones: probe inside upper zone must return
    /// tTouch=1 even though buffered-nearest would pick the lower zone.
    /// </summary>
    [Fact]
    public void ResolveZoneTouchT_H1Backtest_TwoGreenZones_ProbeInsideUpperZone_ReturnsGreenTouch()
    {
        // Two green zones: lower L027=[157.390-157.721], upper L026=[157.85-158.25]
        // Buffered-nearest picks L027 (|157.95-157.721|=0.229 < |157.95-158.25|=0.30) → t=0
        // Pine full-scan: 157.95 in [157.85-158.25] → t=1
        // Combined → t=1
        var zones = new ZoneState
        {
            GreenKeyZones = new[] { (157.390, 157.721), (157.85, 158.25) },
            GreenObZones  = Array.Empty<(double, double)>(),
            RedKeyZones   = Array.Empty<(double, double)>(),
            RedObZones    = Array.Empty<(double, double)>(),
            BarClose      = 157.952,
            ZoneTouchResult = 0,
        };

        var ctx = new AlertEvaluationContext
        {
            ChartTimeframeToken = "60",
            IsRealtime = false,
            ActiveZoneCollectionAvailable = true,
            ZoneState = zones,
            TouchLtfOpenPrice = 157.95,
        };

        var (t, price, kind) = TouchRealEvaluators.ResolveZoneTouchT(ctx, AlertConditionId.CanSellTouchM5);

        Assert.Equal(1, t);           // green touch → canSellTouchM5 would BLOCK
        Assert.Equal(157.95, price);
        Assert.Equal("ltfOpenM5", kind);
    }
}
