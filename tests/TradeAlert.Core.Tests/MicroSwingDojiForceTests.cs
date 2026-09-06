using TradeAlert.Core.Engines;
using Xunit;

namespace TradeAlert.Core.Tests;

public class MicroSwing3ojiForceTests
{
    [Fact]
    public void IsDojiSellForce_RangeZero_False()
    {
        var e = new MicroSwingEngine();
        Assert.False(e.IsDojiSellForce(1, 1, 1, 1, 0.25, 0.55, 0.45, 1.0));
    }

    [Fact]
    public void IsDojiBuyForce_RangeZero_False()
    {
        var e = new MicroSwingEngine();
        Assert.False(e.IsDojiBuyForce(1, 1, 1, 1, 0.25, 0.55, 0.45, 1.0));
    }

    [Fact]
    public void MicroSwing3etect_Legacy_ThrowsInvalidOperation()
    {
        // Stub now throws InvalidOperationException (ported — use Detect() instead).
        var e = new MicroSwingEngine();
#pragma warning disable CS0618
        Assert.Throws<InvalidOperationException>(() => e.MicroSwing3etect());
#pragma warning restore CS0618
    }
}
