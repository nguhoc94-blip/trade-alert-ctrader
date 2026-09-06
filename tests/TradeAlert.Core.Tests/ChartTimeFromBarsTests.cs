using TradeAlert.Core.Models;
using TradeAlert.Core.Models.Alerts;
using TradeAlert.Indicator;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class ChartTimeFromBarsTests
{
    [Fact]
    public void Unspecified_PreservedAsChartLocalWall()
    {
        var t = DateTime.SpecifyKind(new DateTime(2026, 5, 10, 9, 0, 0), DateTimeKind.Unspecified);
        var n = ChartTimeFromBars.NormalizeBarOpenTime(t);
        Assert.Equal(DateTimeKind.Unspecified, n.Kind);
        Assert.Equal(t.Ticks, n.Ticks);
    }

    [Fact]
    public void Utc_ConvertsToChartWallClock_FixedOffset_NotMachineLocal()
    {
        var utc = DateTime.SpecifyKind(new DateTime(2026, 5, 10, 2, 0, 0), DateTimeKind.Utc);
        var n = ChartTimeFromBars.NormalizeBarOpenTime(utc);
        Assert.Equal(DateTimeKind.Unspecified, n.Kind);
        Assert.Equal(9, n.Hour);
        Assert.Equal(10, n.Day);
    }

    [Fact]
    public void Local_Throws()
    {
        var local = DateTime.SpecifyKind(new DateTime(2026, 1, 1), DateTimeKind.Local);
        Assert.Throws<ArgumentException>(() => ChartTimeFromBars.NormalizeBarOpenTime(local));
    }
}
