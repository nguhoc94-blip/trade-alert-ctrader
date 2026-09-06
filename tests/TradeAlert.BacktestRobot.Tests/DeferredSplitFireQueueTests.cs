using System;
using System.Collections.Generic;
using TradeAlert.BacktestRobot.Execution;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.Alerts;
using TradeAlert.Core.Models.KeyLevel;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests;

public class DeferredSplitFireQueueTests
{
    static DeferredSplitFire MakeItem(int slot, int swingBPivotBar, int fireBar = 100) =>
        new()
        {
            Ev = new CompoundFireEvent(slot, $"R{slot + 1}", SignalDirection.Buy, "15", fireBar, DateTime.UnixEpoch, 100, 100),
            OriginalSwingBPivotBar = swingBPivotBar,
            DeferredAtBar = fireBar,
            DeferredAtTimeUtc = DateTime.UnixEpoch,
        };

    [Fact]
    public void EnqueueAndCount_AreInSync()
    {
        var q = new DeferredSplitFireQueue();
        Assert.Equal(0, q.Count);

        q.Enqueue(MakeItem(slot: 0, swingBPivotBar: 50));
        q.Enqueue(MakeItem(slot: 1, swingBPivotBar: 60));

        Assert.Equal(2, q.Count);
        Assert.Equal(50, q[0].OriginalSwingBPivotBar);
        Assert.Equal(60, q[1].OriginalSwingBPivotBar);
    }

    [Fact]
    public void Contains_MatchesSlotAndPivot()
    {
        var q = new DeferredSplitFireQueue();
        q.Enqueue(MakeItem(slot: 2, swingBPivotBar: 99));

        Assert.True(q.Contains(slotIndex: 2, swingBPivotBar: 99));
        Assert.False(q.Contains(slotIndex: 2, swingBPivotBar: 100));
        Assert.False(q.Contains(slotIndex: 3, swingBPivotBar: 99));
    }

    [Fact]
    public void RemoveAt_ShrinksQueue()
    {
        var q = new DeferredSplitFireQueue();
        q.Enqueue(MakeItem(slot: 0, swingBPivotBar: 10));
        q.Enqueue(MakeItem(slot: 0, swingBPivotBar: 20));
        q.Enqueue(MakeItem(slot: 0, swingBPivotBar: 30));

        q.RemoveAt(1);

        Assert.Equal(2, q.Count);
        Assert.Equal(10, q[0].OriginalSwingBPivotBar);
        Assert.Equal(30, q[1].OriginalSwingBPivotBar);
    }

    [Fact]
    public void Clear_EmptiesQueue()
    {
        var q = new DeferredSplitFireQueue();
        q.Enqueue(MakeItem(slot: 0, swingBPivotBar: 10));
        q.Enqueue(MakeItem(slot: 0, swingBPivotBar: 20));

        q.Clear();

        Assert.Equal(0, q.Count);
    }

    [Fact]
    public void Enqueue_NullThrows()
    {
        var q = new DeferredSplitFireQueue();
        Assert.Throws<ArgumentNullException>(() => q.Enqueue(null!));
    }

    static DeferredSplitFire SellItem(int slot, int bPivot, int fireBar = 100) =>
        new()
        {
            Ev = new CompoundFireEvent(slot, $"R{slot + 1}", SignalDirection.Sell, "15", fireBar, DateTime.UnixEpoch, 100, fireBar),
            OriginalSwingBPivotBar = bPivot,
            DeferredAtBar = fireBar,
            DeferredAtTimeUtc = DateTime.UnixEpoch,
        };

    [Fact]
    public void TryEnqueueWithTierFilter_SkipsR6_WhenR4AlreadyQueuedSameB()
    {
        var q = new DeferredSplitFireQueue();
        Assert.Equal(DeferredEnqueueResult.Enqueued, q.TryEnqueueWithTierFilter(SellItem(3, 9507), tierFilterEnabled: true));
        Assert.Equal(DeferredEnqueueResult.SkippedLowerTier, q.TryEnqueueWithTierFilter(SellItem(5, 9507), tierFilterEnabled: true));
        Assert.Equal(1, q.Count);
        Assert.Equal(3, q[0].Ev.SlotIndex);
    }

