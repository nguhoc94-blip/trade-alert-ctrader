using System;
using System.Collections.Generic;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Engines.AlertEvaluators;
using TradeAlert.Core.Models.Alerts;
using Xunit;

namespace TradeAlert.Core.Tests.Alerts;

public sealed class EventAlertTrackingDebugTests
{
    [Fact]
    public void RunRawDetection_emits_FIRE_label_for_M15_Event_A()
    {
        var dedup = new EventDedupSets();
        var pivots = new List<PivotEntry>
        {
            new()
            {
                FlagPrev = 1,
                FlagNow = 2,
                Type = 1,
                SwingIdStr = "H5",
                HasKeyBox = true,
                KeyExtending = true,
                KeyExtendingPrev = true,
            },
        };

        var ctx = BuildCtx(pivots, dedup, barIndex: 10, tf: "15", m15JustClosed: true);
        var labels = new List<EventAlertTrackingLabel>();
        var counts = EventFamilyEvaluators.RunRawDetection(in ctx, isM15: true, labels);

        Assert.Equal(1, counts.BuyHL);
        Assert.Contains(labels, l => l.Text.Contains("EvA RAW M15 BUY") && l.Text.Contains("FIRE") && l.Fired);
        Assert.Contains(labels, l => l.Kind == EventAlertTrackingKind.CondSummary && l.Text.Contains("condBuyEventHLM15"));
        Assert.Equal(ctx.SourceBarHigh, labels[0].Price);
    }

    [Fact]
    public void RunRawDetection_SKIP_when_swing_already_triggered()
    {
        var dedup = new EventDedupSets();
        dedup.TriggeredA_Buy_M15.Add("H5_2");

        var pivots = new List<PivotEntry>
        {
            new()
            {
                FlagPrev = 1,
                FlagNow = 2,
                Type = 1,
                SwingIdStr = "H5",
            },
        };

        var ctx = BuildCtx(pivots, dedup, barIndex: 10, tf: "15", m15JustClosed: true);
        var labels = new List<EventAlertTrackingLabel>();
        var counts = EventFamilyEvaluators.RunRawDetection(in ctx, isM15: true, labels);

        Assert.Equal(0, counts.BuyHL);
        Assert.Contains(labels, l => l.Text.Contains("SKIP") && !l.Fired);
        Assert.DoesNotContain(labels, l => l.Kind == EventAlertTrackingKind.CondSummary);
    }

    [Fact]
    public void Session_cache_runs_detection_once_per_bar()
    {
        var dedup = new EventDedupSets();
        var pivots = new List<PivotEntry>
        {
            new() { FlagPrev = 1, FlagNow = 2, Type = -1, SwingIdStr = "L3" },
        };

        var ctx = BuildCtx(pivots, dedup, barIndex: 7, tf: "15", m15JustClosed: true);
        var cache = new EventDetectionSessionCache();

        var first = EventFamilyEvaluators.ResolveRawCounts(in ctx, true, alertTrackingDebug: false, cache);
        var second = EventFamilyEvaluators.ResolveRawCounts(in ctx, true, alertTrackingDebug: false, cache);

        Assert.Equal(1, first.SellHL);
        Assert.Equal(first.SellHL, second.SellHL);
    }

    [Fact]
    public void RunRawDetection_emits_EventB_NG_M15_and_cond_summary()
    {
        var dedup = new EventDedupSets();
        var pivots = new List<PivotEntry>
        {
            new()
            {
                FlagPrev = 1,
                FlagNow = -1,
                Type = 1,
                SwingIdStr = "H3",
            },
        };

        var ctx = BuildCtx(pivots, dedup, barIndex: 20, tf: "15", m15JustClosed: true, pineBarIndex: 20);
        var labels = new List<EventAlertTrackingLabel>();
        var counts = EventFamilyEvaluators.RunRawDetection(in ctx, isM15: true, labels);

        Assert.Equal(1, counts.BuyNG);
        Assert.Contains(labels, l => l.Text.Contains("EvB RAW M15 BUY") && l.Fired);
        Assert.Contains(labels, l => l.Text.Contains("condBuyEventNGM15"));
        Assert.Equal(ctx.SourceBarLow, labels.Find(l => l.Text.Contains("condBuyEventNGM15")).Price);
    }

    [Fact]
    public void RunRawDetection_emits_EventC_NG_M15_when_keyStop_on_prior_bar()
    {
        var dedup = new EventDedupSets();
        var pivots = new List<PivotEntry>
        {
            new()
            {
                FlagPrev = 2,
                FlagNow = 2,
                Type = -1,
                SwingIdStr = "L7",
                HasKeyBox = true,
                KeyStopBar = 19,
                KeyExtendingPrev = true,
                KeyExtending = false,
            },
        };

        var ctx = BuildCtx(pivots, dedup, barIndex: 20, tf: "15", m15JustClosed: true, pineBarIndex: 20);
        var labels = new List<EventAlertTrackingLabel>();
        var counts = EventFamilyEvaluators.RunRawDetection(in ctx, isM15: true, labels);

        Assert.Equal(1, counts.BuyNG);
        Assert.Contains(labels, l => l.Text.Contains("EvC RAW M15 BUY") && l.Fired);
        Assert.Contains(labels, l => l.Text.Contains("condBuyEventNGM15"));
    }

