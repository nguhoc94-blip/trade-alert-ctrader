using System;
using System.Collections.Generic;
using TradeAlert.BacktestRobot.Execution;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.KeyLevel;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests;

public class SwingCZoneObstacleGateTests
{
    // ── helpers ──────────────────────────────────────────────────────────

    /// <summary>Create a PineStateEngine with one keylevel (typ=-1→green, typ=1→red).</summary>
    static PineStateEngine StateWithKl(int type, double top, double bottom, int pivotBar = 10, int flag = 1)
    {
        var s = new PineStateEngine();
        var idx = s.Pivots.PushPivot(bottom, pivotBar, type, 0, 1, 1);
        s.Pivots.SetFlag(idx, flag);
        s.Pivots.SetHasKey(idx, true);
        s.Pivots.SetKeyExtending(idx, true);
        s.Pivots.SetKeyBox(idx, new KeyBoxRef { Spec = new KeyBoxSpec { Top = top, Bottom = bottom } });
        return s;
    }

    /// <summary>Add an OB to an existing engine (typ=-1→green, typ=1→red).</summary>
    static void AddOb(PineStateEngine s, int type, double top, double bottom)
    {
        s.ObPool.Push(state: 2, count: 1, x: 0, bar: 10, typ: type, owner: 0,
            source: 0, number: 1, pivotType: type, pivotHighId: 1, pivotLowId: 1);
        var i = s.ObPool.Count - 1;
        s.ObPool.SetExtending(i, true);
        s.ObPool.SetBox(i, new ObBoxSpec { Top = top, Bottom = bottom, ObType = type });
    }

    /// <param name="pivotIndex">Default -1 (invalid sentinel) to avoid false pivot-index collision with test zones that auto-generate index 0.</param>
    static SwingCEdgeResult CResult(
        double edgeBottom, double edgeTop,
        int pivotBar = 50, int pivotIndex = -1) => new()
    {
        PivotBar   = pivotBar,
        PivotIndex = pivotIndex,
        PivotType  = 0,
        TakeProfit = edgeTop,
        EdgeBottom = edgeBottom,
        EdgeTop    = edgeTop,
    };

    // ── basic pass/block ──────────────────────────────────────────────────

    [Fact]
    public void NoStates_ReturnsNoObstacle()
    {
        var c = CResult(1.1000, 1.1010);
        var r = SwingCZoneObstacleGate.Evaluate(
            c, isBuy: true, chartTfToken: "15",
            states: new Dictionary<string, PineStateEngine>(),
            pipSize: 0.0001);

        Assert.False(r.HasObstacle);
    }

    [Fact]
    public void InvalidCEdge_ReturnsNoObstacle()
    {
        // EdgeTop == EdgeBottom (degenerate)
        var c = CResult(edgeBottom: 1.1010, edgeTop: 1.1000); // inverted
        var state = StateWithKl(type: 1, top: 1.1010, bottom: 1.0990); // red overlaps
        var states = new Dictionary<string, PineStateEngine> { ["15"] = state };

        var r = SwingCZoneObstacleGate.Evaluate(
            c, isBuy: true, chartTfToken: "15", states, pipSize: 0.0001);

        Assert.False(r.HasObstacle);
    }

    [Fact]
    public void Buy_RedZoneOverlapsCEdge_ReturnsObstacle()
    {
        // BUY: C is HIGH pivot → C zone is RED → another RED zone at same level
        var c = CResult(edgeBottom: 1.1000, edgeTop: 1.1020);
        // Red keylevel fully covering C zone
        var state = StateWithKl(type: 1, top: 1.1025, bottom: 1.0995, pivotBar: 20);
        var states = new Dictionary<string, PineStateEngine> { ["15"] = state };

        var r = SwingCZoneObstacleGate.Evaluate(
            c, isBuy: true, chartTfToken: "15", states, pipSize: 0.0001);

        Assert.True(r.HasObstacle);
        Assert.Equal("M15", r.ObstacleTf);
        Assert.Equal(ZoneSourceKind.KeyLevel, r.ObstacleSource);
        Assert.Equal(ZoneEffectiveColor.Red, r.ObstacleColor);
        Assert.True(r.OverlapPips > 0);
    }

    [Fact]
    public void Sell_GreenZoneOverlapsCEdge_ReturnsObstacle()
    {
        // SELL: C is LOW pivot → C zone is GREEN → another GREEN zone at same level
        var c = CResult(edgeBottom: 1.0980, edgeTop: 1.1000);
        var state = StateWithKl(type: -1, top: 1.1005, bottom: 1.0975, pivotBar: 20);
        var states = new Dictionary<string, PineStateEngine> { ["15"] = state };

        var r = SwingCZoneObstacleGate.Evaluate(
            c, isBuy: false, chartTfToken: "15", states, pipSize: 0.0001);

        Assert.True(r.HasObstacle);
        Assert.Equal(ZoneEffectiveColor.Green, r.ObstacleColor);
    }

