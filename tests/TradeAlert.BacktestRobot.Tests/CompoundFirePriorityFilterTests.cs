using System;
using System.Linq;
using TradeAlert.Core.Models.Alerts;
using TradeAlert.BacktestRobot.Execution;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests;

public class CompoundFirePriorityFilterTests
{
    static CompoundFireEvent Ev(int slot, SignalDirection dir, int bar = 100) =>
        new(slot, $"R{slot + 1}", dir, "15", bar, DateTime.UnixEpoch, 100.0, bar);

    [Fact]
    public void Apply_SkipsR1_WhenR3FiresSameBarAndDirection()
    {
        var input = new[] { Ev(0, SignalDirection.Buy), Ev(2, SignalDirection.Buy) };
        var result = CompoundFirePriorityFilter.Apply(input, enabled: true);
        Assert.Single(result);
        Assert.Equal(2, result[0].SlotIndex);
    }

    [Fact]
    public void Apply_SkipsR5_WhenR4FiresSameBarAndDirection()
    {
        var input = new[] { Ev(3, SignalDirection.Sell), Ev(4, SignalDirection.Sell) };
        var result = CompoundFirePriorityFilter.Apply(input, enabled: true);
        Assert.Single(result);
        Assert.Equal(3, result[0].SlotIndex);
    }

    [Fact]
    public void Apply_KeepsR1R2_WhenNoHigherTierSameBarDirection()
    {
        var input = new[] { Ev(0, SignalDirection.Buy), Ev(1, SignalDirection.Sell) };
        var result = CompoundFirePriorityFilter.Apply(input, enabled: true);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Apply_KeepsOppositeDirection_OnSameBar()
    {
        var input = new[] { Ev(1, SignalDirection.Sell), Ev(4, SignalDirection.Buy) };
        var result = CompoundFirePriorityFilter.Apply(input, enabled: true);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Apply_Disabled_KeepsAll()
    {
        var input = new[] { Ev(0, SignalDirection.Buy), Ev(2, SignalDirection.Buy) };
        var result = CompoundFirePriorityFilter.Apply(input, enabled: false);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Apply_TopTier_SuppressesMidAndLowTiers_SameBarDirection()
    {
        var input = new[] { Ev(2, SignalDirection.Buy), Ev(4, SignalDirection.Buy), Ev(0, SignalDirection.Buy) };
        var result = CompoundFirePriorityFilter.Apply(input, enabled: true);
        Assert.Single(result);
        Assert.Equal(2, result[0].SlotIndex);
    }

    [Fact]
    public void Apply_TopTier_SuppressesMidTier_WhenNoOtherTopTierPresent()
    {
        var input = new[] { Ev(4, SignalDirection.Buy), Ev(2, SignalDirection.Buy) };
        var result = CompoundFirePriorityFilter.Apply(input, enabled: true);
        Assert.Single(result);
        Assert.Equal(2, result[0].SlotIndex);
    }

    [Fact]
    public void Apply_R3AndR4_BothKept_SameTopTier()
    {
        var input = new[] { Ev(2, SignalDirection.Buy), Ev(3, SignalDirection.Buy) };
        var result = CompoundFirePriorityFilter.Apply(input, enabled: true);
        Assert.Equal(2, result.Count);
        Assert.Equal(3, result[0].SlotIndex);
        Assert.Equal(2, result[1].SlotIndex);
    }

    [Fact]
    public void Apply_R5AndR6_BothKept_SameMidTier()
    {
        var input = new[] { Ev(4, SignalDirection.Buy), Ev(5, SignalDirection.Sell) };
        var result = CompoundFirePriorityFilter.Apply(input, enabled: true);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Apply_SortsByTierDescThenWithinTierRank()
    {
        var input = new[]
        {
            Ev(2, SignalDirection.Buy, bar: 100),
            Ev(4, SignalDirection.Sell, bar: 101),
            Ev(0, SignalDirection.Buy, bar: 100),
            Ev(5, SignalDirection.Sell, bar: 101),
        };
        var result = CompoundFirePriorityFilter.Apply(input, enabled: true);
        Assert.Equal(3, result.Count);
        Assert.Equal(2, result[0].SlotIndex);
        Assert.Equal(4, result[1].SlotIndex);
        Assert.Equal(5, result[2].SlotIndex);
    }

    [Fact]
    public void Apply_LogsSkippedLowerPriority()
    {
        var input = new[] { Ev(4, SignalDirection.Sell), Ev(2, SignalDirection.Sell) };
        string? logged = null;
        var result = CompoundFirePriorityFilter.Apply(input, enabled: true, log: m => logged = m);
        Assert.Single(result);
        Assert.Equal(2, result[0].SlotIndex);
        Assert.Contains("priority skip R5", logged);
        Assert.Contains("tier 1 < 2", logged);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(4, 1)]
    [InlineData(5, 1)]
    public void GetPriorityTier_MapsSlotsToTiers(int slot, int expectedTier)
    {
        Assert.Equal(expectedTier, CompoundFirePriorityFilter.GetPriorityTier(slot));
    }
}
