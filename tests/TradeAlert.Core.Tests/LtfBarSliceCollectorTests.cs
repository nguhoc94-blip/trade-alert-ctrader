using System;
using System.Collections.Generic;
using TradeAlert.Core.Engines;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class LtfBarSliceCollectorTests
{
    static readonly DateTime H4Open = new(2024, 6, 3, 10, 0, 0, DateTimeKind.Unspecified);

    [Fact]
    public void H4_window_collects_four_h1_bars()
    {
        var htfOpenNext = H4Open.AddHours(4);
        var opens = new[]
        {
            H4Open.AddHours(-1),
            H4Open,
            H4Open.AddHours(1),
            H4Open.AddHours(2),
            H4Open.AddHours(3),
            htfOpenNext,
        };
        var o = new[] { 1.0, 1.1, 1.2, 1.3, 1.4, 1.5 };
        var h = new[] { 2.0, 2.1, 2.2, 2.3, 2.4, 2.5 };
        var l = new[] { 0.5, 0.6, 0.7, 0.8, 0.9, 1.0 };
        var c = new[] { 1.5, 1.6, 1.7, 1.8, 1.9, 2.0 };

        var bundle = LtfBarSliceCollector.Collect(H4Open, htfOpenNext, opens, o, h, l, c, htfSeconds: 14400, ltfSeconds: 3600);
        Assert.NotNull(bundle);
        Assert.Equal(4, bundle!.Count);
        Assert.Equal(1.1, bundle.OpenAt(0));
        Assert.Equal(1.4, bundle.OpenAt(3));
    }

    [Fact]
    public void Empty_window_returns_null()
    {
        var htfOpenNext = H4Open.AddHours(4);
        var opens = new[] { H4Open.AddHours(-4), H4Open.AddHours(-3) };
        var o = new[] { 1.0, 1.1 };
        var h = new[] { 2.0, 2.1 };
        var l = new[] { 0.5, 0.6 };
        var c = new[] { 1.5, 1.6 };

        Assert.Null(LtfBarSliceCollector.Collect(H4Open, htfOpenNext, opens, o, h, l, c));
    }

    [Fact]
    public void M15_window_collects_three_m5_bars()
    {
        var m15Open = new DateTime(2024, 6, 3, 10, 0, 0, DateTimeKind.Unspecified);
        var m15Next = m15Open.AddMinutes(15);
        var opens = new List<DateTime>();
        var o = new List<double>();
        var h = new List<double>();
        var l = new List<double>();
        var c = new List<double>();
        for (var m = 0; m < 20; m++)
        {
            var t = m15Open.AddMinutes(m * 5 - 10);
            opens.Add(t);
            o.Add(m);
            h.Add(m + 0.1);
            l.Add(m - 0.1);
            c.Add(m + 0.05);
        }

        var bundle = LtfBarSliceCollector.Collect(m15Open, m15Next, opens, o, h, l, c, htfSeconds: 900, ltfSeconds: 300);
        Assert.NotNull(bundle);
        Assert.Equal(3, bundle!.Count);
    }

    [Fact]
    public void M5_window_collects_four_m2_bars_with_extended_slice()
    {
        var m5Open = new DateTime(2024, 6, 3, 10, 0, 0, DateTimeKind.Unspecified);
        var m5Next = m5Open.AddMinutes(5);
        var opens = new[]
        {
            m5Open.AddMinutes(-4),
            m5Open,
            m5Open.AddMinutes(2),
            m5Open.AddMinutes(4),
            m5Next.AddMinutes(1), // 10:06 — nến M2 thứ 4 (overlap tail M5)
            m5Next.AddMinutes(3),
        };
        var o = new[] { 0.9, 1.1, 1.2, 1.3, 1.4, 1.6 };
        var h = new[] { 1.9, 2.1, 2.2, 2.3, 2.4, 2.6 };
        var l = new[] { 0.4, 0.6, 0.7, 0.8, 0.9, 1.1 };
        var c = new[] { 1.4, 1.6, 1.7, 1.8, 1.9, 2.1 };

        var bundle = LtfBarSliceCollector.Collect(m5Open, m5Next, opens, o, h, l, c, htfSeconds: 300, ltfSeconds: 120);
        Assert.NotNull(bundle);
        Assert.Equal(4, bundle!.Count);
        Assert.Equal(1.1, bundle.OpenAt(0));
        Assert.Equal(1.2, bundle.OpenAt(1));
        Assert.Equal(1.3, bundle.OpenAt(2));
        Assert.Equal(1.4, bundle.OpenAt(3));
    }

    [Fact]
    public void M5_extended_slice_includes_trailing_m2_not_next_m5_open()
    {
        var m5Open = new DateTime(2024, 6, 3, 10, 0, 0, DateTimeKind.Unspecified);
        var m5Next = m5Open.AddMinutes(5);
        var opens = new[] { m5Open, m5Open.AddMinutes(2), m5Open.AddMinutes(4), m5Next.AddMinutes(1) };
        var o = new[] { 1.0, 1.1, 1.2, 1.3 };
        var h = new[] { 2.0, 2.1, 2.2, 2.3 };
        var l = new[] { 0.5, 0.6, 0.7, 0.8 };
        var c = new[] { 1.5, 1.6, 1.7, 1.8 };

        var bundle = LtfBarSliceCollector.Collect(m5Open, m5Next, opens, o, h, l, c, htfSeconds: 300, ltfSeconds: 120);
        Assert.NotNull(bundle);
        Assert.Equal(4, bundle!.Count);
        Assert.Equal(1.3, bundle.OpenAt(3));
    }

    [Fact]
    public void M5_consecutive_windows_share_trailing_m2_bar()
    {
        var m5Open1 = new DateTime(2024, 6, 3, 10, 0, 0, DateTimeKind.Unspecified);
        var m5Next1 = m5Open1.AddMinutes(5);
        var m5Open2 = m5Next1;
        var m5Next2 = m5Open2.AddMinutes(5);

        var opens = new List<DateTime>();
        var o = new List<double>();
        for (var i = 0; i < 12; i++)
        {
            opens.Add(m5Open1.AddMinutes(i * 2 - 2));
            o.Add(i);
        }

        var h = new List<double>(o.Count);
        var l = new List<double>(o.Count);
        var c = new List<double>(o.Count);
        for (var i = 0; i < o.Count; i++)
        {
            h.Add(o[i] + 0.1);
            l.Add(o[i] - 0.1);
            c.Add(o[i] + 0.05);
        }

        var b1 = LtfBarSliceCollector.Collect(m5Open1, m5Next1, opens, o, h, l, c, htfSeconds: 300, ltfSeconds: 120);
        var b2 = LtfBarSliceCollector.Collect(m5Open2, m5Next2, opens, o, h, l, c, htfSeconds: 300, ltfSeconds: 120);

        Assert.NotNull(b1);
        Assert.NotNull(b2);
        Assert.Equal(4, b1!.Count);
        Assert.Equal(4, b2!.Count);
        Assert.Equal(b1.OpenAt(3), b2.OpenAt(0));
    }

    [Fact]
    public void CountLtfBarsForHtf_M5_returns_four_m2_bars()
    {
        Assert.Equal(4, LtfBarSliceCollector.CountLtfBarsForHtf(htfSeconds: 300, ltfSeconds: 120));
        Assert.True(LtfBarSliceCollector.UsesConsecutiveLtfSlice(300, 120));
    }

    [Fact]
    public void CountLtfBarsForHtf_H4_uses_half_open_window()
    {
        Assert.Equal(4, LtfBarSliceCollector.CountLtfBarsForHtf(htfSeconds: 14400, ltfSeconds: 3600));
        Assert.False(LtfBarSliceCollector.UsesConsecutiveLtfSlice(14400, 3600));
    }

    [Fact]
    public void TimeframeMapping_M5_resolves_M2_ltf()
    {
        Assert.Equal("5", TimeframeMapping.ChartTfToken(300));
        Assert.Equal("2", TimeframeMapping.ResolveLtfTokenByHtf(300));
        Assert.Equal(120, TimeframeMapping.ResolveLtfSecondsByHtf(300));
    }
}
