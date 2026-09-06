using TradeAlert.Core.Engines;
using TradeAlert.Core.Models;
using TradeAlert.Core.Series;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class OnBarTickDedupTests
{
    static BarSnapshot Bar(int idx, double open, double high, double low, double close) =>
        new(DateTime.SpecifyKind(new DateTime(2026, 5, 19, 0, 0, 0).AddMinutes(idx * 5), DateTimeKind.Unspecified),
            open, high, low, close);

    [Fact]
    public void OnBar_same_index_does_not_duplicate_penH_entries()
    {
        var buf = new SeriesBuffer();
        var eng = new PineStateEngine
        {
            TickSize = 0.01,
            KeylevelAvgBodyLen = 5,
            KeylevelAtrLen = 3,
            KeylevelMinBodyMult = 0.4,
            UsePullbackFilter = true,
        };

        var bars = new[]
        {
            Bar(0, 100, 100.5, 99.5, 100.2),
            Bar(1, 100, 100.5, 99.5, 100.2),
            Bar(2, 100, 100.5, 99.5, 100.2),
            Bar(3, 100, 100.5, 99.5, 100.2),
            Bar(4, 100, 100.5, 99.5, 100.2),
            Bar(5, 101.0, 103.0, 100.9, 102.5),
            Bar(6, 102.4, 102.6, 102.3, 102.5),
        };

        for (var i = 0; i < bars.Length; i++)
            buf.Upsert(i, bars[i], new BarRuntimeFlags(false, false, false, false, false, false));

        eng.OnBar(buf, 6);
        var afterFirst = eng.Filter.PenH.Count;

        buf.Upsert(6, bars[6], new BarRuntimeFlags(false, true, false, true, false, false));
        eng.OnBar(buf, 6);
        var afterDuplicateTick = eng.Filter.PenH.Count;

        Assert.True(afterFirst > 0);
        Assert.Equal(afterFirst, afterDuplicateTick);
        Assert.Equal(6, eng.LastEvaluatedBarIndex);
    }

    [Fact]
    public void OnBar_confirmed_repass_on_same_index_runs_full_engine()
    {
        var buf = new SeriesBuffer();
        var eng = new PineStateEngine
        {
            TickSize = 0.01,
            KeylevelAvgBodyLen = 5,
            KeylevelAtrLen = 3,
            KeylevelMinBodyMult = 0.4,
            UsePullbackFilter = true,
        };

        var bars = new[]
        {
            Bar(0, 100, 100.5, 99.5, 100.2),
            Bar(1, 100, 100.5, 99.5, 100.2),
            Bar(2, 100, 100.5, 99.5, 100.2),
            Bar(3, 100, 100.5, 99.5, 100.2),
            Bar(4, 100, 100.5, 99.5, 100.2),
            Bar(5, 101.0, 103.0, 100.9, 102.5),
            Bar(6, 102.4, 103.5, 102.3, 103.0),
        };

        for (var i = 0; i < bars.Length; i++)
            buf.Upsert(i, bars[i], new BarRuntimeFlags(false, false, false, false, false, false));

        eng.OnBar(buf, 6);
        var pivotsAfterForming = eng.Pivots.Count;

        buf.Upsert(6, bars[6], new BarRuntimeFlags(false, false, true, false, false, false));
        eng.OnBar(buf, 6);

        Assert.True(eng.Pivots.Count >= pivotsAfterForming);
    }

    [Fact]
    public void PushStep1_skips_when_last_queue_entry_is_same_bar_A()
    {
        var filter = new PullbackFilterEngine();
        filter.PushStep1(10, effBull: false, effBear: true, effTier: 2, calcIsGap: false,
            pivCandHigh: 1.30, pivCandLow: 1.20);
        filter.PushStep1(10, effBull: false, effBear: true, effTier: 2, calcIsGap: false,
            pivCandHigh: 1.30, pivCandLow: 1.20);

        Assert.Equal(1, filter.PenH.Count);
        Assert.Equal(1, filter.PenL.Count);
    }
}
