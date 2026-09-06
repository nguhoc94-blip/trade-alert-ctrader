using System;
using TradeAlert.Core.Adapters;
using TradeAlert.Core.Models;
using Xunit;

namespace TradeAlert.Core.Tests;

public class FakeCandleFeedAdapterTests
{
    [Fact]
    public void ToBarSnapshot_ReturnsSeededBar()
    {
        var t = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);
        var a = new FakeCandleFeedAdapter(new[]
        {
            (new BarSnapshot(t, 1, 2, 0.5, 1.5), default(BarRuntimeFlags))
        });
        Assert.Equal(1.5, a.ToBarSnapshot(0, false).Close);
    }
}
