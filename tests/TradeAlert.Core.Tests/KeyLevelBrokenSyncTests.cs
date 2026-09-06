using System.Collections.Generic;
using TradeAlert.Core.Drawing;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.KeyLevel;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class KeyLevelBrokenSyncTests
{
    sealed class CollectSink : IDrawingCommandSink
    {
        public readonly List<DrawingCommand> Commands = new();
        public void Enqueue(in DrawingCommand cmd) => Commands.Add(cmd);
    }

    [Fact]
    public void SyncKeyboxWithFlag_BrokenAfterSoftHide_RecreatesBoxAndAppliesBrokenStyle()
    {
        var pivots = new PivotStateStore();
        pivots.PushDefaults(1.0, 10, 1, 0);
        pivots.SetHasKey(0, true);
        pivots.SetKeyVisible(0, false);
        pivots.SetFlag(0, 2);

        var sink = new CollectSink();
        var ctx = new KeyLevelTickContext
        {
            BarIndex = 20,
            AvgBody = 0.01,
            AtrValue = 0.01,
            TickSize = 0.0001,
            OhlcAtLag = _ => (1.0, 1.1, 0.9, 1.05),
            ChartTimeAtLag = _ => new DateTime(2024, 1, 1),
        };

        KeyLevelSyncEngine.SyncKeyboxWithFlag(0, pivots, ctx, sink);

        Assert.NotNull(pivots.GetKeyBox(0));
        Assert.True(pivots.GetKeyVisible(0));
        Assert.Contains(sink.Commands, c => c.Kind == DrawingCommandKind.EnqueueKeyBoxSpec);
        var brokenUpdate = Assert.Single(sink.Commands, c => c.Kind == DrawingCommandKind.UpdateKeyBox && c.IsDashed);
        Assert.Equal(KeyLevelVisual.BrokenBorderThickness, brokenUpdate.BorderThickness);
        Assert.Equal(KeyLevelVisual.BrokenBorderArgb, brokenUpdate.BorderColorArgb);
    }

    [Fact]
    public void SyncKeyboxWithFlag_MainCBreakPending_UsesYellowMainCStyle()
    {
        var pivots = new PivotStateStore();
        pivots.PushDefaults(1.0, 10, 1, 0);
        pivots.SetHasKey(0, true);
        pivots.SetKeyVisible(0, true);
        pivots.SetKeyExtending(0, true);
        pivots.SetOriginalFlag(0, 0);
        pivots.SetFlag(0, 3);
        pivots.SetKeyBox(0, new KeyBoxRef
        {
            Spec = new KeyBoxSpec
            {
                LeftTimeChartLocal = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
                RightTimeChartLocal = new DateTime(2024, 1, 1, 1, 0, 0, DateTimeKind.Unspecified),
                Top = 2, Bottom = 1, ExtendRight = true,
            },
        });

        var sink = new CollectSink();
        var ctx = new KeyLevelTickContext { BarIndex = 20, AvgBody = 0.01, AtrValue = 0.01, TickSize = 0.0001,
            OhlcAtLag = _ => (1.0, 1.1, 0.9, 1.05), ChartTimeAtLag = _ => new DateTime(2024, 1, 1) };

        KeyLevelSyncEngine.SyncKeyboxWithFlag(0, pivots, ctx, sink);

        var upd = Assert.Single(sink.Commands, c => c.Kind == DrawingCommandKind.UpdateKeyBox);
        Assert.True(upd.IsMainC);
        Assert.Equal(KeyLevelVisual.MainCColors().FillArgb, upd.ColorArgb);
    }

    [Fact]
    public void SyncKeyboxWithFlag_ActiveBreakPending_UsesActiveStyle()
    {
        var pivots = new PivotStateStore();
        pivots.PushDefaults(1.0, 10, 1, 0);
        pivots.SetHasKey(0, true);
        pivots.SetKeyVisible(0, true);
        pivots.SetOriginalFlag(0, 1);
        pivots.SetFlag(0, 3);
        pivots.SetKeyBox(0, new KeyBoxRef
        {
            Spec = new KeyBoxSpec
            {
                LeftTimeChartLocal = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
                RightTimeChartLocal = new DateTime(2024, 1, 1, 1, 0, 0, DateTimeKind.Unspecified),
                Top = 2, Bottom = 1,
            },
        });

        var sink = new CollectSink();
        var ctx = new KeyLevelTickContext { BarIndex = 20, AvgBody = 0.01, AtrValue = 0.01, TickSize = 0.0001,
            OhlcAtLag = _ => (1.0, 1.1, 0.9, 1.05), ChartTimeAtLag = _ => new DateTime(2024, 1, 1) };

        KeyLevelSyncEngine.SyncKeyboxWithFlag(0, pivots, ctx, sink);

        var upd = Assert.Single(sink.Commands, c => c.Kind == DrawingCommandKind.UpdateKeyBox);
        Assert.False(upd.IsMainC);
        Assert.False(upd.IsDashed);
    }

    [Fact]
    public void ResolveKeyStyleFlag_PendingUsesOriginalFlag()
    {
        var pivots = new PivotStateStore();
        pivots.PushDefaults(1.0, 10, 1, 0);
        pivots.SetOriginalFlag(0, 0);
        pivots.SetFlag(0, 3);
        Assert.Equal(0, KeyLevelSyncEngine.ResolveKeyStyleFlag(pivots, 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void BrokenColors_UseGrayBorder_NotActiveTint(int pivotType)
    {
        var (_, border) = KeyLevelVisual.BrokenColors(pivotType);
        Assert.Equal(KeyLevelVisual.BrokenBorderArgb, border);
        Assert.NotEqual(PineColors.KeyHighBorder, border);
        Assert.NotEqual(PineColors.KeyLowBorder, border);
    }
}
