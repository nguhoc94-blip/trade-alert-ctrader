using TradeAlert.Core.Models;
using TradeAlert.Indicator;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class SeriesBufferSyncTests
{
    [Fact]
    public void OnCalculateBar_WithoutBackfill_Throws()
    {
        var s = new SeriesBufferSync();
        var bar = new BarSnapshot(
            DateTime.SpecifyKind(new DateTime(2026, 5, 10), DateTimeKind.Unspecified),
            1, 1, 1, 1);
        var flags = new BarRuntimeFlags(false, false, false, false, false, false);
        Assert.Throws<InvalidOperationException>(() => s.OnCalculateBar(0, bar, flags));
    }

    [Fact]
    public void InitialBackfill_then_OnCalculateBar_OnlyTouchesOneIndexPerCall()
    {
        var s = new SeriesBufferSync();
        BarSnapshot B(int i) => new(
            DateTime.SpecifyKind(new DateTime(2026, 5, 10).AddMinutes(i), DateTimeKind.Unspecified),
            i, i, i, i);
        var f = new BarRuntimeFlags(false, false, true, false, false, false);

        s.InitialBackfill(1, idx => (B(idx), f));
        Assert.Equal(1, s.Buffer.CurrentEvaluationBarIndex);

        s.OnCalculateBar(2, B(2), f);
        Assert.Equal(2, s.Buffer.CurrentEvaluationBarIndex);
        Assert.True(s.Buffer.TryGetOhlcAtOffset(0, out var o) && Math.Abs(o.Close - 2) < 1e-9);
    }

    [Fact]
    public void SecondInitialBackfill_IsNoOp_NoRescan()
    {
        var s = new SeriesBufferSync();
        var f = new BarRuntimeFlags(false, false, true, false, false, false);
        var callCount = 0;
        s.InitialBackfill(2, _ =>
        {
            callCount++;
            return (new BarSnapshot(
                DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Unspecified),
                callCount, callCount, callCount, callCount), f);
        });
        Assert.Equal(3, callCount);

        s.InitialBackfill(10, _ => throw new Exception("should not rescan"));
        Assert.Equal(3, callCount);
    }
}
