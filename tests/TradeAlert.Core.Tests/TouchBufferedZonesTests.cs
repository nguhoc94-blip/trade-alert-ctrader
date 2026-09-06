using TradeAlert.Core.Engines.AlertEvaluators;
using TradeAlert.Core.Models.Alerts;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class TouchBufferedZonesTests
{
    static ZoneState Zones(
        (double lo, double hi)[]? greenKey = null,
        (double lo, double hi)[]? greenOb = null,
        (double lo, double hi)[]? redKey = null,
        (double lo, double hi)[]? redOb = null) =>
        new()
        {
            GreenKeyZones = ToBands(greenKey),
            GreenObZones  = ToBands(greenOb),
            RedKeyZones   = ToBands(redKey),
            RedObZones    = ToBands(redOb),
        };

    static (double Low, double High)[] ToBands((double lo, double hi)[]? bands) =>
        bands == null
            ? Array.Empty<(double, double)>()
            : bands.Select(b => (b.lo, b.hi)).ToArray();

    [Fact]
    public void RedKey_TouchesWhenPriceSlightlyBelowBotWithinBuffer()
    {
        // height=0.005, a=1 → buffer=0.000625; touch when price + buffer >= 1.1000
        var z = Zones(redKey: new[] { (1.1000, 1.1050) });

        Assert.Equal(-1, TouchRealEvaluators.CheckTouchZonesBuffered(1.0995, z));
        Assert.Equal(0, TouchRealEvaluators.CheckTouchZonesBuffered(1.0993, z));
    }

    [Fact]
    public void RedKey_NoTouchWhenPriceAboveTop()
    {
        var z = Zones(redKey: new[] { (1.1000, 1.1050) });

        Assert.Equal(0, TouchRealEvaluators.CheckTouchZonesBuffered(1.1060, z));
    }

    [Fact]
    public void GreenOb_TouchesWhenPriceSlightlyAboveTopWithinBuffer()
    {
        // height=0.005, a=2 → buffer=0.0003125
        var z = Zones(greenOb: new[] { (1.0900, 1.0950) });

        Assert.Equal(1, TouchRealEvaluators.CheckTouchZonesBuffered(1.0953, z));
        Assert.Equal(0, TouchRealEvaluators.CheckTouchZonesBuffered(1.0955, z));
    }

    [Fact]
    public void GreenKey_NoTouchWhenPriceBelowBot()
    {
        var z = Zones(greenKey: new[] { (1.0900, 1.0950) });

        Assert.Equal(0, TouchRealEvaluators.CheckTouchZonesBuffered(1.0890, z));
    }

    [Fact]
    public void NearestRedWins_NotFartherZone()
    {
        var z = Zones(
            redKey: new[] { (1.1000, 1.1050), (1.1200, 1.1250) });

        // Closer to bot=1.1000; inside first zone
        Assert.Equal(-1, TouchRealEvaluators.CheckTouchZonesBuffered(1.1020, z));

        // Nearest bot is 1.1200 (dist=0.0010 vs 1.1000 dist=0.0200) — second zone, not inside
        Assert.Equal(0, TouchRealEvaluators.CheckTouchZonesBuffered(1.1190, z));
    }

    [Fact]
    public void BothColors_ReturnsTwo()
    {
        var z = Zones(
            greenKey: new[] { (1.0900, 1.1000) },
            redKey: new[] { (1.1000, 1.1100) });

        Assert.Equal(2, TouchRealEvaluators.CheckTouchZonesBuffered(1.1000, z));
    }

    [Fact]
    public void RealRangeOverlap_StillUsesFullBand()
    {
        var z = Zones(greenKey: new[] { (100.0, 110.0) });

        // Buffered green: price slightly above top still touches (buffer=10/8=1.25)
        Assert.Equal(1, TouchRealEvaluators.CheckTouchZonesBuffered(110.5, z));

        // Real range overlap: unchanged full-band logic — above top = no touch
        Assert.Equal(0, TouchRealEvaluators.CheckTouchZones(110.5, 110.5, z));
    }

    [Fact]
    public void TieBreakDistance_PrefersKeyOverOb()
    {
        // Same bot distance: red key bot=100, red ob bot=100
        var z = Zones(
            redKey: new[] { (100.0, 110.0) },
            redOb: new[] { (100.0, 108.0) });

        // Key buffer = 10/8=1.25; OB buffer = 8/16=0.5
        // price=99.0 → key: 99+1.25=100.25>=100 touch; ob would not touch alone
        Assert.Equal(-1, TouchRealEvaluators.CheckTouchZonesBuffered(99.0, z));
    }
}