    [Fact]
    public void TryEnqueueWithTierFilter_AllowsR6_AfterR4RemovedFromQueue()
    {
        var q = new DeferredSplitFireQueue();
        q.TryEnqueueWithTierFilter(SellItem(3, 9507), tierFilterEnabled: true);
        q.RemoveAt(0);

        Assert.Equal(DeferredEnqueueResult.Enqueued, q.TryEnqueueWithTierFilter(SellItem(5, 9507), tierFilterEnabled: true));
        Assert.Single(q.Items);
        Assert.Equal(5, q[0].Ev.SlotIndex);
    }

    [Fact]
    public void TryEnqueueWithTierFilter_EvictsR6_WhenHigherTierR4EnqueuesLater()
    {
        var q = new DeferredSplitFireQueue();
        q.TryEnqueueWithTierFilter(SellItem(5, 9507), tierFilterEnabled: false);
        Assert.Equal(DeferredEnqueueResult.Enqueued, q.TryEnqueueWithTierFilter(SellItem(3, 9507), tierFilterEnabled: true));

        Assert.Single(q.Items);
        Assert.Equal(3, q[0].Ev.SlotIndex);
    }

    [Fact]
    public void TryEnqueueWithTierFilter_KeepsR3AndR4_SameTopTierSameB()
    {
        var q = new DeferredSplitFireQueue();
        var buy = (int slot) => new DeferredSplitFire
        {
            Ev = new CompoundFireEvent(slot, $"R{slot + 1}", SignalDirection.Buy, "15", 100, DateTime.UnixEpoch, 100, 100),
            OriginalSwingBPivotBar = 500,
            DeferredAtBar = 100,
            DeferredAtTimeUtc = DateTime.UnixEpoch,
        };

        Assert.Equal(DeferredEnqueueResult.Enqueued, q.TryEnqueueWithTierFilter(buy(2), tierFilterEnabled: true));
        Assert.Equal(DeferredEnqueueResult.Enqueued, q.TryEnqueueWithTierFilter(buy(3), tierFilterEnabled: true));
        Assert.Equal(2, q.Count);
    }

    [Fact]
    public void CanExecuteAtIndex_BlocksLowerTier_WhenHigherTierStillQueued()
    {
        var q = new DeferredSplitFireQueue();
        q.TryEnqueueWithTierFilter(SellItem(5, 9507, fireBar: 9515), tierFilterEnabled: false);
        q.TryEnqueueWithTierFilter(SellItem(3, 9507, fireBar: 9513), tierFilterEnabled: false);

        Assert.False(q.CanExecuteAtIndex(0, tierFilterEnabled: true));
        Assert.True(q.CanExecuteAtIndex(1, tierFilterEnabled: true));
    }

    [Fact]
    public void EvictLowerTierSameSetup_RemovesWaitingLowerTierAfterHigherExecutes()
    {
        var q = new DeferredSplitFireQueue();
        q.TryEnqueueWithTierFilter(SellItem(5, 9507), tierFilterEnabled: false);
        q.TryEnqueueWithTierFilter(SellItem(3, 9507), tierFilterEnabled: false);

        var removed = q.EvictLowerTierSameSetup(9507, SignalDirection.Sell, executedTier: 2, tierFilterEnabled: true);

        Assert.Equal(1, removed);
        Assert.Single(q.Items);
        Assert.Equal(3, q[0].Ev.SlotIndex);
    }

    [Fact]
    public void TryEnqueueWithTierFilter_DifferentB_AllowsBothTiers()
    {
        var q = new DeferredSplitFireQueue();
        Assert.Equal(DeferredEnqueueResult.Enqueued, q.TryEnqueueWithTierFilter(SellItem(3, 9507), tierFilterEnabled: true));
        Assert.Equal(DeferredEnqueueResult.Enqueued, q.TryEnqueueWithTierFilter(SellItem(5, 9600), tierFilterEnabled: true));
        Assert.Equal(2, q.Count);
    }

    /// <summary>
    /// Ensures the mapper's no-swing-C skip reason carries the exact substring that
    /// <c>Loop6BacktestTradingBot.ShouldDeferForSwingC</c> checks for, and that defer only
    /// applies while <see cref="SwingCWaitDiagnostic.IsAwaitingSwingCConfirmation"/> is true.
    /// </summary>
    [Fact]
    public void Mapper_NoSwingC_ReasonContainsTrigger()
    {
        var s = new PineStateEngine();
        var idx = s.Pivots.PushPivot(99.5, 50, SwingBResolver.TypeLow, 0, 1, 1);
        s.Pivots.SetFlag(idx, 1);
        s.Pivots.SetHasKey(idx, true);
        s.Pivots.SetKeyBox(idx, new KeyBoxRef
        {
            Spec = new KeyBoxSpec { Top = 100, Bottom = 99 },
        });

        var cfg = new TradeMapperConfig
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
            SwingCTpMode = SwingCTpMode.SplitTpAtCAndD,
            SwingCEdgeTpRuleSlots = new HashSet<int> { 0, 1, 2, 3, 4, 5 },
            AtrSlWidthAdjust = new AtrSlWidthAdjustConfig { UseAtrAdjustedSlWidthMultiplier = false },
        };

