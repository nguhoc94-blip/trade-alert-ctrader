using System;
using System.Collections.Generic;
using TradeAlert.BacktestRobot.Execution;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models;
using TradeAlert.Core.Models.Alerts;
using TradeAlert.Core.Models.KeyLevel;
using TradeAlert.Core.Series;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests;

public class SwingCEdgeTakeProfitTests
{
    static void AddPivot(
        PineStateEngine s,
        int type,
        int bar,
        double price,
        double? keyTop = null,
        double? keyBottom = null,
        int flag = 1,
        bool brokenWaitingD = false)
    {
        var idx = s.Pivots.PushPivot(price, bar, type, 0, 1, 1);
        s.Pivots.SetFlag(idx, flag);
        if (brokenWaitingD)
            s.Pivots.SetFirstDIdx(idx, PivotStateStore.FirstDWaiting);
        if (keyTop is not null && keyBottom is not null)
        {
            s.Pivots.SetHasKey(idx, true);
            s.Pivots.SetKeyBox(idx, new KeyBoxRef
            {
                Spec = new KeyBoxSpec { Top = keyTop.Value, Bottom = keyBottom.Value },
            });
        }
    }

    static void AddObOnPivot(PineStateEngine s, int pivotIdx, int type, double top, double bottom,
        int state = 2, bool extending = true)
    {
        s.ObPool.Push(state: state, count: 1, x: 0, bar: 10, typ: type, owner: pivotIdx,
            source: 0, number: 1, pivotType: type, pivotHighId: 1, pivotLowId: 1);
        var i = s.ObPool.Count - 1;
        s.ObPool.SetExtending(i, extending);
        s.ObPool.SetBox(i, new ObBoxSpec { Top = top, Bottom = bottom, ObType = type });
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

    static SwingCEdgeCrossTfContext CrossTf(PineStateEngine m5, SeriesBuffer m5Buf, SeriesBuffer m15Buf) => new()
    {
        M5State = m5,
        M5Buffer = m5Buf,
        ChartBuffer = m15Buf,
        ChartTfToken = "15",
    };

    static TradeMapperConfig SwingCCfg() => new()
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
        AssetType = TradeAlert.BacktestRobot.Execution.Kl.KlAssetType.XAUUSD,
        CustomContractSize = 100,
        IsCryptoSymbol = false,
        QuoteCurrency = "USD",
        BaseAssetName = "XAU",
        ConvUsdPerQuote = 1,
        LabelPrefix = "L6BT|",
        UseSwingCEdgeTakeProfit = true,
        SwingCEdgeTpFallbackToRR = false,
        SwingCEdgeTpRuleSlots = new HashSet<int> { 0, 1, 2, 3, 4, 5 },
        AtrSlWidthAdjust = new AtrSlWidthAdjustConfig { UseAtrAdjustedSlWidthMultiplier = false },
    };

    static CompoundFireEvent Fire(SignalDirection dir, int slot = 0) =>
        new(slot, $"R{slot + 1}", dir, "15", 100, System.DateTime.UnixEpoch, 100, 100);