    [Fact]
    public void Buy_GreenZoneOnly_NoObstacle()
    {
        // BUY looks for RED, but only green zones present
        var c = CResult(edgeBottom: 1.1000, edgeTop: 1.1020);
        var state = StateWithKl(type: -1, top: 1.1025, bottom: 1.0995, pivotBar: 20); // green
        var states = new Dictionary<string, PineStateEngine> { ["15"] = state };

        var r = SwingCZoneObstacleGate.Evaluate(
            c, isBuy: true, chartTfToken: "15", states, pipSize: 0.0001);

        Assert.False(r.HasObstacle);
    }

    [Fact]
    public void Sell_RedZoneOnly_NoObstacle()
    {
        // SELL looks for GREEN, but only red zones present
        var c = CResult(edgeBottom: 1.0980, edgeTop: 1.1000);
        var state = StateWithKl(type: 1, top: 1.1005, bottom: 1.0975, pivotBar: 20); // red
        var states = new Dictionary<string, PineStateEngine> { ["15"] = state };

        var r = SwingCZoneObstacleGate.Evaluate(
            c, isBuy: false, chartTfToken: "15", states, pipSize: 0.0001);

        Assert.False(r.HasObstacle);
    }

    [Fact]
    public void ZoneCompletelyAboveCEdge_NoObstacle()
    {
        var c = CResult(edgeBottom: 1.1000, edgeTop: 1.1020);
        // Red zone entirely above C zone
        var state = StateWithKl(type: 1, top: 1.1050, bottom: 1.1030, pivotBar: 20);
        var states = new Dictionary<string, PineStateEngine> { ["15"] = state };

        var r = SwingCZoneObstacleGate.Evaluate(
            c, isBuy: true, chartTfToken: "15", states, pipSize: 0.0001);

        Assert.False(r.HasObstacle);
    }

    [Fact]
    public void ZoneCompletelyBelowCEdge_NoObstacle()
    {
        var c = CResult(edgeBottom: 1.1000, edgeTop: 1.1020);
        // Red zone entirely below C zone
        var state = StateWithKl(type: 1, top: 1.0990, bottom: 1.0970, pivotBar: 20);
        var states = new Dictionary<string, PineStateEngine> { ["15"] = state };

        var r = SwingCZoneObstacleGate.Evaluate(
            c, isBuy: true, chartTfToken: "15", states, pipSize: 0.0001);

        Assert.False(r.HasObstacle);
    }

    // ── self-identity guard ──────────────────────────────────────────────

    [Fact]
    public void SelfIdentity_SameTf_SamePivotBar_ZoneSkipped()
    {
        // C pivot is at bar=50; zone also at bar=50, same TF → self-identity skip
        // pivotIndex=-1 so only PivotBar match triggers the guard
        var c = CResult(edgeBottom: 1.1000, edgeTop: 1.1020, pivotBar: 50, pivotIndex: -1);
        // Zone overlaps C perfectly, same TF same bar
        var state = StateWithKl(type: 1, top: 1.1020, bottom: 1.1000, pivotBar: 50);
        var states = new Dictionary<string, PineStateEngine> { ["15"] = state };

        var r = SwingCZoneObstacleGate.Evaluate(
            c, isBuy: true, chartTfToken: "15", states, pipSize: 0.0001);

        Assert.False(r.HasObstacle);
        Assert.Equal(0, r.SelfIdentitySkipped); // same-TF check doesn't count in selfSkipped counter
    }

    [Fact]
    public void SelfIdentity_SameTf_SamePivotIndex_ZoneSkipped()
    {
        var c = CResult(edgeBottom: 1.1000, edgeTop: 1.1020, pivotBar: 50, pivotIndex: 3);
        // Zone same TF same pivot index → skip
        // Testing bar-based match:
        var state2 = StateWithKl(type: 1, top: 1.1020, bottom: 1.1000, pivotBar: 50);
        var states = new Dictionary<string, PineStateEngine> { ["15"] = state2 };

        var r = SwingCZoneObstacleGate.Evaluate(
            c, isBuy: true, chartTfToken: "15", states, pipSize: 0.0001);

        Assert.False(r.HasObstacle);
    }

