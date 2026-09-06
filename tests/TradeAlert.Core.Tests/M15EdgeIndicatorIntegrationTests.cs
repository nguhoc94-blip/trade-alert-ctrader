using TradeAlert.Core.Engines.AlertEvaluators;
using TradeAlert.Core.Models.Alerts;
using TradeAlert.Indicator;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class M15EdgeIndicatorIntegrationTests
{
    static DateTime Tu() => DateTime.SpecifyKind(new DateTime(2026, 5, 10, 14, 0, 0), DateTimeKind.Unspecified);

    [Fact]
    public void M15CloseAlert_WithoutHostEdge_ReasonCarry()
    {
        var ind = new TradeAlertIndicator(new IndicatorHostContext { Symbol = "XAUUSD", ChartTimeframeToken = "5" });
        ind.M15Edge.SetHostM15CloseEdgeForCurrentEvaluation(false);
        var ctx = new AlertEvaluationContext
        {
            Symbol = "XAUUSD",
            ChartTimeframeToken = "5",
            SourceBarIndex = 10,
            SourceBarOpenTimeChartLocal = Tu(),
            IsCurrentBar = true,
            IsBarClosed = false,
            IsRealtime = true,
            M15CloseEdgeInjected = ind.M15Edge.M15CloseEdgeInjected,
            RealtimeFilterStateAvailable = true,
        };
        var def = ind.Alerts.Engine.Registry[AlertConditionId.CanBuyRealAndM15CloseNow];
        var dup = new DuplicateKey(
            ctx.Symbol, ctx.ChartTimeframeToken, def.Title, def.PineConditionSymbol,
            ctx.SourceBarOpenTimeChartLocal, ctx.SourceBarIndex, ctx.ChartTimeframeToken);

        ind.Alerts.EvaluateRecordAndMaybeFire(AlertConditionId.CanBuyRealAndM15CloseNow, in ctx, Tu(), in dup);
        Assert.Equal(AlertReasonCodes.CarryOverMissingM15Edge, ind.Alerts.LogEntries[^1].ReasonCode);
    }

    [Fact]
    public void M15CloseAlert_WithHostEdge_StillGuardedIfFilterMissing()
    {
        var ind = new TradeAlertIndicator(new IndicatorHostContext());
        ind.M15Edge.SetHostM15CloseEdgeForCurrentEvaluation(true);
        var ctx = new AlertEvaluationContext
        {
            Symbol = "XAUUSD",
            ChartTimeframeToken = "5",
            SourceBarIndex = 10,
            SourceBarOpenTimeChartLocal = Tu(),
            IsCurrentBar = true,
            IsBarClosed = false,
            IsRealtime = true,
            M15CloseEdgeInjected = ind.M15Edge.M15CloseEdgeInjected,
            RealtimeFilterStateAvailable = false,
        };
        var def = ind.Alerts.Engine.Registry[AlertConditionId.CanBuyRealAndM15CloseNow];
        var dup = new DuplicateKey(
            ctx.Symbol, ctx.ChartTimeframeToken, def.Title, def.PineConditionSymbol,
            ctx.SourceBarOpenTimeChartLocal, ctx.SourceBarIndex, ctx.ChartTimeframeToken);
        ind.Alerts.EvaluateRecordAndMaybeFire(AlertConditionId.CanBuyRealAndM15CloseNow, in ctx, Tu(), in dup);
        Assert.Equal(AlertReasonCodes.DepRealtimeFilterUnavailable, ind.Alerts.LogEntries[^1].ReasonCode);
    }
}