    [Fact]
    public void Resolver_Buy_UsesSwingCKeyBottom()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);
        AddPivot(s, SwingBResolver.TypeHigh, 70, 105, keyTop: 106, keyBottom: 104);

        var result = SwingCEdgeTakeProfitResolver.TryResolve(s, isBuy: true, swingBBar: 50, entry: 100, out var reason);

        Assert.NotNull(result);
        Assert.Equal("ok", reason);
        Assert.Equal(70, result!.PivotBar);
        Assert.Equal(104, result.TakeProfit, 6);
    }

    [Fact]
    public void Resolver_Sell_UsesSwingCKeyTop()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeHigh, 50, 100.5, keyTop: 101, keyBottom: 100);
        AddPivot(s, SwingBResolver.TypeLow, 70, 95, keyTop: 96, keyBottom: 94);

        var result = SwingCEdgeTakeProfitResolver.TryResolve(s, isBuy: false, swingBBar: 50, entry: 100, out _);

        Assert.NotNull(result);
        Assert.Equal(96, result!.TakeProfit, 6);
    }

    [Fact]
    public void Resolver_Buy_PicksObBottomWhenCloserToEntryThanKeyBottom()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);
        AddPivot(s, SwingBResolver.TypeHigh, 70, 105, keyTop: 106, keyBottom: 104);
        AddObOnPivot(s, pivotIdx: 1, SwingBResolver.TypeHigh, top: 105.5, bottom: 103);

        var result = SwingCEdgeTakeProfitResolver.TryResolve(s, isBuy: true, swingBBar: 50, entry: 100, out _);

        Assert.NotNull(result);
        Assert.Equal(103, result!.TakeProfit, 6);
        Assert.Equal("ObZin", result.EdgeKind);
        Assert.True(result.IsOb);
        Assert.Equal(106, result.EdgeTop, 6);
        Assert.Equal(104, result.EdgeBottom, 6);
    }

    [Fact]
    public void Resolver_Buy_PicksKeyBottomWhenCloserToEntryThanObBottom()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);
        AddPivot(s, SwingBResolver.TypeHigh, 70, 105, keyTop: 106, keyBottom: 104);
        AddObOnPivot(s, pivotIdx: 1, SwingBResolver.TypeHigh, top: 106.5, bottom: 104.5);

        var result = SwingCEdgeTakeProfitResolver.TryResolve(s, isBuy: true, swingBBar: 50, entry: 100, out _);

        Assert.NotNull(result);
        Assert.Equal(104, result!.TakeProfit, 6);
        Assert.Equal("KeyBox", result.EdgeKind);
        Assert.False(result.IsOb);
    }

    [Fact]
    public void Resolver_Sell_PicksObTopWhenCloserToEntryThanKeyTop()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeHigh, 50, 100.5, keyTop: 101, keyBottom: 100);
        AddPivot(s, SwingBResolver.TypeLow, 70, 95, keyTop: 96, keyBottom: 94);
        AddObOnPivot(s, pivotIdx: 1, SwingBResolver.TypeLow, top: 97, bottom: 95.5);

        var result = SwingCEdgeTakeProfitResolver.TryResolve(s, isBuy: false, swingBBar: 50, entry: 100, out _);

        Assert.NotNull(result);
        Assert.Equal(97, result!.TakeProfit, 6);
        Assert.Equal("ObZin", result.EdgeKind);
        Assert.True(result.IsOb);
    }

    [Fact]
    public void Resolver_Sell_PicksKeyTopWhenCloserToEntryThanObTop()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeHigh, 50, 100.5, keyTop: 101, keyBottom: 100);
        AddPivot(s, SwingBResolver.TypeLow, 70, 95, keyTop: 96, keyBottom: 94);
        AddObOnPivot(s, pivotIdx: 1, SwingBResolver.TypeLow, top: 95.5, bottom: 94.5);

        var result = SwingCEdgeTakeProfitResolver.TryResolve(s, isBuy: false, swingBBar: 50, entry: 100, out _);

        Assert.NotNull(result);
        Assert.Equal(96, result!.TakeProfit, 6);
        Assert.Equal("KeyBox", result.EdgeKind);
        Assert.False(result.IsOb);
    }

    [Fact]
    public void Resolver_Buy_RejectsTpBelowEntry()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);
        AddPivot(s, SwingBResolver.TypeHigh, 70, 101, keyTop: 101.5, keyBottom: 99.5);

        var result = SwingCEdgeTakeProfitResolver.TryResolve(s, isBuy: true, swingBBar: 50, entry: 100, out var reason);

        Assert.Null(result);
        Assert.Contains("not above entry", reason);
    }

    [Fact]
    public void Mapper_Buy_UsesSwingCTpWhenEnabled()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);
        // D_SWING C so B stays the most recent ACTIVE swing for Rule 1.
        AddPivot(s, SwingBResolver.TypeHigh, 70, 105, keyTop: 106, keyBottom: 104, flag: SwingCEdgeTakeProfitResolver.FlagDSwing);

        var plan = CompoundFireTradeMapper.TryBuild(Fire(SignalDirection.Buy), s, SwingCCfg(), out var reason);

        Assert.NotNull(plan);
        Assert.Equal("ok", reason);
        Assert.Equal(TakeProfitSource.SwingCEdge, plan!.TakeProfitSource);
        Assert.Equal(104, plan.TakeProfit, 6);
        Assert.Equal(70, plan.SwingCPivotBar);
    }

    [Fact]
    public void Mapper_SkipsWhenNoSwingCAndNoFallback()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);

        var plan = CompoundFireTradeMapper.TryBuild(Fire(SignalDirection.Buy), s, SwingCCfg(), out var reason);

        Assert.Null(plan);
        Assert.Contains("swing-C TP", reason);
    }

    [Fact]
    public void Mapper_FallbackToRR_WhenConfigured()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);

        var cfg = SwingCCfg() with { SwingCEdgeTpFallbackToRR = true };
        var plan = CompoundFireTradeMapper.TryBuild(Fire(SignalDirection.Buy), s, cfg, out _);

        Assert.NotNull(plan);
        Assert.Equal(TakeProfitSource.RewardRisk, plan!.TakeProfitSource);
        Assert.Equal(104.0, plan.TakeProfit, 6);
    }

    [Fact]
    public void ApplyMinRrFloor_KeepsEdgeWhenRrMeetsBase()
    {
        var tp = SwingCEdgeTakeProfitResolver.ApplyMinRrFloor(
            edgeTp: 104, entry: 100, sl: 98, isBuy: true, minRewardRisk: 2.0);

        Assert.Equal(104, tp, 6);
    }

    [Fact]
    public void ApplyMinRrFloor_BumpsToBaseRrWhenEdgeTooClose()
    {
        var tp = SwingCEdgeTakeProfitResolver.ApplyMinRrFloor(
            edgeTp: 101, entry: 100, sl: 98, isBuy: true, minRewardRisk: 2.0);

        Assert.Equal(104, tp, 6);
    }

    [Fact]
    public void Mapper_Buy_BumpsSwingCTpToBaseRrWhenEdgeTooClose()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);
        AddPivot(s, SwingBResolver.TypeHigh, 70, 101.5, keyTop: 102, keyBottom: 101, flag: SwingCEdgeTakeProfitResolver.FlagDSwing);

        var plan = CompoundFireTradeMapper.TryBuild(Fire(SignalDirection.Buy), s, SwingCCfg(), out var reason);

        Assert.NotNull(plan);
        Assert.Equal("ok", reason);
        Assert.Equal(TakeProfitSource.SwingCEdge, plan!.TakeProfitSource);
        Assert.Equal(104, plan.TakeProfit, 6);
    }

    [Fact]
    public void Mapper_SkipsWhenSwingCRrBelowBaseAndSkipEnabled()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);
        AddPivot(s, SwingBResolver.TypeHigh, 70, 101.5, keyTop: 102, keyBottom: 101, flag: SwingCEdgeTakeProfitResolver.FlagDSwing);

        var cfg = SwingCCfg() with { SkipIfSwingCRrBelowBase = true };
        var plan = CompoundFireTradeMapper.TryBuild(Fire(SignalDirection.Buy), s, cfg, out var reason);

        Assert.Null(plan);
        Assert.Contains("swing-C RR", reason);
        Assert.Contains("< base", reason);
    }

    [Fact]
    public void TryBuildPlans_SplitTpAtCAndD_SkipsAllLegsWhenSwingCRrBelowBase()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);
        AddPivot(s, SwingBResolver.TypeHigh, 70, 101.5, keyTop: 102, keyBottom: 101, flag: SwingCEdgeTakeProfitResolver.FlagDSwing);
        AddDPivot(s, 90, keyTop: 112, keyBottom: 108);

        var cfg = SwingCCfg() with
        {
            SwingCTpMode = SwingCTpMode.SplitTpAtCAndD,
            SkipIfSwingCRrBelowBase = true,
        };
        var plans = CompoundFireTradeMapper.TryBuildPlans(Fire(SignalDirection.Buy), s, cfg, out var reason);

        Assert.Empty(plans);
        Assert.Contains("swing-C RR", reason);
    }

    [Fact]
    public void TryResolveSwingEdgeTp_SkipMode_RejectsSubBaseRr()
    {
        var ok = SwingCEdgeTakeProfitResolver.TryResolveSwingEdgeTp(
            edgeTp: 101, entry: 100, sl: 98, isBuy: true, minRewardRisk: 2.0,
            skipIfBelowBase: true, out var tp, out var reject);

        Assert.False(ok);
        Assert.NotNull(reject);
        Assert.Contains("swing-C RR", reject);
    }

    [Fact]
    public void TryResolveSwingEdgeTp_SkipMode_KeepsEdgeWhenRrMeetsBase()
    {
        var ok = SwingCEdgeTakeProfitResolver.TryResolveSwingEdgeTp(
            edgeTp: 104, entry: 100, sl: 98, isBuy: true, minRewardRisk: 2.0,
            skipIfBelowBase: true, out var tp, out var reject);

        Assert.True(ok);
        Assert.Null(reject);
        Assert.Equal(104, tp, 6);
    }

    static void AddDPivot(PineStateEngine s, int bar, double keyTop, double keyBottom)
    {
        var idx = s.Pivots.PushPivot((keyTop + keyBottom) / 2, bar, SwingBResolver.TypeHigh, 0, 1, 1);
        s.Pivots.SetFlag(idx, 2);
        s.Pivots.SetHasKey(idx, true);
        s.Pivots.SetKeyBox(idx, new KeyBoxRef
        {
            Spec = new KeyBoxSpec { Top = keyTop, Bottom = keyBottom },
        });
    }

    [Fact]
    public void TryBuildPlans_SplitTpAtCAndD_TwoLegsHalfRiskEach()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);
        AddPivot(s, SwingBResolver.TypeHigh, 70, 105, keyTop: 106, keyBottom: 104, flag: SwingCEdgeTakeProfitResolver.FlagDSwing);
        AddDPivot(s, 90, keyTop: 112, keyBottom: 108);

        var cfg = SwingCCfg() with { SwingCTpMode = SwingCTpMode.SplitTpAtCAndD };
        var plans = CompoundFireTradeMapper.TryBuildPlans(Fire(SignalDirection.Buy), s, cfg, out var reason);

        Assert.Equal("ok(split)", reason);
        Assert.Equal(2, plans.Count);
        Assert.Equal("C", plans[0].TpLegTag);
        Assert.Equal("D", plans[1].TpLegTag);
        Assert.Equal(104, plans[0].TakeProfit, 6);
        Assert.Equal(108, plans[1].TakeProfit, 6);
        Assert.Contains("|C", plans[0].Label);
        Assert.Contains("|D", plans[1].Label);
        Assert.Equal(new TradeDedupKey(0, 50, "C"), plans[0].DedupKey);
        Assert.Equal(new TradeDedupKey(0, 50, "D"), plans[1].DedupKey);
        Assert.Equal(70, plans[0].SwingCEntryPivotBar);
        Assert.Equal(70, plans[1].SwingCEntryPivotBar);
        Assert.Equal(70, plans[0].SwingCPivotBar);
        Assert.Equal(90, plans[1].SwingCPivotBar);
        Assert.True(plans[0].LotFtmo > 0);
        Assert.Equal(plans[0].LotFtmo, plans[1].LotFtmo);
    }

    [Fact]
    public void ShouldFallbackDLegTpToC_Buy_WhenIncrementalAtMostHalfR()
    {
        Assert.True(SwingCEdgeTakeProfitResolver.ShouldFallbackDLegTpToC(
            tpC: 104, tpDRaw: 104.8, entry: 100, sl: 98, isBuy: true, minIncrementalRr: 0.5));
        Assert.False(SwingCEdgeTakeProfitResolver.ShouldFallbackDLegTpToC(
            tpC: 104, tpDRaw: 105.1, entry: 100, sl: 98, isBuy: true, minIncrementalRr: 0.5));
    }

    [Fact]
    public void ShouldFallbackDLegTpToC_Sell_WhenIncrementalAtMostHalfR()
    {
        Assert.True(SwingCEdgeTakeProfitResolver.ShouldFallbackDLegTpToC(
            tpC: 154.917, tpDRaw: 154.886, entry: 155.305, sl: 155.369, isBuy: false, minIncrementalRr: 0.5));
        Assert.False(SwingCEdgeTakeProfitResolver.ShouldFallbackDLegTpToC(
            tpC: 154.917, tpDRaw: 154.814, entry: 155.305, sl: 155.369, isBuy: false, minIncrementalRr: 0.5));
    }

    [Fact]
    public void ShouldFallbackDLegTpToC_DisabledWhenThresholdZero()
    {
        Assert.False(SwingCEdgeTakeProfitResolver.ShouldFallbackDLegTpToC(
            tpC: 104, tpDRaw: 104.1, entry: 100, sl: 98, isBuy: true, minIncrementalRr: 0));
    }

    [Fact]
    public void TryBuildPlans_SplitTpAtCAndD_DLegFallsBackToCWhenIncrementalBelowThreshold()
    {
        var chartState = new PineStateEngine();
        AddPivot(chartState, SwingBResolver.TypeHigh, 50, 100.5, keyTop: 101, keyBottom: 100);
        AddPivot(chartState, SwingBResolver.TypeLow, 70, 96, keyTop: 96, keyBottom: 94, flag: SwingCEdgeTakeProfitResolver.FlagDSwing);

        var h1State = new PineStateEngine();
        // D zone just below C (96): incremental RR ≈ 0.25 → fallback TP D → TP C; gate still uses raw zone top 95.5.
        var h1Idx = h1State.Pivots.PushPivot(95.5, 60, -1, 0, 1, 1);
        h1State.Pivots.SetFlag(h1Idx, 1);
        h1State.Pivots.SetHasKey(h1Idx, true);
        h1State.Pivots.SetKeyExtending(h1Idx, true);
        h1State.Pivots.SetKeyBox(h1Idx, new KeyBoxRef
        {
            Spec = new KeyBoxSpec { Top = 95.5, Bottom = 94.0 },
        });

        var cfg = SwingCCfg() with
        {
            SwingCTpMode = SwingCTpMode.SplitTpAtCAndD,
            SplitDLegMinIncrementalRr = 0.5,
            DZoneStates = new Dictionary<string, PineStateEngine> { ["60"] = h1State },
        };

        var plans = CompoundFireTradeMapper.TryBuildPlans(Fire(SignalDirection.Sell), chartState, cfg, out var reason);

        Assert.Equal("ok(split)", reason);
        Assert.Equal(2, plans.Count);
        Assert.Equal(96, plans[0].TakeProfit, 6);
        Assert.Equal(96, plans[1].TakeProfit, 6);
        Assert.Contains("D-near-C", plans[1].Reason);
        // Near-D cancel gate uses raw D zone geometry, not the fallback TP.
        Assert.Equal(95.5, plans[0].NearDCancelZoneHigh, 6);
        Assert.Equal(94.0, plans[0].NearDCancelZoneLow, 6);
        Assert.Equal(plans[0].NearDCancelZoneHigh, plans[1].NearDCancelZoneHigh, 6);
        Assert.Equal(plans[0].NearDCancelZoneLow, plans[1].NearDCancelZoneLow, 6);
    }

    [Fact]
    public void TryBuildPlans_SplitTpAtCAndD_DLegFallsBackToCWhenNoD()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);
        AddPivot(s, SwingBResolver.TypeHigh, 70, 105, keyTop: 106, keyBottom: 104, flag: SwingCEdgeTakeProfitResolver.FlagDSwing);

        var cfg = SwingCCfg() with { SwingCTpMode = SwingCTpMode.SplitTpAtCAndD };
        var plans = CompoundFireTradeMapper.TryBuildPlans(Fire(SignalDirection.Buy), s, cfg, out var reason);

        Assert.Equal("ok(split,no-D)", reason);
        Assert.Equal(2, plans.Count);
        Assert.Equal(TakeProfitSource.SwingCEdge, plans[0].TakeProfitSource);
        Assert.Equal(TakeProfitSource.SwingCEdge, plans[1].TakeProfitSource);
        Assert.Equal(104, plans[0].TakeProfit, 6);
        Assert.Equal(104, plans[1].TakeProfit, 6);
        Assert.Contains("SwingC-fallback", plans[1].Reason);
    }

    [Fact]
    public void TryComputeBaseRrEntry_Buy_MovesEntryDownToReachBase()
    {
        // entry 100, sl 98, C edge 101 (RR 0.5), base 2 → e* = (101 + 2*98)/3 = 99 (== keyBottom)
        var ok = SwingCEdgeTakeProfitResolver.TryComputeBaseRrEntry(
            swingCTp: 101, sl: 98, isBuy: true, baseRewardRisk: 2.0,
            originalEntry: 100, keyTop: 100, keyBottom: 99,
            out var movedEntry, out var reason);

        Assert.True(ok);
        Assert.Equal("ok", reason);
        Assert.Equal(99, movedEntry, 6);
        // RR at moved entry == base
        Assert.Equal(2.0, SwingCEdgeTakeProfitResolver.ComputeEdgeRr(101, movedEntry, 98, isBuy: true), 6);
    }

    [Fact]
    public void TryComputeBaseRrEntry_Buy_FailsWhenBeyondKeyBottom()
    {
        // C edge 100.4 → e* = (100.4 + 196)/3 = 98.8 < keyBottom 99 → unreachable
        var ok = SwingCEdgeTakeProfitResolver.TryComputeBaseRrEntry(
            swingCTp: 100.4, sl: 98, isBuy: true, baseRewardRisk: 2.0,
            originalEntry: 100, keyTop: 100, keyBottom: 99,
            out _, out var reason);

        Assert.False(ok);
        Assert.Contains("keyBottom", reason);
    }

    [Fact]
    public void TryComputeBaseRrEntry_Sell_MovesEntryUpToReachBase()
    {
        // sell entry 100, sl 102, C edge 99 (RR 0.5), base 2 → e* = (99 + 2*102)/3 = 101 (== keyTop)
        var ok = SwingCEdgeTakeProfitResolver.TryComputeBaseRrEntry(
            swingCTp: 99, sl: 102, isBuy: false, baseRewardRisk: 2.0,
            originalEntry: 100, keyTop: 101, keyBottom: 100,
            out var movedEntry, out var reason);

        Assert.True(ok);
        Assert.Equal(101, movedEntry, 6);
        Assert.Equal(2.0, SwingCEdgeTakeProfitResolver.ComputeEdgeRr(99, movedEntry, 102, isBuy: false), 6);
    }

    [Fact]
    public void TryBuildPlans_SplitTpAtCAndD_MovesEntryWhenSwingCRrBelowBase()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);
        // C lower edge 101 → RR 0.5 at entry 100; base 2 → moved entry = 99 (keyBottom), RR 2
        AddPivot(s, SwingBResolver.TypeHigh, 70, 101.5, keyTop: 102, keyBottom: 101, flag: SwingCEdgeTakeProfitResolver.FlagDSwing);
        AddDPivot(s, 90, keyTop: 112, keyBottom: 108);

        var cfg = SwingCCfg() with
        {
            SwingCTpMode = SwingCTpMode.SplitTpAtCAndD,
            MoveEntryIfSwingCRrBelowBase = true,
        };
        var plans = CompoundFireTradeMapper.TryBuildPlans(Fire(SignalDirection.Buy), s, cfg, out _);

        Assert.Equal(2, plans.Count);
        // both legs share the moved entry and the anchored SL
        Assert.Equal(99, plans[0].EntryLimit, 6);
        Assert.Equal(99, plans[1].EntryLimit, 6);
        Assert.Equal(98, plans[0].StopLoss, 6);   // keyTop 100 - w(1)*mult(2)
        Assert.Equal(101, plans[0].TakeProfit, 6); // C edge, RR == base
        Assert.Equal(108, plans[1].TakeProfit, 6); // D edge
        Assert.Equal(plans[0].LotFtmo, plans[1].LotFtmo);
    }

    [Fact]
    public void TryBuildPlans_SplitTpAtCAndD_NoOrdersWhenMoveCannotReachBase()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);
        // C lower edge 100.4 → e* = 98.8 < keyBottom 99 → unreachable
        AddPivot(s, SwingBResolver.TypeHigh, 70, 100.45, keyTop: 100.9, keyBottom: 100.4, flag: SwingCEdgeTakeProfitResolver.FlagDSwing);
        AddDPivot(s, 90, keyTop: 112, keyBottom: 108);

        var cfg = SwingCCfg() with
        {
            SwingCTpMode = SwingCTpMode.SplitTpAtCAndD,
            MoveEntryIfSwingCRrBelowBase = true,
        };
        var plans = CompoundFireTradeMapper.TryBuildPlans(Fire(SignalDirection.Buy), s, cfg, out var reason);

        Assert.Empty(plans);
        Assert.Contains("entry move not possible", reason);
    }

    [Fact]
    public void TryResolveSplitCd_UsesFireReferenceNotCPrice_WhenProvided()
    {
        var chartState = new PineStateEngine();
        AddPivot(chartState, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);
        AddPivot(chartState, SwingBResolver.TypeHigh, 70, 105, keyTop: 106, keyBottom: 104, flag: SwingCEdgeTakeProfitResolver.FlagDSwing);

        var h1State = new PineStateEngine();
        var h1Idx = h1State.Pivots.PushPivot(102.5, 60, 1, 0, 1, 1);
        h1State.Pivots.SetFlag(h1Idx, 1);
        h1State.Pivots.SetHasKey(h1Idx, true);
        h1State.Pivots.SetKeyExtending(h1Idx, true);
        h1State.Pivots.SetKeyBox(h1Idx, new KeyBoxRef
        {
            Spec = new KeyBoxSpec { Top = 103, Bottom = 102 },
        });

        var dZoneStates = new Dictionary<string, PineStateEngine> { ["60"] = h1State };

        var okFire = SwingCEdgeTakeProfitResolver.TryResolveSplitCd(
            chartState, isBuy: true, swingBBar: 50, entry: 100,
            out _, out var dFire, out var reasonFire,
            dZoneStates, dReferencePrice: 101);

        Assert.True(okFire);
        Assert.Equal("ok(split)", reasonFire);
        Assert.NotNull(dFire);
        Assert.Equal(102, dFire!.TakeProfit, 6);

        var okC = SwingCEdgeTakeProfitResolver.TryResolveSplitCd(
            chartState, isBuy: true, swingBBar: 50, entry: 100,
            out _, out var dC, out var reasonC,
            dZoneStates);

        Assert.True(okC);
        Assert.Null(dC);
        Assert.Equal("ok(split,no-D)", reasonC);
    }

    [Fact]
    public void ResolveDFireAtFire_LocksNearDCancelZoneAtFireReference()
    {
        var chartState = new PineStateEngine();
        AddPivot(chartState, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);
        AddPivot(chartState, SwingBResolver.TypeHigh, 70, 105, keyTop: 106, keyBottom: 104, flag: SwingCEdgeTakeProfitResolver.FlagDSwing);

        var h1State = new PineStateEngine();
        var h1Idx = h1State.Pivots.PushPivot(102.5, 60, 1, 0, 1, 1);
        h1State.Pivots.SetFlag(h1Idx, 1);
        h1State.Pivots.SetHasKey(h1Idx, true);
        h1State.Pivots.SetKeyExtending(h1Idx, true);
        h1State.Pivots.SetKeyBox(h1Idx, new KeyBoxRef
        {
            Spec = new KeyBoxSpec { Top = 103, Bottom = 102 },
        });

        var cfg = SwingCCfg() with
        {
            SwingCTpMode = SwingCTpMode.SplitTpAtCAndD,
            DZoneStates = new Dictionary<string, PineStateEngine> { ["60"] = h1State },
        };

        var b = new SwingBResult
        {
            PivotBar = 50,
            Type = SwingBResolver.TypeLow,
            KeyTop = 100,
            KeyBottom = 99,
            Source = SwingBSource.Active,
        };

        var cache = CompoundFireTradeMapper.ResolveDFireAtFire(
            chartState, in cfg, Fire(SignalDirection.Buy), b, fireBarHigh: 101, fireBarLow: 99);

        Assert.NotNull(cache.DFire);
        Assert.Equal(101, cache.ReferencePrice, 6);
        Assert.NotNull(cache.NearDCancelZone);
        Assert.Equal(103, cache.NearDCancelZone!.EdgeTop, 6);
        Assert.Equal(102, cache.NearDCancelZone.EdgeBottom, 6);
    }

    [Fact]
    public void TryResolveSplitCd_DZoneStates_PrefersNearestM5ZoneOverH1()
    {
        var chartState = new PineStateEngine();
        AddPivot(chartState, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);
        AddPivot(chartState, SwingBResolver.TypeHigh, 70, 105, keyTop: 106, keyBottom: 104, flag: SwingCEdgeTakeProfitResolver.FlagDSwing);

        var m5State = new PineStateEngine();
        var m5Idx = m5State.Pivots.PushPivot(107, 80, 1, 0, 1, 1);
        m5State.Pivots.SetFlag(m5Idx, 1);
        m5State.Pivots.SetHasKey(m5Idx, true);
        m5State.Pivots.SetKeyExtending(m5Idx, true);
        m5State.Pivots.SetKeyBox(m5Idx, new KeyBoxRef
        {
            Spec = new KeyBoxSpec { Top = 108, Bottom = 107 },
        });

        var h1State = new PineStateEngine();
        var h1Idx = h1State.Pivots.PushPivot(111, 60, 1, 0, 1, 1);
        h1State.Pivots.SetFlag(h1Idx, 1);
        h1State.Pivots.SetHasKey(h1Idx, true);
        h1State.Pivots.SetKeyExtending(h1Idx, true);
        h1State.Pivots.SetKeyBox(h1Idx, new KeyBoxRef
        {
            Spec = new KeyBoxSpec { Top = 112, Bottom = 110 },
        });

        var dZoneStates = new Dictionary<string, PineStateEngine>
        {
            ["5"]  = m5State,
            ["60"] = h1State,
        };

        var ok = SwingCEdgeTakeProfitResolver.TryResolveSplitCd(
            chartState, isBuy: true, swingBBar: 50, entry: 100,
            out _, out var dResult, out var reason,
            dZoneStates);

        Assert.True(ok);
        Assert.Equal("ok(split)", reason);
        Assert.NotNull(dResult);
        Assert.Equal(107, dResult!.TakeProfit, 6);
        Assert.Equal("5", dResult.TfToken);
    }

    [Fact]
    public void TryResolveSplitCd_DZoneStates_FindsNearestRedZoneAboveCForBuy()
    {
        // Chart state: B at bar 50 (LOW), C at bar 70 (HIGH, key 104-106)
        var chartState = new PineStateEngine();
        AddPivot(chartState, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);
        AddPivot(chartState, SwingBResolver.TypeHigh, 70, 105, keyTop: 106, keyBottom: 104, flag: SwingCEdgeTakeProfitResolver.FlagDSwing);

        // H1 state: one RED zone above C (110-112) → D candidate
        var h1State = new PineStateEngine();
        // Active KeyLevel HIGH pivot with key box 110-112 (Red zone above C's price 105)
        var h1Idx = h1State.Pivots.PushPivot(111, 60, 1 /* HIGH */, 0, 1, 1);
        h1State.Pivots.SetFlag(h1Idx, 1 /* ACTIVE */);
        h1State.Pivots.SetHasKey(h1Idx, true);
        h1State.Pivots.SetKeyExtending(h1Idx, true);
        h1State.Pivots.SetKeyBox(h1Idx, new KeyBoxRef
        {
            Spec = new KeyBoxSpec { Top = 112, Bottom = 110 },
        });

        // H4 state: another RED zone further away (120-122)
        var h4State = new PineStateEngine();
        var h4Idx = h4State.Pivots.PushPivot(121, 30, 1, 0, 1, 1);
        h4State.Pivots.SetFlag(h4Idx, 1);
        h4State.Pivots.SetHasKey(h4Idx, true);
        h4State.Pivots.SetKeyExtending(h4Idx, true);
        h4State.Pivots.SetKeyBox(h4Idx, new KeyBoxRef
        {
            Spec = new KeyBoxSpec { Top = 122, Bottom = 120 },
        });

        var dZoneStates = new Dictionary<string, PineStateEngine>
        {
            ["60"]  = h1State,
            ["240"] = h4State,
        };

        var ok = SwingCEdgeTakeProfitResolver.TryResolveSplitCd(
            chartState, isBuy: true, swingBBar: 50, entry: 100,
            out var cResult, out var dResult, out var reason,
            dZoneStates);

        Assert.True(ok);
        Assert.Equal("ok(split)", reason);
        Assert.NotNull(dResult);
        // D TP = bottom of nearest red zone above C (110), not the farther H4 zone (120)
        Assert.Equal(110, dResult!.TakeProfit, 6);
        Assert.Equal("DZone", dResult.EdgeKind);
    }

    [Fact]
    public void TryResolveSplitCd_DZoneStates_NoDZoneWhenNoRedZoneAboveC()
    {
        var chartState = new PineStateEngine();
        AddPivot(chartState, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);
        AddPivot(chartState, SwingBResolver.TypeHigh, 70, 105, keyTop: 106, keyBottom: 104, flag: SwingCEdgeTakeProfitResolver.FlagDSwing);

        // H1 state with only a GREEN zone (support) — wrong color for BUY D
        var h1State = new PineStateEngine();
        var h1Idx = h1State.Pivots.PushPivot(97, 60, -1 /* LOW */, 0, 1, 1);
        h1State.Pivots.SetFlag(h1Idx, 1);
        h1State.Pivots.SetHasKey(h1Idx, true);
        h1State.Pivots.SetKeyExtending(h1Idx, true);
        h1State.Pivots.SetKeyBox(h1Idx, new KeyBoxRef
        {
            Spec = new KeyBoxSpec { Top = 98, Bottom = 96 },
        });

        var dZoneStates = new Dictionary<string, PineStateEngine> { ["60"] = h1State };

        var ok = SwingCEdgeTakeProfitResolver.TryResolveSplitCd(
            chartState, isBuy: true, swingBBar: 50, entry: 100,
            out _, out var dResult, out var reason,
            dZoneStates);

        Assert.True(ok);
        Assert.Null(dResult);
        Assert.Equal("ok(split,no-D)", reason);
    }

    [Fact]
    public void Resolver_Buy_PicksM5ObZinWhenCloserThanKeyAndM15Ob()
    {
        var m15 = new PineStateEngine();
        AddPivot(m15, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);
        AddPivot(m15, SwingBResolver.TypeHigh, 70, 105, keyTop: 106, keyBottom: 104,
            flag: SwingCEdgeTakeProfitResolver.FlagDSwing);
        AddObOnPivot(m15, pivotIdx: 1, SwingBResolver.TypeHigh, top: 105.2, bottom: 104.6);

        var m5 = new PineStateEngine();
        var m5CIdx = m5.Pivots.PushPivot(105, 211, SwingBResolver.TypeHigh, 0, 1, 1);
        m5.Pivots.SetFlag(m5CIdx, SwingBResolver.FlagActive);
        AddObOnPivot(m5, m5CIdx, SwingBResolver.TypeHigh, top: 104.5, bottom: 103);

        var m15Buf = BuildM15Buffer(250);
        var m5Buf = BuildM5Buffer(250);
        var crossTf = CrossTf(m5, m5Buf, m15Buf);

        var result = SwingCEdgeTakeProfitResolver.TryResolve(
            m15, isBuy: true, swingBBar: 50, entry: 100, out _, crossTf: crossTf);

        Assert.NotNull(result);
        Assert.Equal(103, result!.TakeProfit, 6);
        Assert.Equal("M5ObZin", result.EdgeKind);
        Assert.True(result.IsOb);
    }

    [Fact]
    public void Resolver_Buy_PicksM15ObZinOverM5WhenM15ObIsCloser()
    {
        var m15 = new PineStateEngine();
        AddPivot(m15, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);
        AddPivot(m15, SwingBResolver.TypeHigh, 70, 105, keyTop: 106, keyBottom: 104,
            flag: SwingCEdgeTakeProfitResolver.FlagDSwing);
        AddObOnPivot(m15, pivotIdx: 1, SwingBResolver.TypeHigh, top: 105.2, bottom: 103.5);

        var m5 = new PineStateEngine();
        var m5CIdx = m5.Pivots.PushPivot(105, 211, SwingBResolver.TypeHigh, 0, 1, 1);
        m5.Pivots.SetFlag(m5CIdx, SwingBResolver.FlagActive);
        AddObOnPivot(m5, m5CIdx, SwingBResolver.TypeHigh, top: 104.5, bottom: 103.8);

        var crossTf = CrossTf(m5, BuildM5Buffer(250), BuildM15Buffer(250));

        var result = SwingCEdgeTakeProfitResolver.TryResolve(
            m15, isBuy: true, swingBBar: 50, entry: 100, out _, crossTf: crossTf);

        Assert.NotNull(result);
        Assert.Equal(103.5, result!.TakeProfit, 6);
        Assert.Equal("ObZin", result.EdgeKind);
    }

    [Fact]
    public void Resolver_IgnoresNonZinOrNonExtendingOb()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);
        AddPivot(s, SwingBResolver.TypeHigh, 70, 105, keyTop: 106, keyBottom: 104);
        AddObOnPivot(s, pivotIdx: 1, SwingBResolver.TypeHigh, top: 103.5, bottom: 102.5, state: 1);
        AddObOnPivot(s, pivotIdx: 1, SwingBResolver.TypeHigh, top: 103.2, bottom: 102.2, state: 2, extending: false);

        var result = SwingCEdgeTakeProfitResolver.TryResolve(s, isBuy: true, swingBBar: 50, entry: 100, out _);

        Assert.NotNull(result);
        Assert.Equal(104, result!.TakeProfit, 6);
        Assert.Equal("KeyBox", result.EdgeKind);
    }

    [Fact]
    public void Mapper_Buy_UsesM5ObZinViaCrossTfContext()
    {
        var m15 = new PineStateEngine();
        AddPivot(m15, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);
        AddPivot(m15, SwingBResolver.TypeHigh, 70, 105, keyTop: 106, keyBottom: 104,
            flag: SwingCEdgeTakeProfitResolver.FlagDSwing);

        var m5 = new PineStateEngine();
        var m5CIdx = m5.Pivots.PushPivot(105, 211, SwingBResolver.TypeHigh, 0, 1, 1);
        m5.Pivots.SetFlag(m5CIdx, SwingBResolver.FlagActive);
        AddObOnPivot(m5, m5CIdx, SwingBResolver.TypeHigh, top: 104.5, bottom: 103);

        var cfg = SwingCCfg() with
        {
            RewardRisk = 1.0,
            ChartTfToken = "15",
            M5FallbackState = m5,
            M5FallbackBuffer = BuildM5Buffer(250),
            ChartSeriesBuffer = BuildM15Buffer(250),
        };

        // R3+ keeps M15 structure; M5 OB is merged via cross-TF context on the M15 C pivot.
        var plan = CompoundFireTradeMapper.TryBuild(Fire(SignalDirection.Buy, slot: 2), m15, cfg, out var reason);

        Assert.NotNull(plan);
        Assert.Equal("ok", reason);
        Assert.Equal(103, plan!.TakeProfit, 6);
        Assert.False(plan.UsesM5SwingC);
    }

    [Fact]
    public void ResolveDPrimeFireZone_SkipsM5_PicksH1()
    {
        var m5State = new PineStateEngine();
        var m5Idx = m5State.Pivots.PushPivot(107, 80, 1, 0, 1, 1);
        m5State.Pivots.SetFlag(m5Idx, 1);
        m5State.Pivots.SetHasKey(m5Idx, true);
        m5State.Pivots.SetKeyExtending(m5Idx, true);
        m5State.Pivots.SetKeyBox(m5Idx, new KeyBoxRef
        {
            Spec = new KeyBoxSpec { Top = 108, Bottom = 107 },
        });

        var h1State = new PineStateEngine();
        var h1Idx = h1State.Pivots.PushPivot(111, 60, 1, 0, 1, 1);
        h1State.Pivots.SetFlag(h1Idx, 1);
        h1State.Pivots.SetHasKey(h1Idx, true);
        h1State.Pivots.SetKeyExtending(h1Idx, true);
        h1State.Pivots.SetKeyBox(h1Idx, new KeyBoxRef
        {
            Spec = new KeyBoxSpec { Top = 112, Bottom = 110 },
        });

        var dPrimeStates = new Dictionary<string, PineStateEngine> { ["60"] = h1State };
        var fullStates = new Dictionary<string, PineStateEngine>
        {
            ["5"] = m5State,
            ["60"] = h1State,
        };

        var d = SwingCEdgeTakeProfitResolver.ResolveDFireZone(fullStates, null, 70, 100, isBuy: true, entry: 100);
        var dPrime = SwingCEdgeTakeProfitResolver.ResolveDPrimeFireZone(dPrimeStates, null, 70, 100, isBuy: true, entry: 100);

        Assert.NotNull(d);
        Assert.Equal(107, d!.TakeProfit, 6);
        Assert.Equal("5", d.TfToken);
        Assert.NotNull(dPrime);
        Assert.Equal(110, dPrime!.TakeProfit, 6);
        Assert.Equal("DPrimeZone", dPrime.EdgeKind);
        Assert.Equal("60", dPrime.TfToken);
    }

    [Fact]
    public void TryBuildPlans_GongLoi_R5_TpDFromDPrime_NoDPrimeFallsBackToD()
    {
        var chartState = new PineStateEngine();
        AddPivot(chartState, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);
        AddPivot(chartState, SwingBResolver.TypeHigh, 70, 105, keyTop: 106, keyBottom: 104,
            flag: SwingCEdgeTakeProfitResolver.FlagDSwing);

        var m5State = new PineStateEngine();
        var m5Idx = m5State.Pivots.PushPivot(107, 80, 1, 0, 1, 1);
        m5State.Pivots.SetFlag(m5Idx, 1);
        m5State.Pivots.SetHasKey(m5Idx, true);
        m5State.Pivots.SetKeyExtending(m5Idx, true);
        m5State.Pivots.SetKeyBox(m5Idx, new KeyBoxRef
        {
            Spec = new KeyBoxSpec { Top = 108, Bottom = 107 },
        });

        var h1State = new PineStateEngine();
        var h1Idx = h1State.Pivots.PushPivot(111, 60, 1, 0, 1, 1);
        h1State.Pivots.SetFlag(h1Idx, 1);
        h1State.Pivots.SetHasKey(h1Idx, true);
        h1State.Pivots.SetKeyExtending(h1Idx, true);
        h1State.Pivots.SetKeyBox(h1Idx, new KeyBoxRef
        {
            Spec = new KeyBoxSpec { Top = 112, Bottom = 110 },
        });

        var dZoneStates = new Dictionary<string, PineStateEngine>
        {
            ["5"] = m5State,
            ["60"] = h1State,
        };
        var dPrimeStates = new Dictionary<string, PineStateEngine> { ["60"] = h1State };

        var cfgPrime = SwingCCfg() with
        {
            SwingCTpMode = SwingCTpMode.SplitTpAtCAndD,
            SplitDLegMinIncrementalRr = 0,
            EnableGongLoiTpD = true,
            DZoneStates = dZoneStates,
            DPrimeZoneStates = dPrimeStates,
        };

        var plansPrime = CompoundFireTradeMapper.TryBuildPlans(
            Fire(SignalDirection.Buy, slot: 4), chartState, cfgPrime, out var reasonPrime,
            fireBarHigh: 100);

        Assert.Equal("ok(split)", reasonPrime);
        Assert.Equal(2, plansPrime.Count);
        Assert.Equal(110, plansPrime[1].TakeProfit, 6);
        Assert.Equal(110, plansPrime[1].SwingCEdgeBottom, 6);

        var cfgNoPrime = cfgPrime with { DPrimeZoneStates = new Dictionary<string, PineStateEngine>() };
        var plansFallback = CompoundFireTradeMapper.TryBuildPlans(
            Fire(SignalDirection.Buy, slot: 4), chartState, cfgNoPrime, out var reasonFallback,
            fireBarHigh: 100);

        Assert.Equal("ok(split)", reasonFallback);
        Assert.Equal(107, plansFallback[1].TakeProfit, 6);
    }

    [Fact]
    public void TryBuildPlans_GongLoi_R1_StillUsesM5D()
    {
        var chartState = new PineStateEngine();
        AddPivot(chartState, SwingBResolver.TypeLow, 50, 99.5, keyTop: 100, keyBottom: 99);
        AddPivot(chartState, SwingBResolver.TypeHigh, 70, 105, keyTop: 106, keyBottom: 104,
            flag: SwingCEdgeTakeProfitResolver.FlagDSwing);

        var m5State = new PineStateEngine();
        var m5Idx = m5State.Pivots.PushPivot(107, 80, 1, 0, 1, 1);
        m5State.Pivots.SetFlag(m5Idx, 1);
        m5State.Pivots.SetHasKey(m5Idx, true);
        m5State.Pivots.SetKeyExtending(m5Idx, true);
        m5State.Pivots.SetKeyBox(m5Idx, new KeyBoxRef
        {
            Spec = new KeyBoxSpec { Top = 108, Bottom = 107 },
        });

        var h1State = new PineStateEngine();
        var h1Idx = h1State.Pivots.PushPivot(111, 60, 1, 0, 1, 1);
        h1State.Pivots.SetFlag(h1Idx, 1);
        h1State.Pivots.SetHasKey(h1Idx, true);
        h1State.Pivots.SetKeyExtending(h1Idx, true);
        h1State.Pivots.SetKeyBox(h1Idx, new KeyBoxRef
        {
            Spec = new KeyBoxSpec { Top = 112, Bottom = 110 },
        });

        var cfg = SwingCCfg() with
        {
            SwingCTpMode = SwingCTpMode.SplitTpAtCAndD,
            SplitDLegMinIncrementalRr = 0,
            EnableGongLoiTpD = true,
            DZoneStates = new Dictionary<string, PineStateEngine>
            {
                ["5"] = m5State,
                ["60"] = h1State,
            },
            DPrimeZoneStates = new Dictionary<string, PineStateEngine> { ["60"] = h1State },
        };

        var plans = CompoundFireTradeMapper.TryBuildPlans(
            Fire(SignalDirection.Buy, slot: 0), chartState, cfg, out var reason,
            fireBarHigh: 100);

        Assert.Equal("ok(split)", reason);
        Assert.Equal(107, plans[1].TakeProfit, 6);
    }
}
