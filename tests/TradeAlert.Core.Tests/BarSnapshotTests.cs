using System;
using TradeAlert.Core.Models;
using Xunit;

namespace TradeAlert.Core.Tests;

public class BarSnapshotTests
{
    [Fact]
    public void UnspecifiedChartLocal_Preserved_AsWallClock_NotMachineUtc()
    {
        var wall = new DateTime(2026, 5, 10, 12, 0, 0, DateTimeKind.Unspecified);
        var b = new BarSnapshot(wall, 1, 2, 0.5, 1.5).NormalizeChartTime();
        Assert.Equal(DateTimeKind.Unspecified, b.OpenChartTimeLocal.Kind);
        Assert.Equal(2026, b.OpenChartTimeLocal.Year);
        Assert.Equal(5, b.OpenChartTimeLocal.Month);
        Assert.Equal(10, b.OpenChartTimeLocal.Day);
        Assert.Equal(12, b.OpenChartTimeLocal.Hour);
        Assert.Equal(0, b.OpenChartTimeLocal.Minute);
        Assert.Equal(0, b.OpenChartTimeLocal.Second);
    }

    [Fact]
    public void ChartLocalUtcPlus7_DerivedUtc_Is0500Z()
    {
        var wall = new DateTime(2026, 5, 10, 12, 0, 0, DateTimeKind.Unspecified);
        var b = new BarSnapshot(wall, 0, 0, 0, 0).NormalizeChartTime();
        var derived = b.OpenTimeUtcDerived;
        Assert.Equal(DateTimeKind.Utc, derived.Kind);
        Assert.Equal(new DateTime(2026, 5, 10, 5, 0, 0, DateTimeKind.Utc), derived);
    }

    [Fact]
    public void EnsureChartLocal_Rejects_DateTimeKind_Local()
    {
        var machineLocal = new DateTime(2026, 5, 10, 12, 0, 0, DateTimeKind.Local);
        Assert.Throws<ArgumentException>(() => ChartTimePolicy.EnsureChartLocalUnspecified(machineLocal));
        Assert.Throws<ArgumentException>(() => new BarSnapshot(machineLocal, 0, 0, 0, 0).NormalizeChartTime());
    }

    [Fact]
    public void EnsureChartLocal_Rejects_DateTimeKind_Utc_UnlessExplicitConverter()
    {
        var utc = new DateTime(2026, 5, 10, 5, 0, 0, DateTimeKind.Utc);
        Assert.Throws<ArgumentException>(() => ChartTimePolicy.EnsureChartLocalUnspecified(utc));
        Assert.Throws<ArgumentException>(() => new BarSnapshot(utc, 0, 0, 0, 0).NormalizeChartTime());
    }

    [Fact]
    public void UtcInstantToChartLocalWallClock_ExplicitPath_MatchesBaselinePlus7()
    {
        var utc = new DateTime(2026, 5, 10, 5, 0, 0, DateTimeKind.Utc);
        var wall = ChartTimePolicy.UtcInstantToChartLocalWallClock(utc);
        Assert.Equal(DateTimeKind.Unspecified, wall.Kind);
        Assert.Equal(new DateTime(2026, 5, 10, 12, 0, 0, DateTimeKind.Unspecified), wall);
    }

    [Fact]
    public void ChartTimePolicy_BaselineConstant_DocumentsDataChartZip()
    {
        Assert.Contains("DATA CHART", ChartTimePolicy.BaselineDataChartZipPath, StringComparison.Ordinal);
    }
}
