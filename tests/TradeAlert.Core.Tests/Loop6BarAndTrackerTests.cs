using TradeAlert.Indicator;
using Xunit;

namespace TradeAlert.Core.Tests;

/// <summary>Độc lập cTrader — chỉ logic cờ / tracker Loop6.</summary>
public sealed class Loop6BarRuntimeMapperTests
{
    [Fact]
    public void AttachHistory_LastBar_IsConfirmedHistoryNotRealtime_NotNew()
    {
        var f = Loop6BarRuntimeMapper.ForAttachHistoryPhaseLastBar();
        Assert.True(f.IsLast);
        Assert.True(f.IsHistory);
        Assert.True(f.IsConfirmed);
        Assert.False(f.IsRealtime);
        Assert.False(f.IsNew);
    }

    [Fact]
    public void LiveForming_LastBar_IsRealtimeUnconfirmed_HistoryFalse()
    {
        var noNew = Loop6BarRuntimeMapper.ForLiveRealtimeFormingLastBar(false);
        Assert.True(noNew.IsLast);
        Assert.True(noNew.IsRealtime);
        Assert.False(noNew.IsConfirmed);
        Assert.False(noNew.IsHistory);
        Assert.False(noNew.IsNew);

        var withNew = Loop6BarRuntimeMapper.ForLiveRealtimeFormingLastBar(true);
        Assert.True(withNew.IsNew);
    }

    [Fact]
    public void BacktestForming_LastBar_NotHistory_UnconfirmedUntilClosed()
    {
        var forming = Loop6BarRuntimeMapper.ForBacktestFormingLastBar(false);
        Assert.True(forming.IsLast);
        Assert.False(forming.IsRealtime);
        Assert.False(forming.IsConfirmed);
        Assert.False(forming.IsHistory);
        Assert.False(forming.IsNew);
    }

    [Fact]
    public void BacktestClosedHistoricalLastBar_IsConfirmedNotHistory()
    {
        var f = Loop6BarRuntimeMapper.ForBacktestClosedHistoricalLastBar();
        Assert.True(f.IsLast);
        Assert.True(f.IsConfirmed);
        Assert.False(f.IsHistory);
        Assert.False(f.IsNew);
    }
}

public sealed class Loop6LastBarOpenTrackerTests
{
    [Fact]
    public void SeedSilent_ThenSameOpenOnLive_NoIsNew()
    {
        var t = new Loop6LastBarOpenTracker();
        var ot = DateTime.SpecifyKind(new DateTime(2026, 6, 1, 10, 0, 0), DateTimeKind.Unspecified);
        t.SeedSilent(ot);
        Assert.False(t.TryConsumeNewBarOpen(ot));
    }

    [Fact]
    public void SeedSilent_ThenFirstLiveTick_SameOpen_NotIsNew()
    {
        var t = new Loop6LastBarOpenTracker();
        var ot = DateTime.SpecifyKind(new DateTime(2026, 6, 1, 10, 15, 0), DateTimeKind.Unspecified);
        t.SeedSilent(ot);
        Assert.False(t.TryConsumeNewBarOpen(ot));
        Assert.False(t.TryConsumeNewBarOpen(ot));
    }

    [Fact]
    public void AfterLive_OpenChanges_IsNewOnceThenStable()
    {
        var t = new Loop6LastBarOpenTracker();
        var o1 = DateTime.SpecifyKind(new DateTime(2026, 6, 1, 11, 0, 0), DateTimeKind.Unspecified);
        var o2 = DateTime.SpecifyKind(new DateTime(2026, 6, 1, 11, 15, 0), DateTimeKind.Unspecified);
        t.SeedSilent(o1);
        Assert.False(t.TryConsumeNewBarOpen(o1));
        Assert.True(t.TryConsumeNewBarOpen(o2));
        Assert.False(t.TryConsumeNewBarOpen(o2));
        Assert.False(t.TryConsumeNewBarOpen(o2));
    }

    [Fact]
    public void TryConsumeWithoutSeed_FirstCallIsNotNew_BarsThenNewOnChange()
    {
        var t = new Loop6LastBarOpenTracker();
        var o1 = DateTime.SpecifyKind(new DateTime(2026, 1, 2), DateTimeKind.Unspecified);
        Assert.False(t.TryConsumeNewBarOpen(o1));
        Assert.False(t.TryConsumeNewBarOpen(o1));
        Assert.True(t.TryConsumeNewBarOpen(o1.AddHours(1)));
    }
}
