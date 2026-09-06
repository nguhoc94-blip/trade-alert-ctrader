using System;

using TradeAlert.BacktestRobot.Execution;

using TradeAlert.BacktestRobot.Execution.Kl;

using TradeAlert.Core.Engines;

using TradeAlert.Core.Models;

using TradeAlert.Core.Models.Alerts;

using TradeAlert.Core.Models.KeyLevel;

using TradeAlert.Core.Series;

using Xunit;



namespace TradeAlert.BacktestRobot.Tests;



public class SwingBM5AnchorTests

{

    static void AddActiveBPivot(PineStateEngine s, int type, int bar, double keyTop, double keyBottom)

    {

        var idx = s.Pivots.PushPivot(keyBottom, bar, type, 0, 1, 1);

        s.Pivots.SetFlag(idx, SwingBResolver.FlagActive);

        s.Pivots.SetHasKey(idx, true);

        s.Pivots.SetKeyBox(idx, new KeyBoxRef { Spec = new KeyBoxSpec { Top = keyTop, Bottom = keyBottom } });

    }



    static void AddMainCBPivot(PineStateEngine s, int type, int bar, double keyTop, double keyBottom)

    {

        var idx = s.Pivots.PushPivot(keyBottom, bar, type, 0, 1, 1);

        s.Pivots.SetFlag(idx, SwingBResolver.FlagMainC);

        s.Pivots.SetMainRole(idx, 1);

        s.Pivots.SetHasKey(idx, true);

        s.Pivots.SetKeyExtending(idx, true);

        s.Pivots.SetKeyBox(idx, new KeyBoxRef { Spec = new KeyBoxSpec { Top = keyTop, Bottom = keyBottom } });

    }



    static SeriesBuffer BuildM15Buffer(int barCount)

    {

        var buf = new SeriesBuffer();

        for (var i = 0; i < barCount; i++)

        {

            var snap = new BarSnapshot(

                OpenChartTimeLocal: new DateTime(2020, 1, 1).AddMinutes(i * 15),

                Open: 100, High: 101, Low: 99, Close: 100.5);

            buf.Upsert(i, snap, default);

        }

        return buf;

    }



    static SeriesBuffer BuildM5Buffer(int barCount)

    {

        var buf = new SeriesBuffer();

        for (var i = 0; i < barCount; i++)

        {

            var snap = new BarSnapshot(

                OpenChartTimeLocal: new DateTime(2020, 1, 1).AddMinutes(i * 5),

                Open: 100, High: 101, Low: 99, Close: 100.5);

            buf.Upsert(i, snap, default);

        }

        return buf;

    }



    static TradeMapperConfig Cfg(PineStateEngine? m5, SeriesBuffer? m5Buf, SeriesBuffer? m15Buf) => new()

    {

        SymbolName = "XAUUSD",

        TickSize = 0.01,

        PipSize = 0.01,

        SpreadPips = 0,

        AccountBalanceFtmo = 100_000,

        RiskPercent = 1,

        RoundPrecision = 1000,

        RewardRisk = 2.0,

        SlWidthMult = 2,

        AssetType = KlAssetType.XAUUSD,

        CustomContractSize = 100,

        IsCryptoSymbol = false,

        QuoteCurrency = "USD",

        BaseAssetName = "XAU",

        ConvUsdPerQuote = 1,

        LabelPrefix = "L6BT|",

        ChartTfToken = "15",

        M5FallbackState = m5,

        M5FallbackBuffer = m5Buf,

        EnableM5SwingBFallback = true,

        ChartSeriesBuffer = m15Buf,

        UseSwingCEdgeTakeProfit = false,

        AtrSlWidthAdjust = new AtrSlWidthAdjustConfig { UseAtrAdjustedSlWidthMultiplier = false },

    };



    [Fact]

    public void TryAnchorM5PivotBar_MapsToContainingM15Bar()