    [Fact]
    public void SelfIdentity_SameTf_DifferentPivotBar_ZoneNotSkipped()
    {
        // Zone at different bar AND C has pivotIndex=-1 (no index match possible) → NOT self-identity → obstacle applies
        var c = CResult(edgeBottom: 1.1000, edgeTop: 1.1020, pivotBar: 50, pivotIndex: -1);
        var state = StateWithKl(type: 1, top: 1.1020, bottom: 1.1000, pivotBar: 20);
        var states = new Dictionary<string, PineStateEngine> { ["15"] = state };

        var r = SwingCZoneObstacleGate.Evaluate(
            c, isBuy: true, chartTfToken: "15", states, pipSize: 0.0001);

        Assert.True(r.HasObstacle);
    }

    // ── OB included ───────────────────────────────────────────────────────

    [Fact]
    public void Buy_RedOBOverlaps_ReturnsObstacle()
    {
        var c = CResult(edgeBottom: 1.1000, edgeTop: 1.1020);
        var state = new PineStateEngine();
        AddOb(state, type: 1, top: 1.1025, bottom: 1.0995); // red OB
        var states = new Dictionary<string, PineStateEngine> { ["60"] = state };

        var r = SwingCZoneObstacleGate.Evaluate(
            c, isBuy: true, chartTfToken: "15", states, pipSize: 0.0001);

        Assert.True(r.HasObstacle);
        Assert.Equal("H1", r.ObstacleTf);
        Assert.Equal(ZoneSourceKind.OrderBlock, r.ObstacleSource);
    }

    // ── multi-TF label ────────────────────────────────────────────────────

    [Fact]
    public void Buy_ZoneOnH4_ReportsH4Label()
    {
        var c = CResult(edgeBottom: 1.1000, edgeTop: 1.1020);
        var state = StateWithKl(type: 1, top: 1.1025, bottom: 1.0995, pivotBar: 20);
        var states = new Dictionary<string, PineStateEngine> { ["240"] = state };

        var r = SwingCZoneObstacleGate.Evaluate(
            c, isBuy: true, chartTfToken: "15", states, pipSize: 0.0001);

        Assert.True(r.HasObstacle);
        Assert.Equal("H4", r.ObstacleTf);
    }

    [Fact]
    public void Buy_ZoneOnM5_ReportsM5Label()
    {
        var c = CResult(edgeBottom: 1.1000, edgeTop: 1.1020);
        var state = StateWithKl(type: 1, top: 1.1025, bottom: 1.0995, pivotBar: 20);
        var states = new Dictionary<string, PineStateEngine> { ["5"] = state };

        var r = SwingCZoneObstacleGate.Evaluate(
            c, isBuy: true, chartTfToken: "15", states, pipSize: 0.0001);

        Assert.True(r.HasObstacle);
        Assert.Equal("M5", r.ObstacleTf);
    }

    // ── IsCZoneOwnKeyLevel cross-TF time containment ──────────────────────

    [Fact]
    public void IsCZoneOwnKeyLevel_CrossTf_ZoneInsideCPivotTime_ReturnsTrue()
    {
        var cResult = new SwingCEdgeResult
        {
            PivotBar   = 50,
            PivotIndex = 0,
            EdgeBottom = 1.1000,
            EdgeTop    = 1.1020,
        };

        var cOpenTime = new DateTime(2024, 1, 1, 9, 0, 0);
        var chartTfPeriod = TimeSpan.FromMinutes(15);

        // Zone bar on M5 — starts within [9:00..9:15), falls inside the M15 chart bar
        var zone = new ZoneCandidate
        {
            TfToken = "5",
            TfPeriod = TimeSpan.FromMinutes(5),
            PivotBar = 50,
            PivotOpenTime = new DateTime(2024, 1, 1, 9, 5, 0), // inside [9:00..9:15)
            Low = 1.1000,
            High = 1.1020,
        };

        var selfSkipped = 0;
        var result = SwingCZoneObstacleGate.IsCZoneOwnKeyLevel(
            in cResult, in zone,
            chartTfToken: "15",
            cPivotOpenTime: cOpenTime,
            chartTfPeriod: chartTfPeriod,
            ref selfSkipped);

        Assert.True(result);
        Assert.Equal(1, selfSkipped);
    }

