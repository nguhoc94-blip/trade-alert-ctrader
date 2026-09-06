using System;
using TradeAlert.Core.Models;
using TradeAlert.Core.Series;
using Xunit;

namespace TradeAlert.Core.Tests;

public class SeriesBufferOffsetTests
{
    static BarSnapshot B(int idx, double c) =>
        new(DateTime.SpecifyKind(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Unspecified).AddMinutes(idx), DateTimeKind.Unspecified),
            c - 1, c + 1, c - 2, c, 0);

    [Fact]
    public void Upsert_SameBarIndex_UpdatesFormingBar_AndOffset0ReflectsLatest()
    {
        var s = new SeriesBuffer();
        var f = default(BarRuntimeFlags);
        s.Upsert(10, B(10, 100), f);
        s.Upsert(11, B(11, 200), f);
        s.Upsert(12, B(12, 300), f);
        Assert.Equal(300, s.CloseAt(0));
        Assert.Equal(200, s.CloseAt(1));
        Assert.Equal(100, s.CloseAt(2));

        s.Upsert(12, new BarSnapshot(B(12, 300).OpenChartTimeLocal, 299, 305, 298, 301, 0), f);
        Assert.Equal(301, s.CloseAt(0));
        Assert.Equal(200, s.CloseAt(1));
    }

    [Fact]
    public void TryGetOhlc_InvalidOffsetOrMissing_ReturnsFalse_GetReturnsNan()
    {
        var s = new SeriesBuffer();
        var f = default(BarRuntimeFlags);
        s.Upsert(5, B(5, 50), f);
        s.Upsert(6, B(6, 60), f);

        Assert.False(s.TryGetOhlcAtOffset(-1, out _));
        var ohlc = s.GetOhlcAtOffset(5);
        Assert.True(ohlc.IsNaN);
        Assert.True(double.IsNaN(s.OpenAt(99)));
    }

    [Fact]
    public void SetCurrentEvaluationBarIndex_ChangesOffset0()
    {
        var s = new SeriesBuffer();
        var f = default(BarRuntimeFlags);
        s.Upsert(20, B(20, 1), f);
        s.Upsert(21, B(21, 2), f);
        s.Upsert(22, B(22, 3), f);
        Assert.Equal(3, s.CloseAt(0));

        s.SetCurrentEvaluationBarIndex(21);
        Assert.Equal(2, s.CloseAt(0));
        Assert.Equal(1, s.CloseAt(1));
    }
}
