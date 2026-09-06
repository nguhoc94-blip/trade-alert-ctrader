using System;
using System.Collections.Generic;
using TradeAlert.BacktestRobot.Execution;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.Alerts;
using TradeAlert.Core.Models.KeyLevel;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests;

public class PostBPushObstacleCancelRuleTests
{
    static PineStateEngine ChartWithPushSwing(int bBar, int bType, int pushBar, int pushType, double pushPrice, double? keyTop = null, double? keyBottom = null)
    {
        var s = new PineStateEngine();
        var bIdx = s.Pivots.PushPivot(bType == SwingBResolver.TypeLow ? 99 : 101, bBar, bType, 0, 1, 1);
        s.Pivots.SetHasKey(bIdx, true);
        s.Pivots.SetKeyExtending(bIdx, true);
        s.Pivots.SetKeyBox(bIdx, new KeyBoxRef { Spec = new KeyBoxSpec { Top = 100, Bottom = 99 } });

        var pushIdx = s.Pivots.PushPivot(pushPrice, pushBar, pushType, 0, 1, 1);
        if (keyTop.HasValue && keyBottom.HasValue)
        {
            s.Pivots.SetHasKey(pushIdx, true);
            s.Pivots.SetKeyExtending(pushIdx, true);
            s.Pivots.SetKeyBox(pushIdx, new KeyBoxRef { Spec = new KeyBoxSpec { Top = keyTop.Value, Bottom = keyBottom.Value } });
        }

        return s;
    }

    static void AddRedKeyLevel(PineStateEngine s, int bar, double top, double bottom)
    {
        var idx = s.Pivots.PushPivot(bottom, bar, type: 1, timeMs: 0, highId: 1, lowId: 1);
        s.Pivots.SetFlag(idx, 1);
        s.Pivots.SetHasKey(idx, true);
        s.Pivots.SetKeyExtending(idx, true);
        s.Pivots.SetKeyBox(idx, new KeyBoxRef { Spec = new KeyBoxSpec { Top = top, Bottom = bottom } });
    }

    static void AddGreenKeyLevel(PineStateEngine s, int bar, double top, double bottom)
    {
        var idx = s.Pivots.PushPivot(bottom, bar, type: -1, timeMs: 0, highId: 1, lowId: 1);
        s.Pivots.SetFlag(idx, 1);
        s.Pivots.SetHasKey(idx, true);
        s.Pivots.SetKeyExtending(idx, true);
        s.Pivots.SetKeyBox(idx, new KeyBoxRef { Spec = new KeyBoxSpec { Top = top, Bottom = bottom } });
    }

    static PineStateEngine StateWithRedZone(int bar = 10, double top = 105, double bottom = 104) =>
        StateWithKeylevel(type: 1, flag: 1, top, bottom, bar);

    static PineStateEngine StateWithGreenZone(int bar = 10, double top = 96, double bottom = 95) =>
        StateWithKeylevel(type: -1, flag: 1, top, bottom, bar);

    static PineStateEngine StateWithKeylevel(int type, int flag, double top, double bottom, int bar = 50, bool extending = true)
    {
        var s = new PineStateEngine();
        var idx = s.Pivots.PushPivot(bottom, bar, type, 0, 1, 1);
        s.Pivots.SetFlag(idx, flag);
        s.Pivots.SetHasKey(idx, true);
        s.Pivots.SetKeyExtending(idx, extending);
        s.Pivots.SetKeyBox(idx, new KeyBoxRef { Spec = new KeyBoxSpec { Top = top, Bottom = bottom } });
        return s;
    }

    static PostBPushObstacleCancelRuleConfig Cfg() => new()
    {
        RuleSlotIndices = new HashSet<int> { 0, 1, 2, 3, 4, 5 },
        TfTokens = new[] { "15" },
        IncludeKeyLevels = true,
        IncludeOrderBlocks = false,
        IncludeBrokenKeyLevels = false,
        TolerancePips = 0,
        PipSize = 0.01,
    };

