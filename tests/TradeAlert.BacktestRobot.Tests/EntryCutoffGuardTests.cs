using System;
using TradeAlert.BacktestRobot.Execution;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests;

public class EntryCutoffGuardTests
{
    static DateTime UtcFromBangkok(int year, int month, int day, int hour, int minute = 0) =>
        new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc) - TimeSpan.FromHours(7);

    [Theory]
    [InlineData(21, false)]
    [InlineData(22, true)]
    [InlineData(23, true)]
    [InlineData(0, true)]
    [InlineData(2, true)]
    public void IsEntryBlocked_Bangkok22ToSessionStop3(int bkkHour, bool expectedBlocked)
    {
        var utc = UtcFromBangkok(2024, 6, 15, bkkHour);
        var blocked = EntryCutoffGuard.IsEntryBlocked(utc, enabled: true, cutoffHourBangkok: 22, sessionStopHourBangkok: 3);
        Assert.Equal(expectedBlocked, blocked);
    }

    [Fact]
    public void IsEntryBlocked_Disabled_NeverBlocks()
    {
        var utc = UtcFromBangkok(2024, 6, 15, 23);
        Assert.False(EntryCutoffGuard.IsEntryBlocked(utc, enabled: false, 22, 3));
    }

    [Fact]
    public void IsEntryBlocked_Midday_AllowsEntries()
    {
        var utc = UtcFromBangkok(2024, 6, 15, 14);
        Assert.False(EntryCutoffGuard.IsEntryBlocked(utc, enabled: true, 22, 3));
    }
}
