using System.Collections.Generic;
using TradeAlert.Core.Context;
using TradeAlert.Core.Models;
using TradeAlert.Core.Series;
using Xunit;

namespace TradeAlert.Core.Tests;

public class EngineContextSmokeTest
{
    [Fact]
    public void Primary_Resolves()
    {
        var series = new SeriesBuffer();
        var tf = new TimeframeState
        {
            Symbol = "X",
            TimeframeId = "15",
            Series = series,
            CurrentBarFlags = default,
            CurrentBarIndex = 0
        };
        var ctx = new EngineContext
        {
            Symbol = "X",
            PrimaryTimeframeId = "15",
            Timeframes = new Dictionary<string, TimeframeState> { ["15"] = tf }
        };
        Assert.Same(tf, ctx.Primary);
    }
}
