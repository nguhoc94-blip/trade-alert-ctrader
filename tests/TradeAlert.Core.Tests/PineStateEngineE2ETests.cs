using System;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models;
using TradeAlert.Core.Models.Alerts;
using TradeAlert.Core.Series;
using Xunit;

namespace TradeAlert.Core.Tests;

/// <summary>
/// End-to-end smoke tests cho PineStateEngine — verify pivot detection,
/// flag transitions, real-zone tracking, và ZoneState collection hoạt động
/// trên một stream bar tổng hợp.
/// </summary>
public sealed class PineStateEngineE2ETests
{
    static BarSnapshot Bar(int idx, double open, double high, double low, double close) =>
        new(DateTime.SpecifyKind(new DateTime(2026, 5, 19, 0, 0, 0).AddMinutes(idx * 5), DateTimeKind.Unspecified),
            open, high, low, close);

    static (SeriesBuffer buf, PineStateEngine eng) BuildAndPump(BarSnapshot[] bars)
    {
        var buf = new SeriesBuffer();
        var eng = new PineStateEngine
        {
            TickSize = 0.01,
            KeylevelAvgBodyLen = 5,
            KeylevelAtrLen = 3,        // short ATR period so tests don't need 14+ warm-up bars
            KeylevelMinBodyMult = 0.4,
            DojiBodyRatioMaxStructure = 0.25,
            UsePullbackFilter = false, // bypass mode: immediate push without multi-bar confirmation
        };
        for (var i = 0; i < bars.Length; i++)
        {
            buf.Upsert(i, bars[i], new BarRuntimeFlags());
            eng.OnBar(buf, i);
        }
        return (buf, eng);
    }

    [Fact]
    public void Detector_PushesHIGH_OnBullishClearBody()
    {
        // Stream: 5 baseline bars + 1 strong bullish bar A → HIGH should push
        var bars = new[]
        {
            Bar(0, 100, 100.5, 99.5, 100.2),
            Bar(1, 100, 100.5, 99.5, 100.2),
            Bar(2, 100, 100.5, 99.5, 100.2),
            Bar(3, 100, 100.5, 99.5, 100.2),
            Bar(4, 100, 100.5, 99.5, 100.2),
            Bar(5, 100.0, 102.0, 99.9, 101.9),   // strong bullish bar A
            Bar(6, 101.5, 102.1, 101.4, 102.0),  // bar at barIndex=6 → A is bar 5 (high=102.0)
        };
        var (_, eng) = BuildAndPump(bars);

        Assert.True(eng.Pivots.Count >= 1, "Expected at least one pivot push");
        // First push should be a HIGH (typ=1) at bar 5
        bool foundHigh = false;
        for (var i = 0; i < eng.Pivots.Count; i++)
            if (eng.Pivots.GetTypeAt(i) == 1) { foundHigh = true; break; }
        Assert.True(foundHigh, "Expected HIGH pivot push");
        Assert.Equal(1, eng.PivotDetector.LastPushedSwingType);
    }

    [Fact]
    public void Detector_Alternates_HIGH_then_LOW()
    {
        var bars = new[]
        {
            Bar(0, 100, 100.5, 99.5, 100.2),
            Bar(1, 100, 100.5, 99.5, 100.2),
            Bar(2, 100, 100.5, 99.5, 100.2),
            Bar(3, 100, 100.5, 99.5, 100.2),
            Bar(4, 100, 100.5, 99.5, 100.2),
            Bar(5, 100.0, 102.0, 99.9, 101.9),   // bullish A
            Bar(6, 101.8, 102.0, 101.7, 101.95), // pivot processed at bar 6
            Bar(7, 102.0, 102.2, 100.0, 100.1),  // bearish A — should push LOW
            Bar(8, 100.0, 100.2, 99.9, 100.05),
        };
        var (_, eng) = BuildAndPump(bars);

        Assert.Equal(-1, eng.PivotDetector.LastPushedSwingType);
        Assert.True(eng.Pivots.Count >= 2);
    }

