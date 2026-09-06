using System;
using TradeAlert.Core.Engines.AlertEvaluators;
using TradeAlert.Core.Models.Alerts;
using Xunit;

namespace TradeAlert.Core.Tests;

/// <summary>
/// Tests for CheckTouchZonesCombined — Pine full-scan OR buffered nearest (union).
/// Key property: price inside any collected zone must always register as a touch,
/// even when the buffered-nearest metric would pick a different zone.
/// </summary>
public sealed class TouchCombinedZonesTests
{
    static ZoneState Zones(
        (double lo, double hi)[]? greenKey = null,
        (double lo, double hi)[]? greenOb  = null,
        (double lo, double hi)[]? redKey   = null,
        (double lo, double hi)[]? redOb    = null) =>
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
            : Array.ConvertAll(bands, b => (b.lo, b.hi));

    /// <summary>
    /// USDJPY case: probe=157.95 is inside upper green zone [157.85-158.25] (L026).
    /// Buffered-only misses it because lower zone [157.39-157.721] (L027) wins nearest
    /// (|157.95 - 157.721| = 0.229 < |157.95 - 158.25| = 0.30).
    /// Combined must return 1 (green touch).
    /// </summary>
    [Fact]
    public void Combined_PineDetectsInsideUpperZone_WhenNearestPicksLowerZone()
    {
        var z = Zones(greenKey: new[] { (157.390, 157.721), (157.85, 158.25) });
        double price = 157.95;

        // Verify buffered alone misses it
        Assert.Equal(0, TouchRealEvaluators.CheckTouchZonesBuffered(price, z));

        // Combined picks it up via Pine full-scan
        Assert.Equal(1, TouchRealEvaluators.CheckTouchZonesCombined(price, z));
    }

    /// <summary>
    /// Buffer near-miss preserved: price just above top of green OB is caught by buffer
    /// even though Pine strict overlap returns 0.
    /// </summary>
    [Fact]
    public void Combined_BufferExtends_WhenPineStrictMisses()
    {
        // height=0.005, OB a=2 → buffer=0.0003125; top=1.0950
        var z = Zones(greenOb: new[] { (1.0900, 1.0950) });
        double price = 1.0953;

        Assert.Equal(0, TouchRealEvaluators.CheckTouchZones(price, price, z));   // Pine strict: no
        Assert.Equal(1, TouchRealEvaluators.CheckTouchZonesCombined(price, z));  // Combined: yes via buffer
    }

    /// <summary>
    /// Price inside red zone, no green zones — combined returns -1.
    /// </summary>
    [Fact]
    public void Combined_PriceInsideRedZone_ReturnsMinus1()
    {
        var z = Zones(redKey: new[] { (1.1000, 1.1050) });
        double price = 1.1020;

        Assert.Equal(-1, TouchRealEvaluators.CheckTouchZonesCombined(price, z));
    }

    /// <summary>
    /// Price touches both a green and a red zone (Pine strict) → returns 2.
    /// </summary>
    [Fact]
    public void Combined_BothColors_ReturnsTwo()
    {
        var z = Zones(
            greenKey: new[] { (1.0900, 1.1000) },
            redKey:   new[] { (1.1000, 1.1100) });

        Assert.Equal(2, TouchRealEvaluators.CheckTouchZonesCombined(1.1000, z));
    }

    /// <summary>
    /// Price is far from all zones — both Pine and buffered return 0, combined also 0.
    /// Regression: buffer should NOT invent a touch for a price nowhere near any zone.
    /// </summary>
    [Fact]
    public void Combined_FarFromAllZones_ReturnsZero()
    {
        var z = Zones(
            redKey: new[] { (1.1000, 1.1050), (1.1200, 1.1250) });

        // price=1.1190: nearest bot=1.1200 (dist=0.001); buffer=0.000625; 1.1190+0.000625=1.1196 < 1.1200 → no touch
        Assert.Equal(0, TouchRealEvaluators.CheckTouchZonesBuffered(1.1190, z));
        Assert.Equal(0, TouchRealEvaluators.CheckTouchZonesCombined(1.1190, z));
    }

    /// <summary>
    /// Price inside one of two red zones (nearest wins correctly for Buffered too) — combined -1.
    /// </summary>
    [Fact]
    public void Combined_NearestRedWins_InsideZone()
    {
        var z = Zones(redKey: new[] { (1.1000, 1.1050), (1.1200, 1.1250) });
        // price=1.1020: inside first zone, nearest bot=1.1000 ✓
        Assert.Equal(-1, TouchRealEvaluators.CheckTouchZonesBuffered(1.1020, z));
        Assert.Equal(-1, TouchRealEvaluators.CheckTouchZonesCombined(1.1020, z));
    }

    /// <summary>
    /// Green from Pine (probe inside upper zone), not touching red → Combined = 1.
    /// Then probe well above both green zones and inside red zone → Combined = -1.
    /// </summary>
    [Fact]
    public void Combined_GreenUpperZoneViaPlane_ThenRedZone()
    {
        var z = Zones(
            greenKey: new[] { (157.390, 157.721), (157.85, 158.25) },
            redKey:   new[] { (158.339, 158.420) });

        // probe=157.95 inside green [157.85-158.25] via Pine; far from red bot 158.339 → green only
        Assert.Equal(1, TouchRealEvaluators.CheckTouchZonesCombined(157.95, z));

        // probe=158.38 inside red [158.339-158.420]; above green top 158.25 (price < bot? no, 158.38>158.25)
        // pine: red overlap yes; green: 158.38 >= 158.25 = top but price-buffer <= top? 158.38 - (0.40/8)=158.38-0.05=158.33 <= 158.25? no
        // So no green touch, red touch → -1
        Assert.Equal(-1, TouchRealEvaluators.CheckTouchZonesCombined(158.38, z));
    }
}
