using TradeAlert.Core.Drawing;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.KeyLevel;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class KeyLevelEngineTests
{
    [Fact]
    public void HasClearBodyRaw_WithoutAtrRule_ComparesToAvgBody()
    {
        var ok = KeyLevelEngine.HasClearBodyRaw(
            0,
            avgB: 2.0,
            minBodyMult: 1.0,
            useAtrRule: false,
            atrLen: 14,
            atrMult: 1.0,
            _ => (10, 12, 9, 12),
            atrValueWhenRuleEnabled: null);
        Assert.True(ok);
    }

    [Fact]
    public void HasClearBodyRaw_WithAtrRule_MissingAtr_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => KeyLevelEngine.HasClearBodyRaw(
            0,
            100.0,
            1.0,
            useAtrRule: true,
            atrLen: 14,
            atrMult: 1.0,
            _ => (10, 12, 9, 11),
            atrValueWhenRuleEnabled: null));
    }

    [Fact]
    public void HasClearBodyRaw_WithAtrRule_UsesInjectedAtr()
    {
        var ok = KeyLevelEngine.HasClearBodyRaw(
            0,
            avgB: 100.0,
            minBodyMult: 1.0,
            useAtrRule: true,
            atrLen: 14,
            atrMult: 1.0,
            _ => (10, 14, 9, 13),
            atrValueWhenRuleEnabled: 2.0);
        Assert.True(ok);
    }

    [Fact]
    public void EffectiveKeylevelLookback_GapMerge_ZeroLikePine()
    {
        Assert.Equal(0, KeyLevelEngine.EffectiveKeylevelLookback(isGapMerge: true, useGapMerge: true, keylevelLookback: 2));
        Assert.Equal(2, KeyLevelEngine.EffectiveKeylevelLookback(isGapMerge: false, useGapMerge: true, keylevelLookback: 2));
        Assert.Equal(2, KeyLevelEngine.EffectiveKeylevelLookback(isGapMerge: true, useGapMerge: false, keylevelLookback: 2));
    }

    [Fact]
    public void FindReference_CandleFromPivot_LookbackZero_OnlyScansPivotBar()
    {
        (double o, double h, double l, double c) Ohlc(int lag) => lag switch
        {
            0 => (1.10, 1.12, 1.08, 1.11), // pivot: green, no clear red here
            1 => (1.20, 1.22, 1.18, 1.19), // would be picked if lookback>=1
            _ => (1, 1, 1, 1),
        };

        var pickWide = KeyLevelEngine.FindReferenceCandleFromPivot(
            barIndex: 5, pivotBar: 5, "L", lookback: 2,
            avgB: 0.001, minBodyMult: 1.0, useAtrRule: false,
            atrLen: 14, atrMult: 1.0, Ohlc, null);

        var pickGap = KeyLevelEngine.FindReferenceCandleFromPivot(
            barIndex: 5, pivotBar: 5, "L", lookback: 0,
            avgB: 0.001, minBodyMult: 1.0, useAtrRule: false,
            atrLen: 14, atrMult: 1.0, Ohlc, null);

        Assert.Equal(1, pickWide.BestLag);
        Assert.Equal(0, pickGap.BestLag);
        Assert.Equal(1.11, pickGap.BodyHigh, 6);
    }

    [Fact]
    public void FindReference_CandleFromPivot_WhenNoCandidate_UsesPivotLagLikePine()
    {
        var pick = KeyLevelEngine.FindReferenceCandleFromPivot(
            barIndex: 5,
            pivotBar: 5,
            "H",
            lookback: 2,
            avgB: 10.0,
            minBodyMult: 1.0,
            useAtrRule: false,
            atrLen: 14,
            atrMult: 1.0,
            _ => (12, 13, 9, 10),
            null);
        Assert.Equal(0, pick.BestLag);
    }

    [Fact]
    public void BuildKeylevelBox_RejectNonChartLocalTime()
    {
        var utc = DateTime.SpecifyKind(new DateTime(2026, 1, 1, 0, 0, 0), DateTimeKind.Utc);
        Assert.Throws<ArgumentException>(() => KeyLevelEngine.BuildKeylevelBox(
            barIndex: 10,
            pivotBar: 8,
            "H",
            pivotPrice: 100,
            refLag: 2,
            refBodyLen: 1.0,
            searchRangeBars: 100,
            atrLen: 14,
            atrValue: 1.0,
            highColorArgb: 0xFF0000,
            lowColorArgb: 0x0000FF,
            opacity: 80,
            minTick: 0.01,
            lag => (100, 100, 99, 100),
            _ => utc));
    }

    [Fact]
    public void BuildKeylevelBox_OutsideSearchRange_ReturnsNull()
    {
        var t = DateTime.SpecifyKind(new DateTime(2026, 1, 1, 12, 0, 0), DateTimeKind.Unspecified);
        var spec = KeyLevelEngine.BuildKeylevelBox(
            barIndex: 100,
            pivotBar: 10,
            "H",
            pivotPrice: 100,
            refLag: 10,
            refBodyLen: 1.0,
            searchRangeBars: 5,
            atrLen: 14,
            atrValue: 1.0,
            highColorArgb: 1,
            lowColorArgb: 2,
            opacity: 50,
            minTick: 0.01,
            lag => (99, 101, 98, 100),
            _ => t);
        Assert.Null(spec);
    }

    [Fact]
    public void BuildKeylevelBox_EnqueuesDrawingCommand_WhenSinkProvided()
    {
        var t = DateTime.SpecifyKind(new DateTime(2026, 1, 1, 12, 0, 0), DateTimeKind.Unspecified);
        var cmds = new List<DrawingCommand>();
        var sink = new ListSink(cmds);
        var spec = KeyLevelEngine.BuildKeylevelBox(
            barIndex: 10,
            pivotBar: 8,
            "H",
            pivotPrice: 100,
            refLag: 2,
            refBodyLen: 1.0,
            searchRangeBars: 100,
            atrLen: 14,
            atrValue: 1.0,
            highColorArgb: 1,
            lowColorArgb: 2,
            opacity: 50,
            minTick: 0.1,
            lag => (99, 101, 98, 100),
            _ => t,
            sink);
        Assert.NotNull(spec);
        Assert.Single(cmds);
        Assert.Equal(DrawingCommandKind.EnqueueKeyBoxSpec, cmds[0].Kind);
        Assert.Same(spec, cmds[0].KeyBoxSpec);
    }

    [Fact]
    public void IsValidObCandle_StrongBody()
    {
        var ok = KeyLevelEngine.IsValidObCandle(
            0,
            minBodyRatio: 0.5,
            maxDojiBodyRatio: 0.15,
            _ => (10, 14, 9, 13));
        Assert.True(ok);
    }

    sealed class ListSink : IDrawingCommandSink
    {
        readonly List<DrawingCommand> _list;
        public ListSink(List<DrawingCommand> list) => _list = list;
        public void Enqueue(in DrawingCommand command) => _list.Add(command);
    }
}