    {

        var m5Buf = BuildM5Buffer(200);

        var m15Buf = BuildM15Buffer(80);

        const int m5Bar = 120; // 10:00

        const int expectedM15Bar = 40; // 40*15min = 10:00



        var ok = SwingBResolver.TryAnchorM5PivotBarToChart(m5Bar, m5Buf, m15Buf, "15", out var chartBar);



        Assert.True(ok);

        Assert.Equal(expectedM15Bar, chartBar);

    }



    [Fact]

    public void ResolveSwingB_R2_UsesM5ZoneWhenM15HasNoB()

    {

        var m15 = new PineStateEngine();

        AddActiveBPivot(m15, SwingBResolver.TypeHigh, 50, 101, 100); // wrong type for BUY



        var m5 = new PineStateEngine();

        const int m5Bar = 120;

        AddActiveBPivot(m5, SwingBResolver.TypeLow, m5Bar, 100, 99);



        var cfg = Cfg(m5, BuildM5Buffer(200), BuildM15Buffer(80));

        var ev = new CompoundFireEvent(1, "R2", SignalDirection.Buy, "15", 80, DateTime.UnixEpoch, 100, 80);



        var b = CompoundFireTradeMapper.ResolveSwingB(m15, isBuy: true, ev.SlotIndex, in cfg, out var reason);



        Assert.NotNull(b);

        Assert.Equal(SwingBSource.M5AnchoredFallback, b!.Source);

        Assert.Equal(40, b.PivotBar);

        Assert.Equal(99, b.KeyBottom, 6);

        Assert.Equal(100, b.KeyTop, 6);

        Assert.Contains("M5 B@120 anchored", reason);

    }



    [Fact]

    public void TryBuild_R2_PlanFromM5AnchoredB()

    {

        var m15 = new PineStateEngine();

        AddActiveBPivot(m15, SwingBResolver.TypeHigh, 50, 101, 100);



        var m5 = new PineStateEngine();

        AddActiveBPivot(m5, SwingBResolver.TypeLow, 120, 100, 99);



        var cfg = Cfg(m5, BuildM5Buffer(200), BuildM15Buffer(80));

        var ev = new CompoundFireEvent(1, "R2", SignalDirection.Buy, "15", 80, DateTime.UnixEpoch, 100, 80);



        var plan = CompoundFireTradeMapper.TryBuild(ev, m15, cfg, out var reason);



        Assert.NotNull(plan);

        Assert.Contains("ok", reason);

        Assert.Equal(100, plan!.EntryLimit, 6);

        Assert.Equal(40, plan.SwingBPivotBar);

        Assert.True(plan.UsesM5SwingC);

        Assert.True(plan.SwingBFromM5Anchor);

        Assert.Equal(120, plan.StructureSwingBBar);

        Assert.Contains("|B40", plan.Label);

        Assert.Contains("[B=M5→M15]", plan.Reason);

    }



    [Fact]

    public void ResolveSwingB_R3_DoesNotUseM5Fallback()

    {

        var m15 = new PineStateEngine();

        AddActiveBPivot(m15, SwingBResolver.TypeHigh, 50, 101, 100);



        var m5 = new PineStateEngine();

        AddActiveBPivot(m5, SwingBResolver.TypeLow, 120, 100, 99);



        var cfg = Cfg(m5, BuildM5Buffer(200), BuildM15Buffer(80));

        var ev = new CompoundFireEvent(2, "R3", SignalDirection.Buy, "15", 80, DateTime.UnixEpoch, 100, 80);



        var b = CompoundFireTradeMapper.ResolveSwingB(m15, isBuy: true, ev.SlotIndex, in cfg, out var reason);



        Assert.Null(b);

        Assert.Contains("not swing LOW", reason);

    }



    [Fact]

    public void ResolveSwingB_R2_PrefersM15BWhenPresent()

