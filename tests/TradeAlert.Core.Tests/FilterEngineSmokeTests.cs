using TradeAlert.Core.Engines;
using Xunit;

namespace TradeAlert.Core.Tests;

public class FilterEngineSmokeTests
{
    [Theory]
    [InlineData(240 * 60, "60")]
    [InlineData(60 * 60, "15")]
    [InlineData(15 * 60, "5")]
    [InlineData(5 * 60, "2")]
    [InlineData(60, "1")]
    public void GetNoiseTf_Buckets(int tfSec, string expected) =>
        Assert.Equal(expected, FilterEngine.GetNoiseTf(tfSec));

    [Fact]
    public void IsNoisyWick_Smoke()
    {
        var ok = FilterEngine.IsNoisyWick(1, true, 1.0, 3, 0.5, 1.2,
            _ => 1, _ => 3, _ => 0, _ => 2);
        Assert.False(ok);
    }

    [Fact]
    public void IsRealPullback_FilterDisabled_True() =>
        Assert.True(FilterEngine.IsRealPullback(1, 1, false, _ => 0, _ => 0, 5));

    [Fact]
    public void IsRealPullback_FilterOn_TypNegative_PrevGreenCurRed_BodyBreaks_ReturnsTrue()
    {
        const int barIndex = 4;
        Func<int, double> o = off => off == 1 ? 10.0 : off == 2 ? 8.0 : double.NaN;
        Func<int, double> c = off => off == 1 ? 6.0 : off == 2 ? 10.0 : double.NaN;
        var r = FilterEngine.IsRealPullback(-1, 1, true, o, c, barIndex);
        Assert.True(r);
    }

    [Fact]
    public void ValidPullbackLow_FilterOff_True()
    {
        var r = FilterEngine.ValidPullbackLow(0, false, _ => 0, _ => 0, _ => 0, 5);
        Assert.True(r);
    }

    [Fact]
    public void ValidPullbackHigh_FilterOff_True()
    {
        var r = FilterEngine.ValidPullbackHigh(0, false, _ => 0, _ => 0, _ => 0, 5);
        Assert.True(r);
    }

    [Fact]
    public void FailedByContinuationHigh_DetectsBreak()
    {
        var barIndex = 10;
        double[] highs = new double[20];
        for (var i = 0; i <= barIndex; i++)
            highs[barIndex - i] = 100 + i * 0.1;
        highs[barIndex - 9] = 200;
        var r = FilterEngine.FailedByContinuationHigh(5, 150, i => highs[i], barIndex);
        Assert.True(r);
    }

    [Fact]
    public void FailedByContinuationLow_DetectsBreak()
    {
        var barIndex = 10;
        double[] lows = new double[20];
        for (var i = 0; i <= barIndex; i++)
            lows[barIndex - i] = 100 - i * 0.1;
        lows[barIndex - 8] = 10;
        var r = FilterEngine.FailedByContinuationLow(5, 50, i => lows[i], barIndex);
        Assert.True(r);
    }
}