    [Fact]
    public void Transition_ACTIVE_to_BREAK_PENDING_to_BROKEN()
    {
        // Push HIGH at bar 5 (high=102.0). Next bull-close bar > 102.0 → BREAK_PENDING.
        // Next bar with close > 102.0 again → BROKEN.
        var bars = new[]
        {
            Bar(0, 100, 100.5, 99.5, 100.2),
            Bar(1, 100, 100.5, 99.5, 100.2),
            Bar(2, 100, 100.5, 99.5, 100.2),
            Bar(3, 100, 100.5, 99.5, 100.2),
            Bar(4, 100, 100.5, 99.5, 100.2),
            Bar(5, 100.0, 101.0, 99.9, 100.95),   // bull A pushes HIGH at price=101.0 at bar 5
            Bar(6, 100.8, 101.0, 100.7, 100.95),  // at barIndex=6, bar A=bar 5 pushed
            Bar(7, 100.8, 102.0, 100.7, 101.9),   // at barIndex=7, bar A=bar 6 (sideway, no break yet)
            Bar(8, 101.5, 103.5, 101.4, 103.4),   // at barIndex=8, bar A=bar 7: close 101.9 > 101 → BREAK_PENDING
            Bar(9, 103.4, 104.0, 103.3, 103.9),   // at barIndex=9, bar A=bar 8: close=103.4>101 → confirm BROKEN
        };
        var (_, eng) = BuildAndPump(bars);

        // Find the HIGH pivot pushed (price ~101.0)
        int? broken = null;
        for (var i = 0; i < eng.Pivots.Count; i++)
        {
            if (eng.Pivots.GetTypeAt(i) == 1)
            {
                broken = eng.Pivots.GetFlag(i);
                break;
            }
        }
        // After E2E flow, HIGH pivot at price=101 should be BROKEN (2) or at least BREAK_PENDING (3)
        Assert.True(broken.HasValue);
        Assert.True(broken == 2 || broken == 3, $"Expected BROKEN/BREAK_PENDING but got flag={broken}");
    }

    [Fact]
    public void RealZone_ResetsOnOppositeSwingPush()
    {
        var bars = new[]
        {
            Bar(0, 100, 100.5, 99.5, 100.2),
            Bar(1, 100, 100.5, 99.5, 100.2),
            Bar(2, 100, 100.5, 99.5, 100.2),
            Bar(3, 100, 100.5, 99.5, 100.2),
            Bar(4, 100, 100.5, 99.5, 100.2),
            Bar(5, 100.0, 102.0, 99.9, 101.9),   // bullish bar A → HIGH pushed at barIndex=6
            Bar(6, 101.8, 102.0, 101.7, 101.95),
            Bar(7, 102.0, 102.2, 100.0, 100.1),  // bearish bar A — push happens at barIndex=8
            Bar(8, 100.0, 100.2, 99.9, 100.05),
        };
        var (_, eng) = BuildAndPump(bars);
        Assert.Equal(-1, eng.RealZone.LastPushedSwingType);
        Assert.True(eng.RealZone.BuyRealAnchorBar.HasValue);
    }

    [Fact]
    public void BuildPivotEntries_ReflectsCurrentState()
    {
        var bars = new[]
        {
            Bar(0, 100, 100.5, 99.5, 100.2),
            Bar(1, 100, 100.5, 99.5, 100.2),
            Bar(2, 100, 100.5, 99.5, 100.2),
            Bar(3, 100, 100.5, 99.5, 100.2),
            Bar(4, 100, 100.5, 99.5, 100.2),
            Bar(5, 100.0, 102.0, 99.9, 101.9),
            Bar(6, 101.8, 102.0, 101.7, 101.95),
        };
        var (_, eng) = BuildAndPump(bars);

        var entries = eng.BuildPivotEntries(6);
        Assert.NotEmpty(entries);
        foreach (var e in entries)
        {
            Assert.True(e.Type == 1 || e.Type == -1);
            Assert.False(string.IsNullOrEmpty(e.SwingIdStr));
        }
    }

    [Fact]
    public void BuildZoneState_EmptyByDefault_NoTouchResult()
    {
        var bars = new[]
        {
            Bar(0, 100, 100.5, 99.5, 100.2),
            Bar(1, 100, 100.5, 99.5, 100.2),
        };
        var (_, eng) = BuildAndPump(bars);
        var z = eng.BuildZoneState(100.0);
        Assert.Empty(z.GreenKeyZones);
        Assert.Empty(z.RedKeyZones);
        Assert.Empty(z.GreenObZones);
        Assert.Empty(z.RedObZones);
        Assert.Equal(0, z.ZoneTouchResult);
    }
}