    {

        var m15 = new PineStateEngine();

        AddActiveBPivot(m15, SwingBResolver.TypeLow, 55, 100, 99);



        var m5 = new PineStateEngine();

        AddActiveBPivot(m5, SwingBResolver.TypeLow, 120, 100.5, 99.5);



        var cfg = Cfg(m5, BuildM5Buffer(200), BuildM15Buffer(80));

        var b = CompoundFireTradeMapper.ResolveSwingB(m15, isBuy: true, slotIndex: 1, in cfg, out _);



        Assert.NotNull(b);

        Assert.Equal(SwingBSource.Active, b!.Source);

        Assert.Equal(55, b.PivotBar);

    }



    [Fact]

    public void TryBuild_R2_WithM15B_UsesM5SwingCButNotM5Anchor()

    {

        var m15 = new PineStateEngine();

        AddActiveBPivot(m15, SwingBResolver.TypeLow, 55, 100, 99);



        var m5 = new PineStateEngine();

        AddActiveBPivot(m5, SwingBResolver.TypeLow, 120, 100.5, 99.5);



        var cfg = Cfg(m5, BuildM5Buffer(200), BuildM15Buffer(80));

        var plan = CompoundFireTradeMapper.TryBuild(

            new CompoundFireEvent(1, "R2", SignalDirection.Buy, "15", 80, DateTime.UnixEpoch, 100, 80),

            m15, cfg, out _);



        Assert.NotNull(plan);

        Assert.True(plan!.UsesM5SwingC);

        Assert.False(plan.SwingBFromM5Anchor);

        Assert.Equal(55, plan.SwingBPivotBar);

        Assert.True(plan.StructureSwingBBar > 0);

    }



    [Fact]

    public void ResolveSwingB_R2_SkipsM5FallbackWhenDisabled()

    {

        var m15 = new PineStateEngine();

        AddActiveBPivot(m15, SwingBResolver.TypeHigh, 50, 101, 100);



        var m5 = new PineStateEngine();

        AddActiveBPivot(m5, SwingBResolver.TypeLow, 120, 100, 99);



        var cfg = Cfg(m5, BuildM5Buffer(200), BuildM15Buffer(80)) with { EnableM5SwingBFallback = false };

        var b = CompoundFireTradeMapper.ResolveSwingB(m15, isBuy: true, slotIndex: 1, in cfg, out var reason);



        Assert.Null(b);

        Assert.Contains("not swing LOW", reason);

    }



    [Fact]

    public void ResolveSwingB_R2_UsesM5MainCWhenNoValidActiveOnM5()

    {

        var m15 = new PineStateEngine();

        AddActiveBPivot(m15, SwingBResolver.TypeHigh, 50, 101, 100);



        var m5 = new PineStateEngine();

        // Most recent ACTIVE is wrong direction — MAIN_C fallback still runs.

        AddActiveBPivot(m5, SwingBResolver.TypeHigh, 200, 101, 100);

        const int m5MainCBar = 120;

        AddMainCBPivot(m5, SwingBResolver.TypeLow, m5MainCBar, 100, 99);



        var cfg = Cfg(m5, BuildM5Buffer(200), BuildM15Buffer(80));

        var b = CompoundFireTradeMapper.ResolveSwingB(m15, isBuy: true, slotIndex: 1, in cfg, out var reason);



        Assert.NotNull(b);

        Assert.Equal(SwingBSource.M5AnchoredFallback, b!.Source);

        Assert.True(b.M5AnchoredMainC);

        Assert.Equal(40, b.PivotBar);

        Assert.Contains("MAIN_C", reason);



        var plan = CompoundFireTradeMapper.TryBuild(

            new CompoundFireEvent(1, "R2", SignalDirection.Buy, "15", 80, DateTime.UnixEpoch, 100, 80),

            m15, cfg, out _);

        Assert.NotNull(plan);

        Assert.Contains("[B=M5→M15,MAIN_C]", plan!.Reason);

    }

}