    static PendingOrderContext BuyCtx(int bBar = 50) => new()
    {
        Label = "L6BT|R3|B50",
        RuleSlot = 2,
        Direction = SignalDirection.Buy,
        IsBuy = true,
        SwingBPivotBar = bBar,
        SwingBPivotIndex = 0,
        SwingBTfToken = "15",
        EntryPrice = 100,
        StopLoss = 97,
    };

    static PendingOrderContext SellCtx(int bBar = 50) => new()
    {
        Label = "L6BT|R4|B50",
        RuleSlot = 3,
        Direction = SignalDirection.Sell,
        IsBuy = false,
        SwingBPivotBar = bBar,
        SwingBPivotIndex = 0,
        SwingBTfToken = "15",
        EntryPrice = 100,
        StopLoss = 103,
    };

    [Fact]
    public void ParseRulesMask_All_IncludesEverySlot()
    {
        var slots = PostBPushObstacleCancelRuleConfig.ParseRulesMask("all");
        Assert.Equal(6, slots.Count);
        for (var i = 0; i < 6; i++)
            Assert.Contains(i, slots);
    }

    [Fact]
    public void FindPostBPushSwings_Buy_OnlyHighAfterB()
    {
        var chart = ChartWithPushSwing(bBar: 50, bType: SwingBResolver.TypeLow, pushBar: 55, pushType: SwingBResolver.TypeHigh, pushPrice: 104.5, keyTop: 105, keyBottom: 104);
        chart.Pivots.PushPivot(98, 52, SwingBResolver.TypeLow, 0, 1, 1);
        chart.Pivots.PushPivot(103, 48, SwingBResolver.TypeHigh, 0, 1, 1);

        var swings = PostBPushObstacleCancelRule.FindPostBPushSwings(chart, BuyCtx());
        var s = Assert.Single(swings);
        Assert.Equal(55, s.PivotBar);
        Assert.Equal("HIGH", s.PushTypeLabel);
        Assert.Equal(104, s.PushLow, 6);
        Assert.Equal(105, s.PushHigh, 6);
    }

    [Fact]
    public void Buy_PostBHighOverlapsRedZone_Cancels()
    {
        var chart = ChartWithPushSwing(bBar: 50, bType: SwingBResolver.TypeLow, pushBar: 55, pushType: SwingBResolver.TypeHigh, pushPrice: 104.5, keyTop: 105, keyBottom: 104);
        AddRedKeyLevel(chart, bar: 20, top: 105.2, bottom: 104.8);
        var states = new Dictionary<string, PineStateEngine>();

        var result = PostBPushObstacleCancelRule.EvaluateWithStates(BuyCtx(), chart, states, Cfg());

        Assert.True(result.ShouldCancel);
        Assert.Equal(55, result.Push!.PivotBar);
        Assert.Equal("M15", result.MatchedTf);
        Assert.Equal(ZoneSourceKind.KeyLevel, result.MatchedSource);
        Assert.Equal(ZoneEffectiveColor.Red, result.EffectiveColor);
        Assert.True(result.OverlapPips > 0);
    }

    [Fact]
    public void Buy_PostBHighOverlapsGreenZone_DoesNotCancel()
    {
        var chart = ChartWithPushSwing(bBar: 50, bType: SwingBResolver.TypeLow, pushBar: 55, pushType: SwingBResolver.TypeHigh, pushPrice: 104.5, keyTop: 105, keyBottom: 104);
        AddGreenKeyLevel(chart, bar: 20, top: 105.2, bottom: 104.8);
        var states = new Dictionary<string, PineStateEngine>();

        var result = PostBPushObstacleCancelRule.EvaluateWithStates(BuyCtx(), chart, states, Cfg());

        Assert.False(result.ShouldCancel);
    }

