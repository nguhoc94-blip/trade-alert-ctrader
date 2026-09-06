using System;
using System.Collections.Generic;
using System.Linq;
using TradeAlert.BacktestRobot.Execution;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests;

public class InternalStrategyMetricsTests
{
    [Theory]
    [InlineData("L6BT|R3|B7080", "R3")]
    [InlineData("L6BT|R1|B100", "R1")]
    [InlineData("OTHER", "Unknown")]
    public void LabelRuleParser_ParsesRuleToken(string label, string expected) =>
        Assert.Equal(expected, LabelRuleParser.ParseRule(label));

    [Fact]
    public void FinalizeEquityCurve_TracksDrawdownInChronologicalOrder()
    {
        var engine = new InternalStrategyMetricsEngine();
        engine.SetContext(new InternalStrategyEvalContext { StartEquityUsd = 10_000 });

        var t0 = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        engine.RecordClose("A", t0.AddHours(1), true, "TakeProfit",
            new InternalCloseScore { InternalNetUsd = 100, InternalNetR = 0.1, InternalGrossR = 0.1 }, 120);
        engine.RecordClose("B", t0, true, "StopLoss",
            new InternalCloseScore { InternalNetUsd = -50, InternalNetR = -0.05, InternalGrossR = -0.05 }, -55);

        var records = engine.FinalizeEquityCurve().ToList();

        Assert.Equal(2, records.Count);
        Assert.Equal("B", records[0].Label);
        Assert.Equal(9950, records[0].InternalEquityUsd, 6);
        Assert.Equal("A", records[1].Label);
        Assert.Equal(10050, records[1].InternalEquityUsd, 6);
        Assert.True(records[0].InternalDrawdownUsd > 0);
    }

    [Fact]
    public void PrintStrategyEvaluation_EmitsCsvSummaryV2AndStrategyScore()
    {
        var engine = new InternalStrategyMetricsEngine();
        engine.SetContext(new InternalStrategyEvalContext
        {
            StartEquityUsd = 10_000,
            RewardRisk = 2,
            ProtectionAnchorMode = ProtectionAnchorMode.ActualFillRelative,
            UseSwingCEdgeTakeProfit = true,
        });

        var t = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        engine.RecordClose("L6BT|R2|B1", t, true, "TakeProfit",
            new InternalCloseScore { InternalNetUsd = 200, InternalNetR = 0.2, InternalGrossR = 0.2 }, 250);
        engine.RecordClose("L6BT|R2|B2", t.AddHours(1), true, "StopLoss",
            new InternalCloseScore { InternalNetUsd = -100, InternalNetR = -0.1, InternalGrossR = -0.1 }, -120);

        var logged = new List<string>();
        engine.PrintStrategyEvaluation(logged.Add, new InternalStrategyAuditSnapshot
        {
            HistoryClosedCount = 2,
            CloseAuditCount = 2,
            ReasonCounts = new Dictionary<string, int>
            {
                ["TakeProfit"] = 1,
                ["StopLoss"] = 1,
            },
        });

        Assert.Contains(logged, l => l.StartsWith("[L6BT] INTERNAL STRATEGY EVALUATION"));
        Assert.Contains(logged, l => l.StartsWith("[L6BT_CSV_SUMMARY_V2],"));
        Assert.Contains(logged, l => l.StartsWith("[L6BT_STRATEGY_SCORE]"));
        Assert.Contains(logged, l => l.StartsWith("[L6BT_CSV_EQUITY],"));
        Assert.Contains(logged, l => l.StartsWith("[L6BT_CSV_RULE],"));
        Assert.Contains(logged, l => l.Contains("REASON METRICS reason=normalTakeProfit"));
    }

    [Fact]
    public void PrintOptResultLine_EmitsCompactRankingLine()
    {
        var engine = new InternalStrategyMetricsEngine();
        engine.SetContext(new InternalStrategyEvalContext
        {
            StartEquityUsd = 10_000,
            RewardRisk = 2,
            UseSwingCEdgeTakeProfit = true,
        });

        var t = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        engine.RecordClose("L6BT|R1|B1", t, true, "TakeProfit",
            new InternalCloseScore { InternalNetUsd = 200, InternalNetR = 0.2, InternalGrossR = 0.2 }, 250);

        var logged = new List<string>();
        engine.PrintOptResultLine(logged.Add, new InternalStrategyAuditSnapshot
        {
            CloseAuditCount = 1,
            EnableVirtualXrBarCloseExit = true,
            VirtualXrTriggerR = 1.25,
            VirtualXrFillMode = "SignalBarClose",
        });

        var line = Assert.Single(logged);
        Assert.StartsWith("[L6BT_OPT_RESULT],", line);
        Assert.Contains("rewardRisk=2", line);
        Assert.Contains("enableVirtualXrBarCloseExit=True", line);
        Assert.Contains("virtualXrTriggerR=1.25", line);
        Assert.Contains("virtualXrFillMode=SignalBarClose", line);
        Assert.Contains("useSwingCEdgeTakeProfit=True", line);
        Assert.Contains("internalEVR=", line);
        Assert.Contains("internalNetR=", line);
        Assert.Contains("internalNetUsd=", line);
        Assert.Contains("internalPF=", line);
        Assert.Contains("closedCount=1", line);
        Assert.DoesNotContain("rawNet", line);
    }
}
