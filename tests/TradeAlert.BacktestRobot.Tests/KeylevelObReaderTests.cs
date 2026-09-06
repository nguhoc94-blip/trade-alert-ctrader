using System;
using TradeAlert.BacktestRobot.Execution;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models;
using TradeAlert.Core.Models.KeyLevel;
using TradeAlert.Core.Series;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests;

public class KeylevelObReaderTests
{
    static SwingBResult SellB(int bar, double keyTop, double keyBottom, int pivotIdx = 0) => new()
    {
        PivotIndex = pivotIdx,
        PivotBar = bar,
        Type = SwingBResolver.TypeHigh,
        KeyTop = keyTop,
        KeyBottom = keyBottom,
        Source = SwingBSource.Active,
    };

    static SwingBResult BuyB(int bar, double keyTop, double keyBottom, int pivotIdx = 0) => new()
    {
        PivotIndex = pivotIdx,
        PivotBar = bar,
        Type = SwingBResolver.TypeLow,
        KeyTop = keyTop,
        KeyBottom = keyBottom,
        Source = SwingBSource.Active,
    };

    static void AddOb(PineStateEngine s, int owner, int type, double top, double bottom)
    {
        s.ObPool.Push(state: 2, count: 1, x: 0, bar: 10, typ: type, owner: owner,
            source: 0, number: 1, pivotType: type, pivotHighId: 1, pivotLowId: 1);
        var i = s.ObPool.Count - 1;
        s.ObPool.SetExtending(i, true);
        s.ObPool.SetBox(i, new ObBoxSpec { Top = top, Bottom = bottom, ObType = type });
    }

    static SeriesBuffer BuildM15Buffer(int barCount)
    {
        var buf = new SeriesBuffer();
        for (var i = 0; i < barCount; i++)
        {
            buf.Upsert(i, new BarSnapshot(
                OpenChartTimeLocal: new DateTime(2020, 1, 1).AddMinutes(i * 15),
                Open: 100, High: 101, Low: 99, Close: 100.5), default);
        }
        return buf;
    }

    static SeriesBuffer BuildM5Buffer(int barCount)
    {
        var buf = new SeriesBuffer();
        for (var i = 0; i < barCount; i++)
        {
            buf.Upsert(i, new BarSnapshot(
                OpenChartTimeLocal: new DateTime(2020, 1, 1).AddMinutes(i * 5),
                Open: 100, High: 101, Low: 99, Close: 100.5), default);
        }
        return buf;
    }

    [Fact]
    public void ResolveEntry_NoC_LegacyFurthestM15Ob()
    {
        var s = new PineStateEngine();
        AddOb(s, owner: 0, SwingBResolver.TypeLow, top: 100.8, bottom: 99.5);
        AddOb(s, owner: 0, SwingBResolver.TypeLow, top: 100.5, bottom: 99.5);

        var pick = KeylevelObReader.ResolveEntry(
            s, BuyB(50, keyTop: 100, keyBottom: 99), isBuy: true,
            swingCReference: null, obFallbackState: s, crossTf: null);

        Assert.Equal(100.8, pick.Entry, 6);
        Assert.True(pick.FromOb);
    }

    [Fact]
    public void ResolveEntry_WithC_PicksM15ObNearestToC()
    {
        var s = new PineStateEngine();
        AddOb(s, owner: 0, SwingBResolver.TypeLow, top: 100.8, bottom: 99.5);
        AddOb(s, owner: 0, SwingBResolver.TypeLow, top: 100.4, bottom: 99.5);

        var pick = KeylevelObReader.ResolveEntry(
            s, BuyB(50, keyTop: 100, keyBottom: 99), isBuy: true,
            swingCReference: 104, obFallbackState: s, crossTf: null);

        Assert.Equal(100.8, pick.Entry, 6);
        Assert.True(pick.FromOb);
    }

