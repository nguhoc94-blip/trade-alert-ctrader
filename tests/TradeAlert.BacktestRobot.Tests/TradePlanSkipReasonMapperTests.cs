using TradeAlert.BacktestRobot.Execution.Analytics;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests;

public sealed class TradePlanSkipReasonMapperTests
{
    [Theory]
    [InlineData("SL too tight: 1.2p < min 2.0p", TradePlanSkipReasonMapper.SlBelowMin)]
    [InlineData("no active swing B on M15", TradePlanSkipReasonMapper.BMapperFail)]
    [InlineData("swing-C RR 1.2 < base 2.0", TradePlanSkipReasonMapper.RrFail)]
    [InlineData("ZONE gate overlap", TradePlanSkipReasonMapper.ZoneGate)]
    [InlineData("outside session window", TradePlanSkipReasonMapper.OutsideSession)]
    public void Map_normalizes_trade_planner_skip_codes(string reason, string expected) =>
        Assert.Equal(expected, TradePlanSkipReasonMapper.Map(reason));
}
