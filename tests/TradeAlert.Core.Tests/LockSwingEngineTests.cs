using System.Collections.Generic;
using System.Linq;
using TradeAlert.Core.Drawing;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.KeyLevel;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class LockSwingEngineTests
{
    sealed class CollectSink : IDrawingCommandSink
    {
        public readonly List<DrawingCommand> Commands = new();
        public void Enqueue(in DrawingCommand cmd) => Commands.Add(cmd);
    }

    static PivotStateStore BuildActiveSwings(int count)
    {
        var pivots = new PivotStateStore();
        for (var i = 0; i < count; i++)
        {
            pivots.PushDefaults(1.0 + i * 0.01, 10 + i * 5, type: 1);
            pivots.SetFlag(i, 1);
            pivots.SetHighId(i, i + 1);
        }
        return pivots;
    }

    [Fact]
    public void LockSwingsBefore_NonFake_SetFlagMinus2_AndSaveBeforeLock()
    {
        var pivots = BuildActiveSwings(6);
        var sink = new CollectSink();

        MainPromotionEngine.LockSwingsBefore(pivots, null, beforeIdx: 4, sink, labelOpts: null);

        Assert.Equal(-2, pivots.GetFlag(0));
        Assert.Equal(-2, pivots.GetFlag(1));
        Assert.Equal(-2, pivots.GetFlag(2));
        Assert.Equal(-2, pivots.GetFlag(3));
        Assert.Equal(1, pivots.GetFlag(4));
        Assert.Equal(1, pivots.GetFlagBeforeLock(0));
    }

    [Fact]
    public void LockSwingsBefore_DeletesObsOwnedByLockedPivot()
    {
        var pivots = BuildActiveSwings(4);
        var pool = new ObPoolStore();
        pool.Push(2, 1, 1.0, 10, 1, 0, 0, 1, 1, 1, 2);
        pool.Push(2, 1, 1.1, 20, 1, 2, 0, 2, 1, 2, 3);

        var sink = new CollectSink();
        MainPromotionEngine.LockSwingsBefore(pivots, pool, beforeIdx: 3, sink, labelOpts: null);

        Assert.Equal(0, pool.Count);
    }

    [Fact]
    public void LockedLabel_ShowsLockSuffix_WhenToggleOn()
    {
        var pivots = BuildActiveSwings(2);
        pivots.SetFlag(0, -2);
        pivots.SetFlagBeforeLock(0, 1);

        var sink = new CollectSink();
        var opts = new PivotLabelRenderOptions
        {
            ShowLockedSwings = true,
            ShowActiveSwings = true,
            BarIndex = 100,
            LookbackBars = 200,
        };

        PivotLabelEngine.EmitLabelUpdate(pivots, 0, sink, opts);

        var lbl = sink.Commands.Single(c => c.Kind == DrawingCommandKind.AddLabel);
        Assert.Contains("(lock)", lbl.Text);
        Assert.StartsWith("ACTIVE", lbl.Text);
    }

    [Fact]
    public void LockedLabel_Hidden_WhenShowLockedSwingsFalse()
    {
        var pivots = BuildActiveSwings(1);
        pivots.SetFlag(0, -2);
        pivots.SetFlagBeforeLock(0, 1);

        var sink = new CollectSink();
        var opts = new PivotLabelRenderOptions
        {
            ShowLockedSwings = false,
            BarIndex = 100,
            LookbackBars = 200,
        };

        PivotLabelEngine.EmitLabelUpdate(pivots, 0, sink, opts);

        var lbl = sink.Commands.Single(c => c.Kind == DrawingCommandKind.AddLabel);
        Assert.Equal("", lbl.Text);
    }

    [Fact]
    public void DefaultLockSwingCount_MatchesPine50()
    {
        Assert.Equal(50, MainPromotionEngine.DefaultLockSwingCount);
    }
}