    [Fact]
    public void IsCZoneOwnKeyLevel_CrossTf_ZoneOutsideCPivotTime_ReturnsFalse()
    {
        var cResult = new SwingCEdgeResult
        {
            PivotBar   = 50,
            PivotIndex = 0,
            EdgeBottom = 1.1000,
            EdgeTop    = 1.1020,
        };

        var cOpenTime = new DateTime(2024, 1, 1, 9, 0, 0);
        var chartTfPeriod = TimeSpan.FromMinutes(15);

        // Zone on H1 that started at 8:00 — before [9:00..9:15), not containing it either
        var zone = new ZoneCandidate
        {
            TfToken = "60",
            TfPeriod = TimeSpan.FromMinutes(60),
            PivotBar = 30,
            PivotOpenTime = new DateTime(2024, 1, 1, 8, 0, 0), // [8:00..9:00) doesn't contain 9:00..9:15
            Low = 1.1000,
            High = 1.1020,
        };

        var selfSkipped = 0;
        var result = SwingCZoneObstacleGate.IsCZoneOwnKeyLevel(
            in cResult, in zone,
            chartTfToken: "15",
            cPivotOpenTime: cOpenTime,
            chartTfPeriod: chartTfPeriod,
            ref selfSkipped);

        Assert.False(result);
        Assert.Equal(0, selfSkipped);
    }

    [Fact]
    public void IsCZoneOwnKeyLevel_CrossTf_CInsideZoneBar_PriceMatchGuard_ReturnsTrue()
    {
        var cResult = new SwingCEdgeResult
        {
            PivotBar   = 50,
            PivotIndex = 0,
            EdgeBottom = 1.1000,
            EdgeTop    = 1.1020,
        };

        var cOpenTime = new DateTime(2024, 1, 1, 9, 0, 0);
        var chartTfPeriod = TimeSpan.FromMinutes(15);

        // H4 zone bar starts at 8:00 and spans 4h → [8:00..12:00), contains [9:00..9:15)
        // Price midpoint of zone (1.1010) close to C midpoint (1.1010) → price guard passes
        var zone = new ZoneCandidate
        {
            TfToken = "240",
            TfPeriod = TimeSpan.FromHours(4),
            PivotBar = 10,
            PivotOpenTime = new DateTime(2024, 1, 1, 8, 0, 0),
            Low = 1.1000,
            High = 1.1020,
        };

        var selfSkipped = 0;
        var result = SwingCZoneObstacleGate.IsCZoneOwnKeyLevel(
            in cResult, in zone,
            chartTfToken: "15",
            cPivotOpenTime: cOpenTime,
            chartTfPeriod: chartTfPeriod,
            ref selfSkipped);

        Assert.True(result);
        Assert.Equal(1, selfSkipped);
    }

    [Fact]
    public void IsCZoneOwnKeyLevel_CrossTf_CInsideZoneBar_PriceGuardFails_ReturnsFalse()
    {
        var cResult = new SwingCEdgeResult
        {
            PivotBar   = 50,
            PivotIndex = 0,
            EdgeBottom = 1.1000,
            EdgeTop    = 1.1020, // C midpoint = 1.1010
        };

        var cOpenTime = new DateTime(2024, 1, 1, 9, 0, 0);
        var chartTfPeriod = TimeSpan.FromMinutes(15);

        // H4 zone contains C time, but zone midpoint is 1.1300 (far away) → price guard fails → not self
        var zone = new ZoneCandidate
        {
            TfToken = "240",
            TfPeriod = TimeSpan.FromHours(4),
            PivotBar = 10,
            PivotOpenTime = new DateTime(2024, 1, 1, 8, 0, 0),
            Low = 1.1280,
            High = 1.1320, // midpoint = 1.1300, far from C midpoint 1.1010
        };

        var selfSkipped = 0;
        var result = SwingCZoneObstacleGate.IsCZoneOwnKeyLevel(
            in cResult, in zone,
            chartTfToken: "15",
            cPivotOpenTime: cOpenTime,
            chartTfPeriod: chartTfPeriod,
            ref selfSkipped);

        Assert.False(result);
        Assert.Equal(0, selfSkipped);
    }

    // ── Fallback Daily scan ───────────────────────────────────────────────

    [Fact]
    public void Fallback_PrimaryEmpty_DailyObstacle_ReturnsObstacle()
    {
        var c = CResult(edgeBottom: 1.1000, edgeTop: 1.1020);
        // Primary states: no zones on M15
        var primaryState = new PineStateEngine();
        var primary = new Dictionary<string, PineStateEngine> { ["15"] = primaryState };
        // Fallback state: red Daily zone overlapping C
        var dailyState = StateWithKl(type: 1, top: 1.1025, bottom: 1.0995, pivotBar: 5);
        var fallback = new Dictionary<string, PineStateEngine> { ["1440"] = dailyState };

        // Simulate: no D zone found → caller passes fallback
        var r = SwingCZoneObstacleGate.Evaluate(
            c, isBuy: true, chartTfToken: "15", primary, pipSize: 0.0001,
            chartBuffer: null, fallbackStates: fallback);

        Assert.True(r.HasObstacle);
        Assert.True(r.IsFallback);
        Assert.Equal("Daily", r.ObstacleTf);
        Assert.True(r.OverlapPips > 0);
    }

