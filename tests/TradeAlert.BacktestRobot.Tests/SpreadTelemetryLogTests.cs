using TradeAlert.BacktestRobot.Execution;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests;

public sealed class SpreadTelemetryLogTests
{
    [Fact]
    public void FormatStartup_IncludesSourceAndMinSlMode()
    {
        var line = SpreadTelemetryLog.FormatStartup(
            spreadPipsOverride: 0,
            spreadSource: SpreadTelemetrySources.Symbol,
            spreadPips: 24,
            spreadPrice: 0.24,
            bid: 1850,
            ask: 1850.24,
            enableMinSlConstraint: true,
            minSlBacktestMode: true,
            minSlSpreadMultiple: 10,
            minSlPipsForex: 5);

        Assert.Contains("source=Symbol", line);
        Assert.Contains("spreadPips=24", line);
        Assert.Contains("minSlConstraint=clamp", line);
    }

    [Fact]
    public void FormatPlan_IncludesFloorWhenApplied()
    {
        var plan = new TradePlan
        {
            Label = "L6BT|R3|B100",
            SpreadPipsAtPlan = 12,
            SpreadSourceAtPlan = SpreadTelemetrySources.Symbol,
            StopLossPips = 240,
            SlPipsBeforeFloor = 80,
            MinSlFloorPips = 120,
            SlFloorApplied = true,
        };

        var line = SpreadTelemetryLog.FormatPlan(in plan, ruleSlot: 2, bid: 1.1, ask: 1.1012);

        Assert.Contains("SPREAD PLAN", line);
        Assert.Contains("spreadPips=12", line);
        Assert.Contains("floorApplied=true", line);
        Assert.Contains("slBeforeFloor=80", line);
        Assert.Contains("minSlFloor=120", line);
    }

    [Fact]
    public void FormatFill_IncludesDeltaBetweenPlanAndFillSpread()
    {
        var line = SpreadTelemetryLog.FormatFill(
            "L6BT|R1|B50",
            planSpreadPips: 12,
            planSpreadSource: SpreadTelemetrySources.Symbol,
            fillSpreadPips: 15,
            bid: 1.1,
            ask: 1.1015,
            entryPrice: 1.1005);

        Assert.Contains("planSpread=12(Symbol)", line);
        Assert.Contains("fillSpread=15", line);
        Assert.Contains("delta=3", line);
    }
}
