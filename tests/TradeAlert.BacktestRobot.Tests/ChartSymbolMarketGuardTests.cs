using TradeAlert.Indicator;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests;

public sealed class ChartSymbolMarketGuardTests
{
    [Theory]
    [InlineData("XAUUSD", "XAUUSD", true)]
    [InlineData("xauusd", "XAUUSD", true)]
    [InlineData("EURUSD", "XAUUSD", false)]
    [InlineData(null, "XAUUSD", true)]
    [InlineData("", "XAUUSD", true)]
    [InlineData("EURUSD", null, true)]
    [InlineData("EURUSD", "", true)]
    public void IsAllowed_respects_bound_symbol(string? requested, string? bound, bool expected) =>
        Assert.Equal(expected, ChartSymbolMarketGuard.IsAllowed(requested, bound));

    [Fact]
    public void FormatIgnoredWarning_uses_l6bt_prefix() =>
        Assert.Equal("[L6BT] WARNING ignored non-chart symbol request: EURUSD",
            ChartSymbolMarketGuard.FormatIgnoredWarning("EURUSD"));
}
