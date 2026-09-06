using TradeAlert.Core.Drawing;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.KeyLevel;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class PivotTrimEngineTests
{
    [Fact]
    public void TrimOutsideLookback_RemovesOldestPivot_WhenBarTooOld()
    {
        var pivots = new PivotStateStore();
        pivots.PushPivot(1.0, 10, 1, 0, 1, 0);
        pivots.PushPivot(1.1, 250, -1, 0, 1, 1);

        var trimmed = PivotTrimEngine.TrimOutsideLookback(
            barIndex: 300, lookbackBars: 200, pivots, obPool: null, sink: null);

        Assert.Equal(1, trimmed);
        Assert.Equal(1, pivots.Count);
        Assert.Equal(250, pivots.GetSnapshot(0).BarIndex);
    }

    [Fact]
    public void TrimOutsideLookback_KeepsPivotAtLookbackBoundary()
    {
        var pivots = new PivotStateStore();
        pivots.PushPivot(1.0, 100, 1, 0, 1, 0);

        var trimmed = PivotTrimEngine.TrimOutsideLookback(
            barIndex: 300, lookbackBars: 200, pivots, obPool: null, sink: null);

        Assert.Equal(0, trimmed);
        Assert.Equal(1, pivots.Count);
    }

    [Fact]
    public void TrimOutsideLookback_AdjustsIndexRefsAndObOwners()
    {
        var pivots = new PivotStateStore();
        pivots.PushPivot(1.0, 10, 1, 0, 1, 0);
        pivots.PushPivot(1.1, 250, -1, 0, 1, 1);
        pivots.SetParent(1, 1);
        pivots.SetMainDIdx(1, 1);
        pivots.SetKeyGroupId(1, 1);

        var pool = new ObPoolStore();
        pool.Push(2, 1, 1.2, 260, 1, 1, 0, 1, -1, 1, 1);

        PivotTrimEngine.TrimOutsideLookback(
            barIndex: 300, lookbackBars: 200, pivots, pool, sink: null);

        Assert.Equal(0, pivots.GetParent(0));
        Assert.Equal(0, pivots.GetMainDIdx(0));
        Assert.Equal(0, pivots.GetKeyGroupId(0));
        Assert.Equal(0, pool.GetRecord(0).Owner);
    }

    [Fact]
    public void IsWithinLookbackWindow_MatchesPinePushFilter()
    {
        Assert.True(PivotTrimEngine.IsWithinLookbackWindow(300, 100, 200));
        Assert.False(PivotTrimEngine.IsWithinLookbackWindow(301, 100, 200));
    }

    [Fact]
    public void BreakTick_ProcessesMainInsideLookbackWindow()
    {
        var pivots = new PivotStateStore();
        pivots.PushPivot(1.0, 101, 1, 0, 1, 0);
        pivots.SetFlag(0, 0);
        pivots.SetOriginalFlag(0, 0);

        PivotTrimEngine.TrimOutsideLookback(301, 200, pivots, null, null);
        Assert.Equal(1, pivots.Count);

        var engine = new PivotTransitionEngine();
        engine.Tick(
            barIndex: 301,
            pivots,
            openUsed: 0.95,
            highUsed: 1.05,
            lowUsed: 0.94,
            closeUsed: 1.02,
            rawClearBody: true,
            rawIsDoji: false,
            isM5: false);

        Assert.Equal(3, pivots.GetFlag(0));
    }

    [Fact]
    public void TrimOutsideLookback_EmitsDelLine_ForRemovedSegment()
    {
        var pivots = new PivotStateStore();
        pivots.PushPivot(1.0, 10, 1, 0, 1, 0);
        pivots.PushPivot(1.1, 250, -1, 0, 1, 1);

        var sink = new CollectSink();
        PivotTrimEngine.TrimOutsideLookback(300, 200, pivots, null, sink);

        Assert.Contains(sink.Commands, c =>
            c.Kind == DrawingCommandKind.DelLine && c.LabelKeyStr == "ln_10_250");
    }

    [Fact]
    public void ReconcileLinesOutsideLookback_DeletesOrphanLineToFirstPivot()
    {
        var pivots = new PivotStateStore();
        pivots.PushPivot(1.1, 250, -1, 0, 1, 1);
        pivots.SetPrevConnectBar(0, 10);

        var sink = new CollectSink();
        PivotTrimEngine.TrimOutsideLookback(300, 200, pivots, null, sink);

        Assert.Contains(sink.Commands, c =>
            c.Kind == DrawingCommandKind.DelLine && c.LabelKeyStr == "ln_10_250");
    }

    sealed class CollectSink : IDrawingCommandSink
    {
        public readonly List<DrawingCommand> Commands = new();
        public void Enqueue(in DrawingCommand cmd) => Commands.Add(cmd);
    }
}