    [Fact]
    public void RunRawDetection_EventC_skips_when_extPrev_not_held()
    {
        var dedup = new EventDedupSets();
        var pivots = new List<PivotEntry>
        {
            new()
            {
                FlagPrev = 2,
                FlagNow = 2,
                Type = -1,
                SwingIdStr = "L7",
                HasKeyBox = true,
                KeyStopBar = 19,
                KeyExtendingPrev = false,
                KeyExtending = false,
            },
        };

        var ctx = BuildCtx(pivots, dedup, barIndex: 20, tf: "15", m15JustClosed: true, pineBarIndex: 20);
        var counts = EventFamilyEvaluators.RunRawDetection(in ctx, isM15: true, trackingSink: null);

        Assert.Equal(0, counts.BuyNG);
    }

    [Fact]
    public void Session_cache_keeps_m5_and_m15_entries_separate()
    {
        var dedup = new EventDedupSets();
        var pivotsM15 = new List<PivotEntry>
        {
            new() { FlagPrev = 1, FlagNow = -1, Type = 1, SwingIdStr = "H1" },
        };
        var pivotsM5 = new List<PivotEntry>
        {
            new() { FlagPrev = 1, FlagNow = 2, Type = 1, SwingIdStr = "H9" },
        };

        var ctxM15 = BuildCtx(pivotsM15, dedup, barIndex: 3, tf: "15", m15JustClosed: true);
        var ctxM5 = BuildCtx(pivotsM5, dedup, barIndex: 3, tf: "5", m15JustClosed: false);
        ctxM5 = ctxM5 with { IsM5JustClosed = true };

        var cache = new EventDetectionSessionCache();
        EventFamilyEvaluators.ResolveRawCounts(in ctxM5, false, alertTrackingDebug: true, cache);
        EventFamilyEvaluators.ResolveRawCounts(in ctxM15, true, alertTrackingDebug: true, cache);

        Assert.True(cache.HasTrackingLabels(3, "5", isM15: false));
        Assert.True(cache.HasTrackingLabels(3, "15", isM15: true));
    }

    [Fact]
    public void PineStateEngine_holds_keyExtPrev_on_stop_bar_for_next_EventC()
    {
        var engine = new PineStateEngine();
        engine.Pivots.PushPivot(1.10, 18, 1, 0, 1, 0);
        engine.Pivots.SetFlag(0, 2);
        engine.Pivots.SetKeyExtending(0, true);
        engine.Pivots.SetHasKey(0, true);
        engine.Pivots.SetKeyVisible(0, true);
        engine.UpdateKeyExtPrevAfterAlertEval(18);

        engine.Pivots.SetKeyStopBar(0, 19);
        engine.Pivots.SetKeyExtending(0, false);
        engine.UpdateKeyExtPrevAfterAlertEval(19);

        var entries = engine.BuildPivotEntries(20);
        Assert.True(entries[0].KeyExtendingPrev);
        Assert.False(entries[0].KeyExtending);
        Assert.Equal(19, entries[0].KeyStopBar);
    }

    [Fact]
    public void ResolveRawCounts_rebuilds_labels_when_cache_had_counts_only()
    {
        var dedup = new EventDedupSets();
        var pivots = new List<PivotEntry>
        {
            new() { FlagPrev = 0, FlagNow = -1, Type = -1, SwingIdStr = "L2" },
        };

        var ctx = BuildCtx(pivots, dedup, barIndex: 5, tf: "15", m15JustClosed: true, pineBarIndex: 5);
        var cache = new EventDetectionSessionCache();

        EventFamilyEvaluators.ResolveRawCounts(in ctx, true, alertTrackingDebug: false, cache);
        EventFamilyEvaluators.ResolveRawCounts(in ctx, true, alertTrackingDebug: true, cache);

        Assert.True(cache.HasTrackingLabels(5, "15", isM15: true));
        Assert.Contains(cache.TrackingLabels, l => l.Text.Contains("EvB RAW M15 SELL"));
    }

    static AlertEvaluationContext BuildCtx(
        List<PivotEntry> pivots,
        EventDedupSets dedup,
        int barIndex,
        string tf,
        bool m15JustClosed,
        int? pineBarIndex = null)
    {
        return new AlertEvaluationContext
        {
            Symbol = "EURUSD",
            ChartTimeframeToken = tf,
            SourceBarIndex = barIndex,
            PineBarIndex = pineBarIndex ?? barIndex,
            SourceBarOpenTimeChartLocal = new DateTime(2024, 1, 1, 10, 0, 0),
            SourceBarHigh = 1.1050,
            SourceBarLow = 1.1000,
            EventRawDetectionAvailable = true,
            PivotEntries = pivots,
            EventDedup = dedup,
            IsM15JustClosed = m15JustClosed,
            IsBarClosed = true,
            EvaluationOffset = 1,
        };
    }
}