        var ev = new CompoundFireEvent(0, "R1", SignalDirection.Buy, "15", 100, DateTime.UnixEpoch, 100, 100);

        var plans = CompoundFireTradeMapper.TryBuildPlans(in ev, s, in cfg, out var reason);

        Assert.Empty(plans);
        Assert.Contains("no confirmed swing C", reason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Deferred retry must use B captured at fire time. After C confirms, B is often promoted
    /// off ACTIVE while a newer ACTIVE pivot of the wrong type becomes "most recent" — re-resolving
    /// B from live state would incorrectly fail with "not swing LOW/HIGH".
    /// </summary>
    [Fact]
    public void TryBuildPlans_PinnedB_StillBuildsSplitAfterBLeavesActiveScan()
    {
        var s = new PineStateEngine();
        var bIdx = s.Pivots.PushPivot(99.5, 50, SwingBResolver.TypeLow, 0, 1, 1);
        s.Pivots.SetFlag(bIdx, SwingBResolver.FlagActive);
        s.Pivots.SetHasKey(bIdx, true);
        s.Pivots.SetKeyBox(bIdx, new KeyBoxRef { Spec = new KeyBoxSpec { Top = 100, Bottom = 99 } });

        var cfg = new TradeMapperConfig
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
            SwingCTpMode = SwingCTpMode.SplitTpAtCAndD,
            SwingCEdgeTpRuleSlots = new HashSet<int> { 0, 1, 2, 3, 4, 5 },
            AtrSlWidthAdjust = new AtrSlWidthAdjustConfig { UseAtrAdjustedSlWidthMultiplier = false },
        };

        var ev = new CompoundFireEvent(0, "R1", SignalDirection.Buy, "15", 100, DateTime.UnixEpoch, 100, 100);

        var noC = CompoundFireTradeMapper.TryBuildPlans(in ev, s, in cfg, out var noCReason);
        Assert.Empty(noC);
        Assert.Contains("no confirmed swing C", noCReason, StringComparison.OrdinalIgnoreCase);

        var cIdx = s.Pivots.PushPivot(105, 70, SwingBResolver.TypeHigh, 0, 1, 1);
        s.Pivots.SetFlag(cIdx, SwingCEdgeTakeProfitResolver.FlagDSwing);
        s.Pivots.SetHasKey(cIdx, true);
        s.Pivots.SetKeyBox(cIdx, new KeyBoxRef { Spec = new KeyBoxSpec { Top = 106, Bottom = 104 } });

        // Simulate post-fire promotion: B is no longer ACTIVE; a newer ACTIVE HIGH is now "most recent".
        s.Pivots.SetFlag(bIdx, SwingBResolver.FlagMainC);
        var newerIdx = s.Pivots.PushPivot(108, 85, SwingBResolver.TypeHigh, 0, 1, 1);
        s.Pivots.SetFlag(newerIdx, SwingBResolver.FlagActive);
        s.Pivots.SetHasKey(newerIdx, true);
        s.Pivots.SetKeyBox(newerIdx, new KeyBoxRef { Spec = new KeyBoxSpec { Top = 109, Bottom = 107 } });

        var live = CompoundFireTradeMapper.TryBuildPlans(in ev, s, in cfg, out var liveReason);
        Assert.Empty(live);
        Assert.Contains("not swing LOW", liveReason, StringComparison.OrdinalIgnoreCase);

        var pinnedB = new SwingBResult
        {
            PivotIndex = bIdx,
            PivotBar = 50,
            Type = SwingBResolver.TypeLow,
            KeyTop = 100,
            KeyBottom = 99,
            Source = SwingBSource.Active,
        };

        var pinned = CompoundFireTradeMapper.TryBuildPlans(in ev, s, in cfg, out var pinnedReason, pinnedB: pinnedB);
        Assert.Equal(2, pinned.Count);
        Assert.Equal("ok(split)", pinnedReason);
        Assert.Equal(50, pinned[0].SwingBPivotBar);
    }
}
