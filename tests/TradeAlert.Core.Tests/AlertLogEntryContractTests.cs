using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.Alerts;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class AlertLogEntryContractTests
{
    [Fact]
    public void ToLogEntry_HasAllRequiredFields_NonEmpty()
    {
        var eng = new AlertEngine();
        var def = eng.Registry[AlertConditionId.CondBuyEventM5];
        var ctx = new AlertEvaluationContext
        {
            Symbol = "XAUUSD",
            ChartTimeframeToken = "5",
            SourceBarIndex = 10,
            SourceBarOpenTimeChartLocal =
                DateTime.SpecifyKind(new DateTime(2026, 5, 10), DateTimeKind.Unspecified),
            IsCurrentBar = false,
            IsBarClosed = true,
            IsRealtime = false,
            EvaluationOffset = 0,
            EventRawDetectionAvailable = false,
        };
        var res = eng.Evaluate(AlertConditionId.CondBuyEventM5, in ctx);
        var dup = new DuplicateKey(
            ctx.Symbol, ctx.ChartTimeframeToken, def.Title, def.PineConditionSymbol,
            ctx.SourceBarOpenTimeChartLocal, ctx.SourceBarIndex, ctx.ChartTimeframeToken);
        var fire = DateTime.SpecifyKind(new DateTime(2026, 5, 10, 12, 1, 1), DateTimeKind.Unspecified);
        var log = AlertEngine.ToLogEntry(def, in ctx, in res, in dup, fire);

        Assert.NotEqual(default, log.FireTimestampChartLocal);
        Assert.False(string.IsNullOrEmpty(log.Symbol));
        Assert.False(string.IsNullOrEmpty(log.Timeframe));
        Assert.False(string.IsNullOrEmpty(log.AlertName));
        Assert.False(string.IsNullOrEmpty(log.AlertType));
        Assert.False(string.IsNullOrEmpty(log.ConditionId));
        Assert.NotEqual(default, log.SourceBarTimeChartLocal);
        Assert.False(string.IsNullOrEmpty(log.ReasonCode));
        Assert.False(string.IsNullOrEmpty(log.ReasonText));
        Assert.False(string.IsNullOrEmpty(log.DuplicateKeyCanonical));
        Assert.False(string.IsNullOrEmpty(log.PineConditionSymbol));
        Assert.False(string.IsNullOrEmpty(log.CSharpTarget));
    }
}
