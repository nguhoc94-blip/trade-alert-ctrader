using TradeAlert.Core.Models.Alerts;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class AlertBarTimingTests
{
    [Theory]
    [InlineData("5", true, false, true)]
    [InlineData("5", false, true, true)]
    [InlineData("5", false, false, false)]
    [InlineData("15", true, false, false)]
    [InlineData("60", true, false, false)]
    public void IsM5JustClosedGate_OnlyM5Chart(string tf, bool closed, bool isNew, bool expected) =>
        Assert.Equal(expected, AlertBarTiming.IsM5JustClosedGate(tf, closed, isNew));

    [Theory]
    [InlineData("15", true, false, 0, true)]
    [InlineData("15", false, true, 30, true)]
    [InlineData("5", true, false, 0, true)]
    [InlineData("5", true, false, 5, false)]
    [InlineData("60", true, false, 0, false)]
    public void IsM15JustClosedGate_M15OrM5BoundaryOnly(
        string tf, bool closed, bool isNew, int minute, bool expected) =>
        Assert.Equal(expected, AlertBarTiming.IsM15JustClosedGate(tf, closed, isNew, minute));

    [Fact]
    public void ComputeM15CloseEdge_RealtimeEdgeOnlyOnNewBar()
    {
        Assert.False(AlertBarTiming.ComputeM15CloseEdge("15", false, true, false, 0, false));
        Assert.True(AlertBarTiming.ComputeM15CloseEdge("15", false, true, true, 0, false));
        Assert.True(AlertBarTiming.ComputeM15CloseEdge("5", false, true, true, 0, false));
        Assert.False(AlertBarTiming.ComputeM15CloseEdge("5", false, true, true, 5, false));
    }

    [Fact]
    public void ShouldEvaluateOnPass_SplitsStateAndBarClose()
    {
        Assert.True(AlertBarTiming.ShouldEvaluateOnPass(
            AlertTimingClass.RealtimeFilterBar0, passIsBarCloseEval: false, passIsRealtimeStateEval: true));
        Assert.False(AlertBarTiming.ShouldEvaluateOnPass(
            AlertTimingClass.BarCloseM5Event, passIsBarCloseEval: false, passIsRealtimeStateEval: true));
        Assert.True(AlertBarTiming.ShouldEvaluateOnPass(
            AlertTimingClass.BarCloseM5Event, passIsBarCloseEval: true, passIsRealtimeStateEval: false));
    }
}
