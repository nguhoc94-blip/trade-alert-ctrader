using TradeAlert.Core.Models.Alerts;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class AlertDedupHelpersTests
{
    [Fact]
    public void CheckAlreadyTriggered_MatchesPineSemantics()
    {
        var list = new[] { "k1", "k2" };
        Assert.True(AlertDedupHelpers.CheckAlreadyTriggered(list, "k1"));
        Assert.False(AlertDedupHelpers.CheckAlreadyTriggered(list, "k3"));
        Assert.False(AlertDedupHelpers.CheckAlreadyTriggered(Array.Empty<string>(), "k1"));
    }
}
