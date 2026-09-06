using TradeAlert.BacktestRobot.Execution;
using TradeAlert.BacktestRobot.Execution.Analytics;
using TradeAlert.BacktestRobot.Execution.Kl;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests;

public class Loop6AnalyticsTests
{
    [Theory]
    [InlineData(2024, 6, 15, 23, 0, Loop6BangkokSession.Asia1)]       // UTC 23 → BKK 06
    [InlineData(2024, 6, 15, 5, 30, Loop6BangkokSession.Asia2PreLondon)]  // UTC 05:30 → BKK 12:30
    [InlineData(2024, 6, 15, 7, 0, Loop6BangkokSession.LondonOpen)]  // UTC 07 → BKK 14
    [InlineData(2024, 6, 15, 11, 0, Loop6BangkokSession.LondonNyOverlap)]
    [InlineData(2024, 6, 15, 15, 0, Loop6BangkokSession.NyMid)]
    [InlineData(2024, 6, 15, 17, 30, Loop6BangkokSession.LateNy)]
    [InlineData(2024, 6, 15, 20, 0, Loop6BangkokSession.BlockedOutside)]
    public void BangkokSession_MapsHours(int y, int m, int d, int h, int min, string expected)
    {
        var utc = new DateTime(y, m, d, h, min, 0, DateTimeKind.Utc);
        var bkk = Loop6BangkokSession.GetBangkokTime(utc);
        Assert.Equal(expected, Loop6BangkokSession.GetBangkokSession(bkk));
    }

    [Fact]
    public void SafeCsv_EscapesCommaAndQuotes()
    {
        Assert.Equal("hello", Loop6CsvUtil.SafeCsv("hello"));
        Assert.Equal("\"a,b\"", Loop6CsvUtil.SafeCsv("a,b"));
        Assert.Equal("\"say \"\"hi\"\"\"", Loop6CsvUtil.SafeCsv("say \"hi\""));
    }

    [Fact]
    public void SpreadToSlRatio_And_ResultR()
    {
        Assert.Equal(0.2, Loop6AnalyticsMath.ComputeSpreadToSlRatio(10, 50), 3);
        Assert.Equal(2.0, Loop6AnalyticsMath.ComputeResultRByPrice(100, 90, 120, isBuy: true), 3);
        Assert.Equal(-1.0, Loop6AnalyticsMath.ComputeResultRByPrice(100, 90, 90, isBuy: true), 3);
    }

    [Fact]
    public void XauSpreadTelemetry_040Price_Is40Pips()
    {
        var spread = Loop6AnalyticsMath.ResolveSpreadSnapshotForLog(
            0, 0.40, 0.01, 0.01, "XAUUSD", KlAssetType.XAUUSD);
        Assert.Equal(40, spread.Pips, 3);
        Assert.Equal(0.40, spread.Price, 3);
        Assert.Equal(SpreadTelemetrySources.Symbol, spread.Source);

        var overrideSpread = Loop6AnalyticsMath.ResolveSpreadSnapshotForLog(
            0.40, 0, 0.01, 0.01, "XAUUSD", KlAssetType.XAUUSD);
        Assert.Equal(40, overrideSpread.Pips, 3);
        Assert.Equal(0.40, overrideSpread.Price, 3);
        Assert.Equal(SpreadTelemetrySources.OverrideGoldPrice, overrideSpread.Source);
    }

    [Theory]
    [InlineData("SL too tight", Loop6SkipReasonCodes.SlBelowMin)]
    [InlineData("inconsistent levels", Loop6SkipReasonCodes.LevelInvalid)]
    [InlineData("limit wrong side", Loop6SkipReasonCodes.OrderDistanceInvalid)]
    public void SkipReasonCodes_MapKnownReasons(string reason, string expectedCode)
    {
        var (code, _) = Loop6SkipReasonCodes.Map(reason);
        Assert.Equal(expectedCode, code);
    }

    [Fact]
    public void ExcursionTracker_TracksMfeMae()
    {
        var tracker = new Loop6TradeExcursionTracker();
        tracker.OnOpened("L1", isBuy: true, entry: 100, initialSl: 90, pipSize: 1,
            openTimeUtc: DateTime.UtcNow, openBarIndex: 10);
        tracker.UpdateBar("L1", bidHigh: 105, bidLow: 98);
        tracker.UpdateBar("L1", bidHigh: 108, bidLow: 97);

        Assert.True(tracker.TryRemove("L1", out var snap));
        Assert.Equal(8, snap.MfePips, 3);
        Assert.Equal(3, snap.MaePips, 3);
        Assert.Equal(0.8, snap.MfeR, 3);
        Assert.Equal(0.3, snap.MaeR, 3);
    }
}