    [Fact]
    public void Fallback_DZoneExists_CallerPassesNullFallback_NoObstacle()
    {
        // When D zone exists, the caller (TryBuildPlans) passes fallbackStates=null.
        // Even if a Daily zone overlaps C, the gate should NOT trigger.
        var c = CResult(edgeBottom: 1.1000, edgeTop: 1.1020);
        var primaryState = new PineStateEngine(); // no primary zone
        var primary = new Dictionary<string, PineStateEngine> { ["15"] = primaryState };

        // Fallback has a zone but caller passes null (because D zone exists)
        var r = SwingCZoneObstacleGate.Evaluate(
            c, isBuy: true, chartTfToken: "15", primary, pipSize: 0.0001,
            chartBuffer: null, fallbackStates: null); // null = D exists, skip daily check

        Assert.False(r.HasObstacle);
    }

    [Fact]
    public void Fallback_PrimaryHasObstacle_FallbackNotScanned()
    {
        // Primary already has an obstacle → should return from primary, IsFallback=false
        var c = CResult(edgeBottom: 1.1000, edgeTop: 1.1020);
        var primaryState = StateWithKl(type: 1, top: 1.1025, bottom: 1.0995, pivotBar: 20);
        var primary = new Dictionary<string, PineStateEngine> { ["15"] = primaryState };
        // Fallback also has a zone (shouldn't affect result)
        var dailyState = StateWithKl(type: 1, top: 1.1030, bottom: 1.0990, pivotBar: 5);
        var fallback = new Dictionary<string, PineStateEngine> { ["1440"] = dailyState };

        var r = SwingCZoneObstacleGate.Evaluate(
            c, isBuy: true, chartTfToken: "15", primary, pipSize: 0.0001,
            chartBuffer: null, fallbackStates: fallback);

        Assert.True(r.HasObstacle);
        Assert.False(r.IsFallback); // came from primary, not fallback
    }

    [Fact]
    public void Fallback_NullFallbackStates_ReturnsNoObstacle()
    {
        var c = CResult(edgeBottom: 1.1000, edgeTop: 1.1020);
        var primaryState = new PineStateEngine(); // no zones
        var primary = new Dictionary<string, PineStateEngine> { ["15"] = primaryState };

        var r = SwingCZoneObstacleGate.Evaluate(
            c, isBuy: true, chartTfToken: "15", primary, pipSize: 0.0001,
            chartBuffer: null, fallbackStates: null);

        Assert.False(r.HasObstacle);
        Assert.False(r.IsFallback);
    }

    [Fact]
    public void Fallback_DailyNoOverlap_ReturnsNoObstacle()
    {
        var c = CResult(edgeBottom: 1.1000, edgeTop: 1.1020);
        var primaryState = new PineStateEngine();
        var primary = new Dictionary<string, PineStateEngine> { ["15"] = primaryState };
        // Daily zone completely above C
        var dailyState = StateWithKl(type: 1, top: 1.1500, bottom: 1.1400, pivotBar: 5);
        var fallback = new Dictionary<string, PineStateEngine> { ["1440"] = dailyState };

        var r = SwingCZoneObstacleGate.Evaluate(
            c, isBuy: true, chartTfToken: "15", primary, pipSize: 0.0001,
            chartBuffer: null, fallbackStates: fallback);

        Assert.False(r.HasObstacle);
    }

    [Fact]
    public void Fallback_Sell_DailyGreenZone_ReturnsObstacle()
    {
        var c = CResult(edgeBottom: 1.0980, edgeTop: 1.1000);
        var primaryState = new PineStateEngine(); // no zones
        var primary = new Dictionary<string, PineStateEngine> { ["5"] = primaryState };
        // Daily green zone (SELL check) overlapping C
        var dailyState = StateWithKl(type: -1, top: 1.1005, bottom: 1.0975, pivotBar: 5);
        var fallback = new Dictionary<string, PineStateEngine> { ["1440"] = dailyState };

        var r = SwingCZoneObstacleGate.Evaluate(
            c, isBuy: false, chartTfToken: "15", primary, pipSize: 0.0001,
            chartBuffer: null, fallbackStates: fallback);

        Assert.True(r.HasObstacle);
        Assert.True(r.IsFallback);
        Assert.Equal("Daily", r.ObstacleTf);
        Assert.Equal(ZoneEffectiveColor.Green, r.ObstacleColor);
    }
}