    [Fact]
    public void Sell_PostBLowOverlapsGreenZone_Cancels()
    {
        var chart = ChartWithPushSwing(bBar: 50, bType: SwingBResolver.TypeHigh, pushBar: 55, pushType: SwingBResolver.TypeLow, pushPrice: 95.5, keyTop: 96, keyBottom: 95);
        AddGreenKeyLevel(chart, bar: 20, top: 96.2, bottom: 95.8);
        var states = new Dictionary<string, PineStateEngine>();

        var result = PostBPushObstacleCancelRule.EvaluateWithStates(SellCtx(), chart, states, Cfg());

        Assert.True(result.ShouldCancel);
        Assert.Equal("LOW", result.Push!.PushTypeLabel);
        Assert.Equal(ZoneEffectiveColor.Green, result.EffectiveColor);
    }

    [Fact]
    public void PushBeforeOrAtB_IsIgnored()
    {
        var chart = ChartWithPushSwing(bBar: 50, bType: SwingBResolver.TypeLow, pushBar: 50, pushType: SwingBResolver.TypeHigh, pushPrice: 104.5, keyTop: 105, keyBottom: 104);
        AddRedKeyLevel(chart, bar: 20, top: 105.2, bottom: 104.8);
        var states = new Dictionary<string, PineStateEngine>();

        var result = PostBPushObstacleCancelRule.EvaluateWithStates(BuyCtx(), chart, states, Cfg());

        Assert.False(result.ShouldCancel);
    }

    [Fact]
    public void PushWithoutKeybox_UsesPointRange()
    {
        var chart = ChartWithPushSwing(bBar: 50, bType: SwingBResolver.TypeLow, pushBar: 55, pushType: SwingBResolver.TypeHigh, pushPrice: 104.5);
        AddRedKeyLevel(chart, bar: 20, top: 104.6, bottom: 104.4);
        var states = new Dictionary<string, PineStateEngine>();

        var result = PostBPushObstacleCancelRule.EvaluateWithStates(BuyCtx(), chart, states, Cfg());

        Assert.True(result.ShouldCancel);
        Assert.Equal(104.5, result.Push!.PushLow, 6);
        Assert.Equal(104.5, result.Push.PushHigh, 6);
    }

    [Fact]
    public void M5KeyLevel_IsIncluded_NotObOnly()
    {
        var chart = ChartWithPushSwing(bBar: 50, bType: SwingBResolver.TypeLow, pushBar: 55, pushType: SwingBResolver.TypeHigh, pushPrice: 104.5, keyTop: 105, keyBottom: 104);
        var m5 = StateWithRedZone(bar: 30, top: 105.1, bottom: 104.9);
        var states = new Dictionary<string, PineStateEngine> { ["5"] = m5 };
        var cfg = new PostBPushObstacleCancelRuleConfig
        {
            RuleSlotIndices = Cfg().RuleSlotIndices,
            TfTokens = new[] { "5" },
            IncludeKeyLevels = true,
            IncludeOrderBlocks = false,
            IncludeBrokenKeyLevels = false,
            TolerancePips = 0,
            PipSize = 0.01,
        };

        var result = PostBPushObstacleCancelRule.EvaluateWithStates(BuyCtx(), chart, states, cfg);

        Assert.True(result.ShouldCancel);
        Assert.Equal("M5", result.MatchedTf);
    }

    // ---------- Cross-TF self-identity (PostBPushObstacleCancelRule.IsPushSwingOwnKeyLevel) ----------

    static PostBPushSwingMatch MakePush(DateTime? openTime, TimeSpan tfPeriod, double low = 1.1000, double high = 1.1020)
        => new()
        {
            PivotIndex = 1,
            PivotBar = 100,
            PushTypeLabel = "HIGH",
            PushLow = low,
            PushHigh = high,
            PivotOpenTime = openTime,
            ChartTfPeriod = tfPeriod,
        };

    static ZoneCandidate MakeZone(string tf, DateTime? openTime, TimeSpan tfPeriod, double low = 1.1000, double high = 1.1020, int? pivotBar = 200)
        => new()
        {
            TfToken = tf,
            Source = ZoneSourceKind.KeyLevel,
            EffectiveColor = ZoneEffectiveColor.Red,
            PivotIndex = 99,
            PivotBar = pivotBar,
            PivotOpenTime = openTime,
            TfPeriod = tfPeriod,
            Low = low,
            High = high,
        };

