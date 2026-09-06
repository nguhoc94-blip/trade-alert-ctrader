using TradeAlert.Core.Engines;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class TimeframeMappingTests
{
    [Theory]
    [InlineData(300, "5", "2")]
    [InlineData(900, "15", "5")]
    [InlineData(3600, "60", "15")]
    [InlineData(14400, "240", "60")]
    [InlineData(86400, "D", "240")]
    public void ResolveLtf_MatchesPine(int htfSec, string htfToken, string ltfToken)
    {
        Assert.Equal(htfToken, TimeframeMapping.ChartTfToken(htfSec));
        Assert.Equal(ltfToken, TimeframeMapping.ResolveLtfTokenByHtf(htfSec));
    }

    [Fact]
    public void Daily_NoiseTf_DiffersFromLtfWick()
    {
        Assert.Equal("240", TimeframeMapping.ResolveLtfTokenByHtf(86400));
        Assert.Equal("60", TimeframeMapping.GetNoiseTfToken(86400));
    }
}
