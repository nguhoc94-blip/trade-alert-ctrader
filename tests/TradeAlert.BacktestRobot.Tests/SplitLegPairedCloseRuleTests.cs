using TradeAlert.BacktestRobot.Execution;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests;

public class SplitLegPairedCloseRuleTests
{
    [Theory]
    [InlineData("L6BT|R2|B14758|C", "L6BT|R2|B14758|D")]
    [InlineData("L6BT|R2|B14758|D", "L6BT|R2|B14758|C")]
    public void TryGetSiblingLabel_MapsCLegs(string label, string expectedSibling)
    {
        Assert.True(SplitLegPairedCloseRule.TryGetSiblingLabel(label, out var sibling));
        Assert.Equal(expectedSibling, sibling);
    }

    [Theory]
    [InlineData("L6BT|R2|B14758")]
    [InlineData("L6BT|R2|B14758|DEFER")]
    [InlineData(null)]
    public void TryGetSiblingLabel_NotSplitLeg_ReturnsFalse(string? label)
    {
        Assert.False(SplitLegPairedCloseRule.TryGetSiblingLabel(label, out _));
    }

    [Fact]
    public void IsSplitLegLabel_TrueForCLeg()
    {
        Assert.True(SplitLegPairedCloseRule.IsSplitLegLabel("L6BT|R3|B100|C"));
    }

    [Fact]
    public void ShouldPairedClose_StopLoss_CancelsPendingSibling()
    {
        Assert.True(SplitLegPairedCloseRule.ShouldPairedClose("StopLoss", 155.96, 156.97, 0.001));
    }

    [Fact]
    public void ShouldPairedClose_TakeProfit_OnlyWhenSameTp()
    {
        Assert.True(SplitLegPairedCloseRule.ShouldPairedClose("TakeProfit", 155.96, 155.96, 0.001));
        Assert.False(SplitLegPairedCloseRule.ShouldPairedClose("TakeProfit", 155.96, 156.97, 0.001));
    }

    [Fact]
    public void ShouldPairedClose_OtherReasons_False()
    {
        Assert.False(SplitLegPairedCloseRule.ShouldPairedClose("ManualClose", 155.96, 155.96, 0.001));
        Assert.False(SplitLegPairedCloseRule.ShouldPairedClose("BBroken", 155.96, 155.96, 0.001));
    }
}