    [Fact]
    public void ResolveEntry_WithC_PicksKeyBoxWhenNearestToC()
    {
        var s = new PineStateEngine();
        AddOb(s, owner: 0, SwingBResolver.TypeLow, top: 100.8, bottom: 99.5);

        var pick = KeylevelObReader.ResolveEntry(
            s, BuyB(50, keyTop: 100, keyBottom: 99), isBuy: true,
            swingCReference: 100.5, obFallbackState: s, crossTf: null);

        Assert.Equal(100, pick.Entry, 6);
        Assert.False(pick.FromOb);
    }

    [Fact]
    public void ResolveEntry_NoC_M15B_IncludesM5OverlapOb()
    {
        var m15 = new PineStateEngine();
        var crossTf = new SwingCEdgeCrossTfContext
        {
            M5State = new PineStateEngine(),
            M5Buffer = BuildM5Buffer(100),
            ChartBuffer = BuildM15Buffer(100),
            ChartTfToken = "15",
        };
        AddOb(m15, owner: 0, SwingBResolver.TypeLow, top: 100.4, bottom: 99.5);
        AddOb(crossTf.M5State, owner: 99, SwingBResolver.TypeLow, top: 100.9, bottom: 99.5);

        var pick = KeylevelObReader.ResolveEntry(
            m15, BuyB(50, keyTop: 100, keyBottom: 99), isBuy: true,
            swingCReference: null, obFallbackState: m15, crossTf);

        Assert.Equal(100.9, pick.Entry, 6);
        Assert.True(pick.FromOb);
    }

    [Fact]
    public void ResolveEntry_WithC_PicksM5ObWhenCloserToCThanM15()
    {
        var m15 = new PineStateEngine();
        var bIdx = m15.Pivots.PushPivot(99.5, 50, SwingBResolver.TypeLow, 0, 1, 1);
        m15.Pivots.SetFlag(bIdx, SwingBResolver.FlagActive);
        m15.Pivots.SetHasKey(bIdx, true);
        m15.Pivots.SetKeyBox(bIdx, new KeyBoxRef
        {
            Spec = new KeyBoxSpec { Top = 100, Bottom = 99 },
        });
        AddOb(m15, owner: 0, SwingBResolver.TypeLow, top: 100.4, bottom: 99.5);

        var m5 = new PineStateEngine();
        var m5BIdx = m5.Pivots.PushPivot(99.5, 150, SwingBResolver.TypeLow, 0, 1, 1);
        m5.Pivots.SetFlag(m5BIdx, SwingBResolver.FlagActive);
        m5.Pivots.SetHasKey(m5BIdx, true);
        m5.Pivots.SetKeyBox(m5BIdx, new KeyBoxRef
        {
            Spec = new KeyBoxSpec { Top = 100, Bottom = 99 },
        });
        AddOb(m5, m5BIdx, SwingBResolver.TypeLow, top: 100.7, bottom: 99.5);

        var crossTf = new SwingCEdgeCrossTfContext
        {
            M5State = m5,
            M5Buffer = BuildM5Buffer(250),
            ChartBuffer = BuildM15Buffer(250),
            ChartTfToken = "15",
        };

        var pick = KeylevelObReader.ResolveEntry(
            m15, BuyB(50, keyTop: 100, keyBottom: 99, pivotIdx: bIdx), isBuy: true,
            swingCReference: 104, obFallbackState: m15, crossTf);

        Assert.Equal(100.7, pick.Entry, 6);
        Assert.True(pick.FromOb);
    }

    [Fact]
    public void ResolveEntry_WithC_ObPick_UsesObBoxForNearBZone()
    {
        var s = new PineStateEngine();
        AddOb(s, owner: 0, SwingBResolver.TypeLow, top: 100.8, bottom: 99.6);

        var pick = KeylevelObReader.ResolveEntry(
            s, BuyB(50, keyTop: 100, keyBottom: 99), isBuy: true,
            swingCReference: 104, obFallbackState: s, crossTf: null);

        Assert.Equal(100.8, pick.Entry, 6);
        Assert.True(pick.FromOb);
        Assert.Equal(99.6, pick.NearBZoneLow, 6);
        Assert.Equal(100.8, pick.NearBZoneHigh, 6);
    }

