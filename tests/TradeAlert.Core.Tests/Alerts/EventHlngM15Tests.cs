using System;
using System.Collections.Generic;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Engines.AlertEvaluators;
using TradeAlert.Core.Models.Alerts;
using Xunit;

namespace TradeAlert.Core.Tests.Alerts;

public sealed class EventHlngM15Tests
{
    [Fact]
    public void BuyHLNG_fires_on_NG_only_without_opposite_KL()
    {
        var ctx = BuildCtx(
            eventPivot: EventB_BuyNg(),
            oppositeKl: null,
            m15JustClosed: true);

        var result = EventFamilyEvaluators.EvaluateById(AlertConditionId.CondBuyEventHLNGM15, in ctx);

        Assert.True(result.Fired);
        Assert.Contains("buyNG=1", result.ReasonText);
    }

    [Fact]
    public void BuyHLNG_HL_without_opposite_KL_does_not_fire()
    {
        var ctx = BuildCtx(
            eventPivot: EventA_Buy(),
            oppositeKl: null,
            m15JustClosed: true);

        var hlOnly = EventFamilyEvaluators.EvaluateById(AlertConditionId.CondBuyEventHLM15, in ctx);
        var hlng = EventFamilyEvaluators.EvaluateById(AlertConditionId.CondBuyEventHLNGM15, in ctx);

        Assert.True(hlOnly.Fired);
        Assert.False(hlng.Fired);
    }

    [Fact]
    public void BuyHLNG_HL_fires_when_opposite_RED_KL_exists()
    {
        var ctx = BuildCtx(
            eventPivot: EventA_Buy(),
            oppositeKl: OppositeRedKl(),
            m15JustClosed: true);

        var result = EventFamilyEvaluators.EvaluateById(AlertConditionId.CondBuyEventHLNGM15, in ctx);

        Assert.True(result.Fired);
        Assert.Contains("hlGate=True", result.ReasonText);
        Assert.Contains("oppositeKl=True", result.ReasonText);
    }

    [Fact]
    public void BuyHLNG_MAIN_C_opposite_counts_as_KL()
    {
        var ctx = BuildCtx(
            eventPivot: EventA_Buy(),
            oppositeKl: MainC_RedKl(),
            m15JustClosed: true);

        var result = EventFamilyEvaluators.EvaluateById(AlertConditionId.CondBuyEventHLNGM15, in ctx);

        Assert.True(result.Fired);
        Assert.Contains("oppositeKl=True", result.ReasonText);
    }

    [Fact]
    public void BuyHLNG_broken_KL_does_not_gate_HL()
    {
        var ctx = BuildCtx(
            eventPivot: EventA_Buy(),
            oppositeKl: BrokenRedKl(),
            m15JustClosed: true);

        var result = EventFamilyEvaluators.EvaluateById(AlertConditionId.CondBuyEventHLNGM15, in ctx);

        Assert.False(result.Fired);
    }

    [Fact]
    public void SellHLNG_HL_fires_when_opposite_GREEN_KL_exists()
    {
        var ctx = BuildCtx(
            eventPivot: EventA_Sell(),
            oppositeKl: OppositeGreenKl(),
            m15JustClosed: true);

        var result = EventFamilyEvaluators.EvaluateById(AlertConditionId.CondSellEventHLNGM15, in ctx);

        Assert.True(result.Fired);
        Assert.Contains("hlGate=True", result.ReasonText);
        Assert.Contains("oppositeKl=True", result.ReasonText);
    }

    [Fact]
    public void BuyHLNG_requires_M15_bar_close()
    {
        var ctx = BuildCtx(
            eventPivot: EventA_Buy(),
            oppositeKl: OppositeRedKl(),
            m15JustClosed: false);

        var result = EventFamilyEvaluators.EvaluateById(AlertConditionId.CondBuyEventHLNGM15, in ctx);

        Assert.False(result.Fired);
    }

    static PivotEntry EventB_BuyNg() => new()
    {
        FlagPrev = 1,
        FlagNow = -1,
        Type = 1,
        SwingIdStr = "H3",
    };

    static PivotEntry EventA_Buy() => new()
    {
        FlagPrev = 1,
        FlagNow = 2,
        Type = 1,
        SwingIdStr = "H5",
        HasKeyBox = true,
        KeyExtending = true,
        KeyExtendingPrev = true,
    };

    static PivotEntry EventA_Sell() => new()
    {
        FlagPrev = 1,
        FlagNow = 2,
        Type = -1,
        SwingIdStr = "L5",
        HasKeyBox = true,
        KeyExtending = true,
        KeyExtendingPrev = true,
    };

    static PivotEntry OppositeRedKl() => new()
    {
        FlagNow = 1,
        Type = 1,
        SwingIdStr = "H9",
        HasKeyBox = true,
        KeyExtending = true,
    };

    static PivotEntry OppositeGreenKl() => new()
    {
        FlagNow = 1,
        Type = -1,
        SwingIdStr = "L9",
        HasKeyBox = true,
        KeyExtending = true,
    };

    static PivotEntry MainC_RedKl() => new()
    {
        FlagNow = 0,
        MainRole = 1,
        Type = 1,
        SwingIdStr = "H8",
        HasKeyBox = true,
        KeyExtending = true,
    };

    static PivotEntry BrokenRedKl() => new()
    {
        FlagNow = 2,
        Type = 1,
        SwingIdStr = "H7",
        HasKeyBox = true,
        KeyExtending = true,
    };

    static AlertEvaluationContext BuildCtx(
        PivotEntry eventPivot,
        PivotEntry? oppositeKl,
        bool m15JustClosed)
    {
        var pivots = new List<PivotEntry> { eventPivot };
        if (oppositeKl != null)
            pivots.Add(oppositeKl);

        return new AlertEvaluationContext
        {
            Symbol = "EURUSD",
            ChartTimeframeToken = "15",
            SourceBarIndex = 10,
            PineBarIndex = 10,
            SourceBarOpenTimeChartLocal = new DateTime(2024, 1, 1, 10, 0, 0),
            SourceBarHigh = 1.1050,
            SourceBarLow = 1.1000,
            EventRawDetectionAvailable = true,
            PivotEntries = pivots,
            EventDedup = new EventDedupSets(),
            IsM15JustClosed = m15JustClosed,
            IsBarClosed = true,
            EvaluationOffset = 1,
        };
    }
}
