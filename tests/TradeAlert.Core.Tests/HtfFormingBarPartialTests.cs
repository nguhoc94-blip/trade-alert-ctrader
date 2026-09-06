using System;
using System.Collections.Generic;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.KeyLevel;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class HtfFormingBarPartialTests
{
    [Fact]
    public void Collect_ClipBefore_ExcludesFutureLtfBars()
    {
        var htfOpen = new DateTime(2024, 7, 15, 4, 0, 0);
        var htfNext = new DateTime(2024, 7, 15, 5, 0, 0);
        var clip = new DateTime(2024, 7, 15, 4, 15, 0);

        var opens = new List<DateTime>
        {
            new(2024, 7, 15, 4, 0, 0),
            new(2024, 7, 15, 4, 5, 0),
            new(2024, 7, 15, 4, 10, 0),
            new(2024, 7, 15, 4, 15, 0),
        };
        var o = new List<double> { 1.0, 1.1, 1.2, 1.3 };
        var h = new List<double> { 1.5, 1.6, 1.7, 1.8 };
        var l = new List<double> { 0.9, 1.0, 1.1, 1.2 };
        var c = new List<double> { 1.4, 1.5, 1.6, 1.7 };

        var full = LtfBarSliceCollector.Collect(htfOpen, htfNext, opens, o, h, l, c, htfSeconds: 3600, ltfSeconds: 300);
        var clipped = LtfBarSliceCollector.Collect(
            htfOpen, htfNext, opens, o, h, l, c, htfSeconds: 3600, ltfSeconds: 300, clipBeforeExclusive: clip);

        Assert.NotNull(full);
        Assert.NotNull(clipped);
        Assert.Equal(4, full!.Count);
        Assert.Equal(3, clipped!.Count);
    }

    [Fact]
    public void TryAggregateFromLtfBundle_ComputesHighLowClose()
    {
        var bundle = new LtfBarBundle(
            new[] { 1.0, 1.1 },
            new[] { 1.5, 1.8 },
            new[] { 0.9, 1.0 },
            new[] { 1.4, 1.6 });

        Assert.True(HtfFormingBarPartial.TryAggregateFromLtfBundle(1.0, bundle, out var hi, out var lo, out var cl));
        Assert.Equal(1.8, hi);
        Assert.Equal(0.9, lo);
        Assert.Equal(1.6, cl);
    }
}
