using TradeAlert.Core.Engines;
using TradeAlert.Core.Engines.AlertEvaluators;
using TradeAlert.Core.Models.Alerts;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class AlertGuardedDependencyTests
{
    static DateTime T() => DateTime.SpecifyKind(new DateTime(2026, 5, 10), DateTimeKind.Unspecified);

    [Fact]
    public void Event_WithoutDetector_NotFired_WithReason()
    {
        var eng = new AlertEngine();
        var ctx = BaseCtx();
        var r = eng.Evaluate(AlertConditionId.CondBuyEventM5, in ctx);
        Assert.False(r.Fired);
        Assert.True(r.StateDependencyMissing);
        Assert.Equal(AlertReasonCodes.DepEventDetectorUnavailable, r.ReasonCode);
    }

    [Fact]
    public void Touch_WithoutZones_NotFired()
    {
        var eng = new AlertEngine();
        var ctx = BaseCtx();
        var r = eng.Evaluate(AlertConditionId.CanBuyTouchM5, in ctx);
        Assert.False(r.Fired);
        Assert.Equal(AlertReasonCodes.DepZoneCollectionUnavailable, r.ReasonCode);
    }

    [Fact]
    public void Real_WithoutFilterState_NotFired()
    {
        var eng = new AlertEngine();
        var ctx = BaseCtx();
        var r = eng.Evaluate(AlertConditionId.CanBuyReal, in ctx);
        Assert.False(r.Fired);
        Assert.Equal(AlertReasonCodes.DepRealtimeFilterUnavailable, r.ReasonCode);
    }

    [Fact]
    public void RealM15_WithoutEdge_NotFired()
    {
        var eng = new AlertEngine();
        var ctx = BaseCtx();
        var r = eng.Evaluate(AlertConditionId.CanBuyRealAndM15CloseNow, in ctx);
        Assert.False(r.Fired);
        Assert.Equal(AlertReasonCodes.CarryOverMissingM15Edge, r.ReasonCode);
    }

    [Fact]
    public void RealM15_WithEdge_StillSkeleton_WithoutFilterState()
    {
        var eng = new AlertEngine();
        var ctx = new AlertEvaluationContext
        {
            Symbol = "XAUUSD",
            ChartTimeframeToken = "5",
            SourceBarIndex = 1,
            SourceBarOpenTimeChartLocal = T(),
            IsCurrentBar = true,
            IsBarClosed = false,
            IsRealtime = true,
            EventRawDetectionAvailable = false,
            ActiveZoneCollectionAvailable = false,
            RealtimeFilterStateAvailable = false,
            M15CloseEdgeInjected = true,
        };
        var r = eng.Evaluate(AlertConditionId.CanSellRealAndM15CloseNow, in ctx);
        Assert.False(r.Fired);
        Assert.Equal(AlertReasonCodes.DepRealtimeFilterUnavailable, r.ReasonCode);
    }

    static AlertEvaluationContext BaseCtx() => new()
    {
        Symbol = "XAUUSD",
        ChartTimeframeToken = "5",
        SourceBarIndex = 1,
        SourceBarOpenTimeChartLocal = T(),
        IsCurrentBar = true,
        IsBarClosed = false,
        IsRealtime = true,
        EventRawDetectionAvailable = false,
        ActiveZoneCollectionAvailable = false,
        RealtimeFilterStateAvailable = false,
        M15CloseEdgeInjected = false,
    };
}
