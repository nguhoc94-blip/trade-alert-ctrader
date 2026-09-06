using TradeAlert.BacktestRobot.Execution;
using TradeAlert.Core.Models.Alerts;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests;

public sealed class CompoundFireChartDebugLabelsMetricsTests
{
    [Fact]
    public void FormatPlanMetricsBlock_split_legs_shows_prices_and_rr()
    {
        var plans = new[]
        {
            new TradePlan
            {
                TpLegTag = "C",
                EntryLimit = 1.08450,
                StopLoss = 1.08330,
                TakeProfit = 1.08620,
                StopLossPips = 12,
                TakeProfitPips = 18,
            },
            new TradePlan
            {
                TpLegTag = "D",
                EntryLimit = 1.08450,
                StopLoss = 1.08330,
                TakeProfit = 1.08750,
                StopLossPips = 12,
                TakeProfitPips = 30,
            },
        };

        var block = CompoundFireChartDebugLabels.FormatPlanMetricsBlock(plans);

        Assert.Equal(
            "entry=1.0845 sl=1.0833 tpC=1.0862 tpD=1.0875\nSL=12pip RRc=1.5 RRd=2.5",
            block);
    }

    [Fact]
    public void FormatSwingCCandidateLabel_marks_would_pick()
    {
        var ev = new CompoundFireEvent { SlotIndex = 3, ChartBarIndex = 350, EventBarIndex = 3500, Direction = SignalDirection.Sell };
        var row = new SwingCPivotAudit.Row
        {
            Bar = 355,
            FlagLabel = "D_SWING",
            ValidCCandidate = true,
        };

        var text = CompoundFireChartDebugLabels.FormatSwingCCandidateLabel(in ev, row, watchBar: 358, isWouldPick: true);

        Assert.Contains("R4 C? bar=355", text);
        Assert.Contains("validC=Y", text);
        Assert.Contains("watch@358", text);
        Assert.Contains("<<wouldPick", text);
    }

    [Fact]
    public void FormatSwingCPickedLabel_includes_picked_row()
    {
        var ev = new CompoundFireEvent { SlotIndex = 3, ChartBarIndex = 350, EventBarIndex = 3500, Direction = SignalDirection.Sell };
        var row = new SwingCPivotAudit.Row
        {
            Bar = 355,
            FlagLabel = "D_SWING",
            Price = 1.08432,
            ValidCCandidate = true,
        };

        var text = CompoundFireChartDebugLabels.FormatSwingCPickedLabel(in ev, 350, 355, 9, row);

        Assert.Contains("C✓ PICKED", text);
        Assert.Contains("C@355 held=9bar", text);
        Assert.Contains("D_SWING", text);
        Assert.Contains("price=1.08432", text);
    }
}
