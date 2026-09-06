using System;
using System.Collections.Generic;
using TradeAlert.Core.Engines;
using Xunit;

namespace TradeAlert.Core.Tests.Engines;

public sealed class HtfPartialOhlcFromM5Tests
{
    static readonly TimeSpan M5 = TimeSpan.FromMinutes(5);

    [Fact]
    public void TryAggregate_clips_before_snapshot_without_future_H1_extremes()
    {
        var h1Open = new DateTime(2024, 6, 18, 10, 0, 0);
        var m5Opens = new List<DateTime>();
        var o = new List<double>();
        var h = new List<double>();
        var l = new List<double>();
        var c = new List<double>();

        for (var i = 0; i < 12; i++)
        {
            var t = h1Open.Add(TimeSpan.FromMinutes(5 * i));
            m5Opens.Add(t);
            o.Add(1.0 + i * 0.001);
            h.Add(1.01 + i * 0.001);
            l.Add(0.99 + i * 0.001);
            c.Add(1.005 + i * 0.001);
        }

        // Last M5 bar spikes high — must NOT appear when snapshot stops at bar 7 close.
        h[^1] = 2.50;
        l[^1] = 0.10;

        var snapshotClose = h1Open.Add(TimeSpan.FromMinutes(5 * 7));
        Assert.True(HtfPartialOhlcFromM5.TryAggregate(
            m5Opens, o, h, l, c, M5, h1Open, snapshotClose, static t => t,
            out var aggOpen, out var aggHigh, out var aggLow, out var aggClose, out _));

        Assert.Equal(o[0], aggOpen);
        Assert.Equal(c[6], aggClose);
        Assert.True(aggHigh < 2.0, "future M5 spike must not leak into partial H1 high");
        Assert.True(aggLow > 0.5, "future M5 dip must not leak into partial H1 low");

        var maxIncluded = 0.0;
        for (var i = 0; i < 7; i++)
            if (h[i] > maxIncluded) maxIncluded = h[i];
        Assert.Equal(maxIncluded, aggHigh);
    }

    [Fact]
    public void TryAggregate_uses_M5_close_at_snapshot_not_full_HTF_bar()
    {
        var h1Open = new DateTime(2024, 6, 18, 11, 0, 0);
        var m5Opens = new[] { h1Open, h1Open.AddMinutes(5), h1Open.AddMinutes(10) };
        var o = new[] { 1.0, 1.1, 1.2 };
        var h = new[] { 1.05, 1.15, 1.25 };
        var l = new[] { 0.95, 1.05, 1.15 };
        var c = new[] { 1.02, 1.12, 1.22 };

        var snapshotClose = h1Open.AddMinutes(10);
        Assert.True(HtfPartialOhlcFromM5.TryAggregate(
            m5Opens, o, h, l, c, M5, h1Open, snapshotClose, static t => t,
            out _, out _, out _, out var aggClose, out _));

        Assert.Equal(1.12, aggClose);
    }
}
