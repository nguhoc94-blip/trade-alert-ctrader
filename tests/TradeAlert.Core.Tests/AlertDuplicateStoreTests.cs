using TradeAlert.Core.Models.Alerts;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class AlertDuplicateStoreTests
{
    static DuplicateKey K(long bar) =>
        new("XAUUSD", "5", "SELL EVENT M5", "condSellEventM5",
            DateTime.SpecifyKind(new DateTime(2026, 1, 1, 0, 0, 0), DateTimeKind.Unspecified),
            bar, "5");

    [Fact]
    public void SameKey_SecondRegister_False()
    {
        var s = new AlertDuplicateMemoryStore();
        var k1 = K(1);
        Assert.True(s.TryRegister(in k1, out var r1));
        Assert.Equal(DuplicateRejectReason.None, r1);
        var k1b = K(1);
        Assert.False(s.TryRegister(in k1b, out var r2));
        Assert.Equal(DuplicateRejectReason.AlreadyRegisteredInSession, r2);
    }

    [Fact]
    public void DifferentKey_AfterDuplicate_Allowed()
    {
        var s = new AlertDuplicateMemoryStore();
        var k1 = K(1);
        var k2 = K(2);
        Assert.True(s.TryRegister(in k1, out _));
        Assert.True(s.TryRegister(in k2, out _));
    }

    [Fact]
    public void ResetSession_Clears()
    {
        var s = new AlertDuplicateMemoryStore();
        var k1 = K(1);
        Assert.True(s.TryRegister(in k1, out _));
        s.ResetSession();
        var k1b = K(1);
        Assert.True(s.TryRegister(in k1b, out _));
    }
}
