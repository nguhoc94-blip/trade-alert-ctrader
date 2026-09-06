using TradeAlert.Core.Models;
using TradeAlert.Core.Models.Alerts;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class DuplicateKeyTests
{
    static DateTime T() => DateTime.SpecifyKind(new DateTime(2026, 5, 10, 12, 0, 0), DateTimeKind.Unspecified);

    [Fact]
    public void ToCanonicalString_IsStableAcrossBuilds()
    {
        var k = new DuplicateKey("XAUUSD", "5", "BUY EVENT M5", "condBuyEventM5", T(), 100, "5");
        var a = k.ToCanonicalString();
        var b = k.ToCanonicalString();
        Assert.Equal(a, b);
        Assert.StartsWith("sym=XAUUSD|", a, StringComparison.Ordinal);
    }

    [Fact]
    public void ToCanonicalString_ChangesWhenFieldChanges()
    {
        var k1 = new DuplicateKey("XAUUSD", "5", "A", "condBuyEventM5", T(), 100, "5");
        var k2 = new DuplicateKey("XAUUSD", "5", "A", "condBuyEventM5", T(), 101, "5");
        Assert.NotEqual(k1.ToCanonicalString(), k2.ToCanonicalString());
    }

    [Fact]
    public void DuplicateKey_RejectsMachineLocalTime()
    {
        var local = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Local);
        Assert.Throws<ArgumentException>(() =>
            new DuplicateKey("X", "5", "T", "c", local, 0, "5"));
    }
}
