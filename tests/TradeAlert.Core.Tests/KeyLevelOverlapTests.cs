using System.Collections.Generic;
using TradeAlert.Core.Drawing;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.KeyLevel;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class KeyLevelOverlapTests
{
    [Fact]
    public void OverlapSoftHide_KeepsTwoNewestInTransitiveGroup()
    {
        var pivots = new PivotStateStore();
        var cmds = new List<DrawingCommand>();
        var sink = new ListSink(cmds);

        static void AddActive(PivotStateStore p, int bar, double top, double bot)
        {
            var idx = p.PushPivot(bar + 0.5, bar, 1, 0, 1, 0);
            p.SetFlag(idx, 1);
            p.SetHasKey(idx, true);
            p.SetKeyVisible(idx, true);
            p.SetKeyBox(idx, new KeyBoxRef
            {
                Spec = new KeyBoxSpec
                {
                    LeftTimeChartLocal = new DateTime(2026, 1, 1, 0, bar, 0, DateTimeKind.Unspecified),
                    RightTimeChartLocal = new DateTime(2026, 1, 1, 0, bar, 0, DateTimeKind.Unspecified),
                    Top = top,
                    Bottom = bot,
                    ExtendRight = true,
                },
            });
        }

        AddActive(pivots, 10, 105, 100);
        AddActive(pivots, 20, 104, 99);
        AddActive(pivots, 30, 103, 98);

        KeyLevelOverlapEngine.LimitOverlappingKeylevelsSoft(pivots, maxKeep: 2, sink);

        Assert.Equal(1, cmds.Count);
        Assert.False(pivots.GetKeyVisible(0));
        Assert.Null(pivots.GetKeyBox(0));
        Assert.True(pivots.GetHasKey(0));
        Assert.True(pivots.GetKeyVisible(1));
        Assert.True(pivots.GetKeyVisible(2));
    }

    sealed class ListSink : IDrawingCommandSink
    {
        readonly List<DrawingCommand> _cmds;
        public ListSink(List<DrawingCommand> cmds) => _cmds = cmds;
        public void Enqueue(in DrawingCommand cmd) => _cmds.Add(cmd);
    }
}