    [Fact]
    public void ResolveEntry_KeyPick_UsesKeyBoxForNearBZone()
    {
        var s = new PineStateEngine();
        AddOb(s, owner: 0, SwingBResolver.TypeLow, top: 100.8, bottom: 99.5);

        var pick = KeylevelObReader.ResolveEntry(
            s, BuyB(50, keyTop: 100, keyBottom: 99), isBuy: true,
            swingCReference: 100.5, obFallbackState: s, crossTf: null);

        Assert.Equal(100, pick.Entry, 6);
        Assert.False(pick.FromOb);
        Assert.Equal(99, pick.NearBZoneLow, 6);
        Assert.Equal(100, pick.NearBZoneHigh, 6);
    }

    [Fact]
    public void ResolveEntry_Sell_WithC_PicksObNearestToC()
    {
        var s = new PineStateEngine();
        AddOb(s, owner: 0, SwingBResolver.TypeHigh, top: 100.5, bottom: 98.8);
        AddOb(s, owner: 0, SwingBResolver.TypeHigh, top: 100.5, bottom: 98.5);

        var pick = KeylevelObReader.ResolveEntry(
            s, new SwingBResult
            {
                PivotBar = 50,
                Type = SwingBResolver.TypeHigh,
                KeyTop = 100,
                KeyBottom = 99,
            },
            isBuy: false,
            swingCReference: 96,
            obFallbackState: s,
            crossTf: null);

        Assert.Equal(98.5, pick.Entry, 6);
        Assert.True(pick.FromOb);
    }

    [Fact]
    public void ResolveEntry_Sell_ObPick_UsesObBoxForNearBZone()
    {
        var s = new PineStateEngine();
        AddOb(s, owner: 0, SwingBResolver.TypeHigh, top: 100.4, bottom: 98.2);

        var pick = KeylevelObReader.ResolveEntry(
            s, SellB(50, keyTop: 100, keyBottom: 99), isBuy: false,
            swingCReference: 96, obFallbackState: s, crossTf: null);

        Assert.Equal(98.2, pick.Entry, 6);
        Assert.True(pick.FromOb);
        Assert.Equal(98.2, pick.NearBZoneLow, 6);
        Assert.Equal(100.4, pick.NearBZoneHigh, 6);
    }

    [Fact]
    public void ResolveEntry_Sell_KeyPick_UsesKeyBoxForNearBZone()
    {
        var s = new PineStateEngine();
        AddOb(s, owner: 0, SwingBResolver.TypeHigh, top: 100.5, bottom: 98.0);

        var pick = KeylevelObReader.ResolveEntry(
            s, SellB(50, keyTop: 100, keyBottom: 99), isBuy: false,
            swingCReference: 98.5, obFallbackState: s, crossTf: null);

        Assert.Equal(99, pick.Entry, 6);
        Assert.False(pick.FromOb);
        Assert.Equal(99, pick.NearBZoneLow, 6);
        Assert.Equal(100, pick.NearBZoneHigh, 6);
    }

    [Fact]
    public void ResolveEntry_Sell_NoC_LegacyFurthestM15Ob()
    {
        var s = new PineStateEngine();
        AddOb(s, owner: 0, SwingBResolver.TypeHigh, top: 100.5, bottom: 98.6);
        AddOb(s, owner: 0, SwingBResolver.TypeHigh, top: 100.5, bottom: 98.3);

        var pick = KeylevelObReader.ResolveEntry(
            s, SellB(50, keyTop: 100, keyBottom: 99), isBuy: false,
            swingCReference: null, obFallbackState: s, crossTf: null);

        Assert.Equal(98.3, pick.Entry, 6);
        Assert.True(pick.FromOb);
        Assert.Equal(98.3, pick.NearBZoneLow, 6);
        Assert.Equal(100.5, pick.NearBZoneHigh, 6);
    }
}
