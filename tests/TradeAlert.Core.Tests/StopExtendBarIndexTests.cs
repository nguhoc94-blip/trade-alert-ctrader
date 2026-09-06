using TradeAlert.Core.Drawing;
using Xunit;

namespace TradeAlert.Core.Tests;

public class StopExtendBarIndexTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(42, 41)]
    public void ToChartRightBarIndex_MapsPineOpenTimeToCTrader(int pineBar, int chartBar)
    {
        Assert.Equal(chartBar, KeyLevelVisual.ToChartRightBarIndex(pineBar));
    }

    [Theory]
    [InlineData(42, 40)]
    [InlineData(41, 39)]
    public void ObLoseStopExtend_ChartRightBar_IsLoseTouchBar(int barIndex, int checkBar)
    {
        var pineRight = checkBar + 1;
        Assert.Equal(barIndex - 1, pineRight);
        Assert.Equal(checkBar, KeyLevelVisual.ToChartRightBarIndex(pineRight));
    }
}
