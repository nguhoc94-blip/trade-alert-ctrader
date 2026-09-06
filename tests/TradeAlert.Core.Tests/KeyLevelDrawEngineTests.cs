using TradeAlert.Core.Drawing;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models;
using TradeAlert.Core.Models.KeyLevel;
using TradeAlert.Core.Series;
using TradeAlert.Indicator;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class KeyLevelDrawEngineTests
{
    static BarSnapshot Bar(double open, double high, double low, double close) =>
        new(DateTime.SpecifyKind(new DateTime(2026, 1, 1, 12, 0, 0), DateTimeKind.Unspecified),
            open, high, low, close, 1);

    [Fact]
    public void ResolveKeyBreakCheck_SkipsLiveFormingMidBar()
    {
        var series = new SeriesBuffer();
        series.Upsert(10, Bar(1.1, 1.11, 1.09, 1.105), Loop6BarRuntimeFlags.ForLiveRealtimeFormingLastBar(isNewBarViaOpenTime: false));

        var ctx = KeyLevelDrawEngine.ResolveKeyBreakCheck(series, 10);

        Assert.False(ctx.ShouldRun);
    }

    [Fact]
    public void ResolveKeyBreakCheck_UsesClosedBarOnConfirmed()
    {
        var series = new SeriesBuffer();
        series.Upsert(10, Bar(1.1, 1.12, 1.09, 1.115), Loop6BarRuntimeFlags.ForClosedChartBarBeforeLast());

        var ctx = KeyLevelDrawEngine.ResolveKeyBreakCheck(series, 10);

        Assert.True(ctx.ShouldRun);
        Assert.Equal(10, ctx.StopBarIndex);
        Assert.Equal(1.115, ctx.Close, 6);
    }

    [Fact]
    public void ResolveKeyBreakCheck_OnNewBar_UsesPreviousClosedBar()
    {
        var series = new SeriesBuffer();
        series.Upsert(9, Bar(1.0, 1.01, 0.99, 1.005), Loop6BarRuntimeFlags.ForClosedChartBarBeforeLast());
        series.Upsert(10, Bar(1.005, 1.006, 1.004, 1.0055), Loop6BarRuntimeFlags.ForLiveRealtimeFormingLastBar(isNewBarViaOpenTime: true));

        var ctx = KeyLevelDrawEngine.ResolveKeyBreakCheck(series, 10);

        Assert.True(ctx.ShouldRun);
        Assert.Equal(9, ctx.StopBarIndex);
        Assert.Equal(1.005, ctx.Close, 6);
    }

    [Fact]
    public void ResolveKeyBreakCheck_SkipsRealtimeAttachPhaseLastBar()
    {
        var series = new SeriesBuffer();
        series.Upsert(10, Bar(1.1, 1.11, 1.05, 1.108), Loop6BarRuntimeMapper.ForAttachHistoryPhaseLastBar());

        var ctx = KeyLevelDrawEngine.ResolveKeyBreakCheck(series, 10);

        Assert.False(ctx.ShouldRun);
    }

    [Fact]
    public void ResolveKeyBreakCheck_BackfillLastBar_StillRuns()
    {
        var series = new SeriesBuffer();
        series.Upsert(10, Bar(1.1, 1.12, 1.09, 1.115), Loop6BarRuntimeMapper.ForHistoricalBackfillRow(10, 10));

        var ctx = KeyLevelDrawEngine.ResolveKeyBreakCheck(series, 10);

        Assert.True(ctx.ShouldRun);
        Assert.Equal(10, ctx.StopBarIndex);
        Assert.Equal(1.115, ctx.Close, 6);
    }

    [Fact]
    public void ResolveKeyBreakCheck_SkipsBacktestFormingLastBar()
    {
        var series = new SeriesBuffer();
        series.Upsert(10, Bar(1.1, 1.11, 1.05, 1.108), Loop6BarRuntimeMapper.ForBacktestFormingLastBar(isNewBarViaOpenTime: false));

        var ctx = KeyLevelDrawEngine.ResolveKeyBreakCheck(series, 10);

        Assert.False(ctx.ShouldRun);
    }

    [Fact]
    public void ResolveKeyBreakCheck_BacktestIsNew_UsesPreviousClosedBar()
    {
        var series = new SeriesBuffer();
        series.Upsert(9, Bar(1.0, 1.01, 0.99, 1.005), Loop6BarRuntimeMapper.ForClosedChartBarBeforeLast());
        series.Upsert(10, Bar(1.005, 1.006, 1.004, 1.0055), Loop6BarRuntimeMapper.ForBacktestFormingLastBar(isNewBarViaOpenTime: true));

        var ctx = KeyLevelDrawEngine.ResolveKeyBreakCheck(series, 10);

        Assert.True(ctx.ShouldRun);
        Assert.Equal(9, ctx.StopBarIndex);
        Assert.Equal(1.005, ctx.Close, 6);
    }

    [Fact]
    public void ResolveKeyBreakCheck_ClosedHistoricalLastBar_UsesOhlc0()
    {
        var series = new SeriesBuffer();
        series.Upsert(10, Bar(1.1, 1.12, 1.09, 1.115), Loop6BarRuntimeMapper.ForBacktestClosedHistoricalLastBar());

        var ctx = KeyLevelDrawEngine.ResolveKeyBreakCheck(series, 10);

        Assert.True(ctx.ShouldRun);
        Assert.Equal(10, ctx.StopBarIndex);
        Assert.Equal(1.115, ctx.Close, 6);
    }

    [Fact]
    public void Tick_KeyBreakBeforeExtend_DoesNotExtendAfterStop()
    {
        var pivots = new PivotStateStore();
        var idx = pivots.PushPivot(1.0, 5, -1, 0, 1, 0);
        pivots.SetFlag(idx, 2);
        pivots.SetHasKey(idx, true);
        pivots.SetKeyVisible(idx, true);
        pivots.SetKeyExtending(idx, true);
        pivots.SetKeyBox(idx, new KeyBoxRef
        {
            Spec = new KeyBoxSpec { Top = 1.08, Bottom = 1.02, ExtendRight = true },
        });

        var sink = new ListDrawingSink();
        var ctx = new KeyLevelTickContext { BarIndex = 10, AtrValue = 0.01, MaxKeylevelKeep = 2 };
        var keyBreak = new KeyLevelDrawEngine.KeyBreakCheckContext
        {
            ShouldRun    = true,
            StopBarIndex = 10,
            Open         = 1.07,
            High         = 1.10,
            Low          = 1.06,
            Close        = 1.095,
        };

        KeyLevelDrawEngine.Tick(ctx, pivots, in keyBreak, sink);

        Assert.False(pivots.GetKeyExtending(idx));
        Assert.DoesNotContain(sink.Commands, c => c.Kind == DrawingCommandKind.ExtendKeyBox);
        Assert.Contains(sink.Commands, c => c.Kind == DrawingCommandKind.StopExtendKeyBox);
    }

    [Fact]
    public void TryStopOnKeyBreak_HighBroken_WickBelowBottom_CloseAbove_DoesNotStop()
    {
        var pivots = new PivotStateStore();
        var idx = pivots.PushPivot(1.05, 5, 1, 0, 1, 0);
        pivots.SetFlag(idx, 2);
        pivots.SetHasKey(idx, true);
        pivots.SetKeyVisible(idx, true);
        pivots.SetKeyExtending(idx, true);
        pivots.SetKeyBox(idx, new KeyBoxRef
        {
            Spec = new KeyBoxSpec { Top = 1.06, Bottom = 1.04, ExtendRight = true },
        });

        var sink = new ListDrawingSink();
        var ctx = new KeyLevelTickContext { BarIndex = 10, AtrValue = 0.001, MaxKeylevelKeep = 2 };
        var keyBreak = new KeyLevelDrawEngine.KeyBreakCheckContext
        {
            ShouldRun    = true,
            StopBarIndex = 10,
            Open         = 1.055,
            High         = 1.056,
            Low          = 1.035,
            Close        = 1.052,
        };

        KeyLevelDrawEngine.Tick(ctx, pivots, in keyBreak, sink);

        Assert.True(pivots.GetKeyExtending(idx));
        Assert.False(pivots.GetKeyBreakLabeled(idx));
    }

    [Fact]
    public void TryStopOnKeyBreak_LowBroken_WickAboveTop_CloseBelow_DoesNotStop()
    {
        var pivots = new PivotStateStore();
        var idx = pivots.PushPivot(1.05, 5, -1, 0, 1, 0);
        pivots.SetFlag(idx, 6);
        pivots.SetHasKey(idx, true);
        pivots.SetKeyVisible(idx, true);
        pivots.SetKeyExtending(idx, true);
        pivots.SetKeyBox(idx, new KeyBoxRef
        {
            Spec = new KeyBoxSpec { Top = 1.06, Bottom = 1.04, ExtendRight = true },
        });

        var sink = new ListDrawingSink();
        var ctx = new KeyLevelTickContext { BarIndex = 10, AtrValue = 0.001, MaxKeylevelKeep = 2 };
        var keyBreak = new KeyLevelDrawEngine.KeyBreakCheckContext
        {
            ShouldRun    = true,
            StopBarIndex = 10,
            Open         = 1.045,
            High         = 1.065,
            Low          = 1.044,
            Close        = 1.048,
        };

        KeyLevelDrawEngine.Tick(ctx, pivots, in keyBreak, sink);

        Assert.True(pivots.GetKeyExtending(idx));
        Assert.False(pivots.GetKeyBreakLabeled(idx));
    }

    [Fact]
    public void TryStopOnKeyBreak_OnlyOnClosedBarClose_NotFormingWick()
    {
        var pivots = new PivotStateStore();
        var idx = pivots.PushPivot(1.0, 5, -1, 0, 1, 0);
        pivots.SetFlag(idx, 2);
        pivots.SetHasKey(idx, true);
        pivots.SetKeyVisible(idx, true);
        pivots.SetKeyExtending(idx, true);
        pivots.SetKeyBox(idx, new KeyBoxRef
        {
            Spec = new KeyBoxSpec
            {
                Top = 1.08,
                Bottom = 1.02,
                ExtendRight = true,
            },
        });

        var sink = new ListDrawingSink();
        var ctx = new KeyLevelTickContext
        {
            BarIndex = 10,
            AtrValue = 0.01,
            MaxKeylevelKeep = 2,
        };

        // Forming tick: close pierces but should NOT stop (ShouldRun=false)
        KeyLevelDrawEngine.Tick(ctx, pivots, default, sink);
        Assert.True(pivots.GetKeyExtending(idx));

        // Closed bar: strong green close above top → stop
        var keyBreak = new KeyLevelDrawEngine.KeyBreakCheckContext
        {
            ShouldRun    = true,
            StopBarIndex = 10,
            Open         = 1.07,
            High         = 1.10,
            Low          = 1.06,
            Close        = 1.095,
        };
        KeyLevelDrawEngine.Tick(ctx, pivots, in keyBreak, sink);

        Assert.False(pivots.GetKeyExtending(idx));
        Assert.Equal(10, pivots.GetKeyStopBar(idx));
        Assert.True(pivots.GetKeyBreakLabeled(idx));
    }

    sealed class ListDrawingSink : IDrawingCommandSink
    {
        public List<DrawingCommand> Commands { get; } = new();
        public void Enqueue(in DrawingCommand cmd) => Commands.Add(cmd);
    }

    static class Loop6BarRuntimeFlags
    {
        public static BarRuntimeFlags ForClosedChartBarBeforeLast() =>
            new(IsFirst: false, IsLast: false, IsConfirmed: true, IsRealtime: false, IsNew: false, IsHistory: true);

        public static BarRuntimeFlags ForLiveRealtimeFormingLastBar(bool isNewBarViaOpenTime) =>
            Loop6BarRuntimeMapper.ForLiveRealtimeFormingLastBar(isNewBarViaOpenTime);
    }
}
