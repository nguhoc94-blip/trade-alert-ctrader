using TradeAlert.Core.Engines.AlertEvaluators;
using TradeAlert.Core.Models.Alerts;
using TradeAlert.Indicator;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class AlertPipelineHostWiringTests
{
    static DateTime T() => DateTime.SpecifyKind(new DateTime(2026, 5, 10, 12, 0, 0), DateTimeKind.Unspecified);

    [Fact]
    public void GuardedEvaluate_Log_HasReasonCode()
    {
        var sink = new ListAlertFireSink();
        var host = new AlertPipelineHost(fireSink: sink);
        var ctx = new AlertEvaluationContext
        {
            Symbol = "XAUUSD",
            ChartTimeframeToken = "5",
            SourceBarIndex = 5,
            SourceBarOpenTimeChartLocal = T(),
            IsCurrentBar = true,
            IsBarClosed = false,
            IsRealtime = true,
            EventRawDetectionAvailable = false,
        };
        var def = host.Engine.Registry[AlertConditionId.CondBuyEventM5];
        var dup = new DuplicateKey(
            ctx.Symbol, ctx.ChartTimeframeToken, def.Title, def.PineConditionSymbol,
            ctx.SourceBarOpenTimeChartLocal, ctx.SourceBarIndex, ctx.ChartTimeframeToken);

        host.EvaluateRecordAndMaybeFire(AlertConditionId.CondBuyEventM5, in ctx, T(), in dup);
        Assert.Single(host.LogEntries);
        Assert.Equal(AlertReasonCodes.DepEventDetectorUnavailable, host.LogEntries[0].ReasonCode);
        Assert.Empty(sink.Events);
    }

    [Fact]
    public void AppendLogFalse_DoesNotGrowLogEntries()
    {
        var sink = new ListAlertFireSink();
        var host = new AlertPipelineHost(fireSink: sink);
        var ctx = new AlertEvaluationContext
        {
            Symbol = "XAUUSD",
            ChartTimeframeToken = "5",
            SourceBarIndex = 5,
            SourceBarOpenTimeChartLocal = T(),
            IsCurrentBar = true,
            IsBarClosed = false,
            IsRealtime = true,
            EventRawDetectionAvailable = false,
        };
        var def = host.Engine.Registry[AlertConditionId.CondBuyEventM5];
        var dup = new DuplicateKey(
            ctx.Symbol, ctx.ChartTimeframeToken, def.Title, def.PineConditionSymbol,
            ctx.SourceBarOpenTimeChartLocal, ctx.SourceBarIndex, ctx.ChartTimeframeToken);

        host.EvaluateRecordAndMaybeFire(AlertConditionId.CondBuyEventM5, in ctx, T(), in dup, appendLog: false);
        Assert.Empty(host.LogEntries);
        Assert.Empty(sink.Events);
    }

    [Fact]
    public void MaxDebugLogEntries_TrimsOldestOnly()
    {
        var sink = new ListAlertFireSink();
        var host = new AlertPipelineHost(fireSink: sink);
        host.MaxDebugLogEntries = 3;

        for (var bi = 0; bi < 10; bi++)
        {
            var ctx = new AlertEvaluationContext
            {
                Symbol = "X",
                ChartTimeframeToken = "5",
                SourceBarIndex = bi,
                SourceBarOpenTimeChartLocal = T().AddMinutes(bi),
                IsCurrentBar = false,
                IsBarClosed = true,
                IsRealtime = false,
                EventRawDetectionAvailable = false,
            };
            var def = host.Engine.Registry[AlertConditionId.CondBuyEventM5];
            var dup = new DuplicateKey("X", "5", def.Title, def.PineConditionSymbol,
                ctx.SourceBarOpenTimeChartLocal, bi, "5");
            host.EvaluateRecordAndMaybeFire(AlertConditionId.CondBuyEventM5, in ctx, T(), in dup, appendLog: true);
        }

        Assert.Equal(host.MaxDebugLogEntries, host.LogEntries.Count);
    }

    [Fact]
    public void ResetSession_ClearsLogsAndDuplicateStore()
    {
        var host = new AlertPipelineHost();
        var ctx = new AlertEvaluationContext
        {
            Symbol = "X",
            ChartTimeframeToken = "5",
            SourceBarIndex = 1,
            SourceBarOpenTimeChartLocal = T(),
            IsCurrentBar = false,
            IsBarClosed = true,
            IsRealtime = false,
            EventRawDetectionAvailable = false,
        };
        var def = host.Engine.Registry[AlertConditionId.CondBuyEventM5];
        var dup = new DuplicateKey("X", "5", def.Title, def.PineConditionSymbol, T(), 1, "5");
        host.EvaluateRecordAndMaybeFire(AlertConditionId.CondBuyEventM5, in ctx, T(), in dup);
        Assert.NotEmpty(host.LogEntries);
        host.ResetSession();
        Assert.Empty(host.LogEntries);
    }
}