    [Fact]
    public void OwnKeyLevel_SameTfBarIndexMatch_StillFires()
    {
        var push = MakePush(openTime: null, tfPeriod: TimeSpan.FromMinutes(15));
        var zone = MakeZone(tf: "15", openTime: null, tfPeriod: TimeSpan.FromMinutes(15), pivotBar: 100);
        var skipped = 0;
        Assert.True(PostBPushObstacleCancelRule.IsPushSwingOwnKeyLevel(in push, in zone, "15", ref skipped));
        Assert.Equal(0, skipped); // same-TF path doesn't count as cross-TF skip
    }

    [Fact]
    public void OwnKeyLevel_SameTfDifferentBarIndex_DoesNotFire()
    {
        var push = MakePush(openTime: null, tfPeriod: TimeSpan.FromMinutes(15));
        var zone = MakeZone(tf: "15", openTime: null, tfPeriod: TimeSpan.FromMinutes(15), pivotBar: 200);
        var skipped = 0;
        Assert.False(PostBPushObstacleCancelRule.IsPushSwingOwnKeyLevel(in push, in zone, "15", ref skipped));
        Assert.Equal(0, skipped);
    }

    [Fact]
    public void OwnKeyLevel_CrossTf_M5ZoneInsideM15PushWindow_Fires()
    {
        // M15 push: 12:00 � 12:15. M5 zone: 12:05 � 12:10 (inside push window).
        var pushOpen = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);
        var zoneOpen = new DateTime(2024, 1, 1, 12, 5, 0, DateTimeKind.Unspecified);
        var push = MakePush(pushOpen, TimeSpan.FromMinutes(15));
        var zone = MakeZone(tf: "5", openTime: zoneOpen, tfPeriod: TimeSpan.FromMinutes(5));
        var skipped = 0;
        Assert.True(PostBPushObstacleCancelRule.IsPushSwingOwnKeyLevel(in push, in zone, "15", ref skipped));
        Assert.Equal(1, skipped);
    }

    [Fact]
    public void OwnKeyLevel_CrossTf_M5ZoneOutsideM15PushWindow_DoesNotFire()
    {
        // M5 zone at 12:30, push at 12:00 � different physical region.
        var pushOpen = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);
        var zoneOpen = new DateTime(2024, 1, 1, 12, 30, 0, DateTimeKind.Unspecified);
        var push = MakePush(pushOpen, TimeSpan.FromMinutes(15));
        var zone = MakeZone(tf: "5", openTime: zoneOpen, tfPeriod: TimeSpan.FromMinutes(5));
        var skipped = 0;
        Assert.False(PostBPushObstacleCancelRule.IsPushSwingOwnKeyLevel(in push, in zone, "15", ref skipped));
        Assert.Equal(0, skipped);
    }

    [Fact]
    public void OwnKeyLevel_CrossTf_M15PushInsideH1ZoneWithSimilarMid_Fires()
    {
        // H1 zone: 12:00 � 13:00 containing M15 push at 12:30 with similar mid-price.
        var pushOpen = new DateTime(2024, 1, 1, 12, 30, 0, DateTimeKind.Unspecified);
        var zoneOpen = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);
        var push = MakePush(pushOpen, TimeSpan.FromMinutes(15), low: 1.1010, high: 1.1030); // mid=1.1020
        var zone = MakeZone(tf: "60", openTime: zoneOpen, tfPeriod: TimeSpan.FromMinutes(60),
                            low: 1.1000, high: 1.1040); // mid=1.1020, span=0.0040, �span=0.0020
        var skipped = 0;
        Assert.True(PostBPushObstacleCancelRule.IsPushSwingOwnKeyLevel(in push, in zone, "15", ref skipped));
        Assert.Equal(1, skipped);
    }

    [Fact]
    public void OwnKeyLevel_CrossTf_M15PushInsideH1ZoneWithDifferentMid_DoesNotFire()
    {
        // H1 zone at lower price level than M15 push � even with time containment, price-mid guard rejects.
        var pushOpen = new DateTime(2024, 1, 1, 12, 30, 0, DateTimeKind.Unspecified);
        var zoneOpen = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);
        var push = MakePush(pushOpen, TimeSpan.FromMinutes(15), low: 1.1050, high: 1.1070); // mid=1.1060
        var zone = MakeZone(tf: "60", openTime: zoneOpen, tfPeriod: TimeSpan.FromMinutes(60),
                            low: 1.1000, high: 1.1010); // mid=1.1005, span=0.0020, �span=0.0010 < |1.1060-1.1005|
        var skipped = 0;
        Assert.False(PostBPushObstacleCancelRule.IsPushSwingOwnKeyLevel(in push, in zone, "15", ref skipped));
        Assert.Equal(0, skipped);
    }

    [Fact]
    public void OwnKeyLevel_CrossTf_NoOpenTime_DegradeGracefullyToFalse()
    {
        // Missing time on either side ? cross-TF check is skipped; legacy bar_index won't match a foreign zone.
        var push = MakePush(openTime: null, tfPeriod: TimeSpan.FromMinutes(15));
        var zone = MakeZone(tf: "5", openTime: null, tfPeriod: TimeSpan.FromMinutes(5));
        var skipped = 0;
        Assert.False(PostBPushObstacleCancelRule.IsPushSwingOwnKeyLevel(in push, in zone, "15", ref skipped));
        Assert.Equal(0, skipped);
    }

    [Fact]
    public void OwnKeyLevel_CrossTf_ZeroTfPeriod_DegradeGracefullyToFalse()
    {
        var pushOpen = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);
        var zoneOpen = new DateTime(2024, 1, 1, 12, 5, 0, DateTimeKind.Unspecified);
        var push = MakePush(pushOpen, TimeSpan.Zero); // unknown chart TF
        var zone = MakeZone(tf: "5", openTime: zoneOpen, tfPeriod: TimeSpan.FromMinutes(5));
        var skipped = 0;
        Assert.False(PostBPushObstacleCancelRule.IsPushSwingOwnKeyLevel(in push, in zone, "15", ref skipped));
        Assert.Equal(0, skipped);
    }

    [Fact]
    public void TfPeriodTokens_Parse_KnownTokens()
    {
        Assert.Equal(TimeSpan.FromMinutes(1),  TfPeriodTokens.Parse("1"));
        Assert.Equal(TimeSpan.FromMinutes(5),  TfPeriodTokens.Parse("5"));
        Assert.Equal(TimeSpan.FromMinutes(15), TfPeriodTokens.Parse("15"));
        Assert.Equal(TimeSpan.FromMinutes(60), TfPeriodTokens.Parse("60"));
        Assert.Equal(TimeSpan.FromHours(4),    TfPeriodTokens.Parse("240"));
        Assert.Equal(TimeSpan.FromDays(1),     TfPeriodTokens.Parse("D"));
        Assert.Equal(TimeSpan.Zero,            TfPeriodTokens.Parse("weird"));
    }

    [Fact]
    public void FormatCancelLog_MatchesExpectedShape()
    {
        var chart = ChartWithPushSwing(bBar: 50, bType: SwingBResolver.TypeLow, pushBar: 55, pushType: SwingBResolver.TypeHigh, pushPrice: 104.5, keyTop: 105, keyBottom: 104);
        AddRedKeyLevel(chart, bar: 20, top: 105.2, bottom: 104.8);
        var states = new Dictionary<string, PineStateEngine>();
        var ctx = BuyCtx();
        var result = PostBPushObstacleCancelRule.EvaluateWithStates(ctx, chart, states, Cfg());
        var log = PostBPushObstacleCancelRule.FormatCancelLog(in ctx, in result);

        Assert.Contains("[L6BT] CANCEL L6BT|R3|B50 (post-B-push-obstacle)", log);
        Assert.Contains("pushBar=55", log);
        Assert.Contains("pushType=HIGH", log);
        Assert.Contains("matched M15 KeyLevel RED", log);
    }
}
