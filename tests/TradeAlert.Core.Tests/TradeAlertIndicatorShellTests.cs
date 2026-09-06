using TradeAlert.Core.Drawing;
using TradeAlert.Core.Models;
using TradeAlert.Indicator;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class TradeAlertIndicatorShellTests
{
    [Fact]
    public void OnStopOrReload_ClearsSeriesAlertsRenderAndResetsM15Default()
    {
        var ind = new TradeAlertIndicator(new IndicatorHostContext { Symbol = "X", ChartTimeframeToken = "5" });
        ind.M15Edge.SetHostM15CloseEdgeForCurrentEvaluation(true);
        ind.Series.InitialBackfill(0, i => (
            new BarSnapshot(
                DateTime.SpecifyKind(new DateTime(2026, 1, 1), DateTimeKind.Unspecified),
                1, 1, 1, 1),
            new BarRuntimeFlags(false, false, true, false, false, false)));
        ind.Render.Enqueue(new DrawingCommand { Kind = DrawingCommandKind.AddLabel, Text = "t" });

        ind.OnStopOrReload();
        Assert.False(ind.M15Edge.M15CloseEdgeInjected);
        Assert.False(ind.Series.BackfillCompleted);
        Assert.Empty(ind.Render.Commands);
        Assert.Empty(ind.Alerts.LogEntries);
    }
}
