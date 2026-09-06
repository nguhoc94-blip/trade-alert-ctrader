using System.Collections.Generic;
using System.Linq;
using TradeAlert.BacktestRobot.Execution;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.KeyLevel;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests;

public class EntrySlZoneGateTests
{
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

    static void AddOb(PineStateEngine s, int type, double top, double bottom, int state = 2, bool extending = true)
    {
        s.ObPool.Push(state: state, count: 1, x: 0, bar: 10, typ: type, owner: 0,
            source: 0, number: 1, pivotType: type, pivotHighId: 1, pivotLowId: 1);
        var i = s.ObPool.Count - 1;
        s.ObPool.SetExtending(i, extending);
        s.ObPool.SetBox(i, new ObBoxSpec { Top = top, Bottom = bottom, ObType = type });
    }

    static EntrySlZoneGateConfig Cfg(bool kl = true, bool ob = true, bool broken = true, bool m5ObOnly = true) => new()
    {
        GatedSlotIndices = new HashSet<int> { 0, 1, 4, 5 },
        TfTokens = new[] { "15" },
        IncludeKeyLevels = kl,
        IncludeOrderBlocks = ob,
        IncludeBrokenKeyLevels = broken,
        OverlapTolerancePips = 0,
        RequireSameColor = true,
        ExcludeSetupSwingBKeyLevel = true,
        M5ConfluenceObOnly = m5ObOnly,
        PipSize = 0.01,
    };

    static TradePlan BuyPlan(double entry, double sl, int bBar = 50, int swingIdx = 0) => new()
    {
        IsBuy = true,
        EntryLimit = entry,
        StopLoss = sl,
        SwingBPivotBar = bBar,
        SwingBPivotIndex = swingIdx,
        SwingBTfToken = "15",
        SwingBKeyLow = 99,
        SwingBKeyHigh = 100,
    };

    static TradePlan SellPlan(double entry, double sl, int bBar = 50, int swingIdx = 0) => new()
    {
        IsBuy = false,
        EntryLimit = entry,
        StopLoss = sl,
        SwingBPivotBar = bBar,
        SwingBPivotIndex = swingIdx,
        SwingBTfToken = "15",
        SwingBKeyHigh = 101,
        SwingBKeyLow = 100,
    };

    static ZoneCandidate Zone(string tf, double low, double high, ZoneSourceKind source = ZoneSourceKind.OrderBlock) => new()
    {
        TfToken = tf,
        Low = low,
        High = high,
        Source = source,
        EffectiveColor = source == ZoneSourceKind.OrderBlock ? ZoneEffectiveColor.Green : ZoneEffectiveColor.Green,
    };

    [Theory]
    [InlineData(true, 97, "15", 97.5, 99.5, true)]
    [InlineData(true, 97, "15", 96.5, 99.5, false)]
    [InlineData(true, 97, "5", 97.01, 99, true)]
    [InlineData(true, 97, "5", 97, 99, false)]
    [InlineData(false, 103, "15", 100.5, 102.5, true)]
    [InlineData(false, 103, "15", 100.5, 103, false)]
    [InlineData(false, 103, "5", 100, 102.99, true)]
    [InlineData(false, 103, "5", 100, 103.01, false)]
    [InlineData(true, 97, "60", 96, 99, true)]
    [InlineData(false, 103, "240", 100, 104, true)]
    public void PassesShortTfSlBoundary_M5M15Only(bool isBuy, double sl, string tf, double low, double high, bool expected)
    {
        var candidate = Zone(tf, low, high);
        Assert.Equal(expected, EntrySlZoneGate.PassesShortTfSlBoundary(isBuy, sl, in candidate));
    }

    [Fact]
    public void Gate_Buy_SkipsM15ZoneWhenBottomNotAboveSl()
    {
        var m15 = StateWithKeylevel(type: -1, flag: 1, top: 100, bottom: 99, bar: 50);
        AddOb(m15, type: -1, top: 99.5, bottom: 96.5);

        var plan = BuyPlan(entry: 100, sl: 97, bBar: 50, swingIdx: 0);
        var cfg = new EntrySlZoneGateConfig
        {
            TfTokens = new[] { "15" },
            IncludeKeyLevels = true,
            IncludeOrderBlocks = true,
            IncludeBrokenKeyLevels = false,
            RequireSameColor = true,
            ExcludeSetupSwingBKeyLevel = true,
            M5ConfluenceObOnly = true,
            PipSize = 0.01,
        };

        var result = EntrySlZoneGate.EvaluateWithStates(
            in plan,
            new Dictionary<string, PineStateEngine> { ["15"] = m15 },
            in cfg);

        Assert.False(result.Passed);
        Assert.Equal(1, result.OwnSwingBSkipped);
        Assert.Equal(1, result.M5M15SlBoundarySkipped);
        Assert.Equal(0, result.OverlappingAfterExclusion);
    }

    [Fact]
    public void Gate_Buy_PassesM15ObWhenBottomAboveSl()
    {
        var m15 = StateWithKeylevel(type: -1, flag: 1, top: 100, bottom: 99, bar: 50);
        AddOb(m15, type: -1, top: 99.5, bottom: 97.5);

        var plan = BuyPlan(entry: 100, sl: 97, bBar: 50, swingIdx: 0);
        var cfg = new EntrySlZoneGateConfig
        {
            TfTokens = new[] { "15" },
            IncludeKeyLevels = true,
            IncludeOrderBlocks = true,
            IncludeBrokenKeyLevels = false,
            RequireSameColor = true,
            ExcludeSetupSwingBKeyLevel = true,
            M5ConfluenceObOnly = true,
            PipSize = 0.01,
        };

        var result = EntrySlZoneGate.EvaluateWithStates(
            in plan,
            new Dictionary<string, PineStateEngine> { ["15"] = m15 },
            in cfg);

        Assert.True(result.Passed);
        Assert.Equal(1, result.OwnSwingBSkipped);
        Assert.Equal(0, result.M5M15SlBoundarySkipped);
        Assert.Equal(1, result.OverlappingAfterExclusion);
        Assert.Equal("M15", result.MatchedTf);
    }

    [Fact]
    public void Gate_Sell_SkipsM15ZoneWhenTopNotBelowSl()
    {
        var m15Own = StateWithKeylevel(type: 1, flag: 1, top: 101, bottom: 100, bar: 50);
        AddOb(m15Own, type: 1, top: 103.5, bottom: 100.5);

        var plan = SellPlan(entry: 100, sl: 103, bBar: 50, swingIdx: 0);
        var cfg = new EntrySlZoneGateConfig
        {
            TfTokens = new[] { "15" },
            IncludeKeyLevels = true,
            IncludeOrderBlocks = true,
            IncludeBrokenKeyLevels = false,
            RequireSameColor = true,
            ExcludeSetupSwingBKeyLevel = true,
            M5ConfluenceObOnly = true,
            PipSize = 0.01,
        };

        var result = EntrySlZoneGate.EvaluateWithStates(
            in plan,
            new Dictionary<string, PineStateEngine> { ["15"] = m15Own },
            in cfg);

        Assert.False(result.Passed);
        Assert.Equal(1, result.OwnSwingBSkipped);
        Assert.Equal(1, result.M5M15SlBoundarySkipped);
        Assert.Equal(0, result.OverlappingAfterExclusion);
    }

    [Fact]
    public void Gate_Sell_PassesM15ObWhenTopBelowSl()
    {
        var m15Own = StateWithKeylevel(type: 1, flag: 1, top: 101, bottom: 100, bar: 50);
        AddOb(m15Own, type: 1, top: 102.5, bottom: 100.5);

        var plan = SellPlan(entry: 100, sl: 103, bBar: 50, swingIdx: 0);
        var cfg = new EntrySlZoneGateConfig
        {
            TfTokens = new[] { "15" },
            IncludeKeyLevels = true,
            IncludeOrderBlocks = true,
            IncludeBrokenKeyLevels = false,
            RequireSameColor = true,
            ExcludeSetupSwingBKeyLevel = true,
            M5ConfluenceObOnly = true,
            PipSize = 0.01,
        };

        var result = EntrySlZoneGate.EvaluateWithStates(
            in plan,
            new Dictionary<string, PineStateEngine> { ["15"] = m15Own },
            in cfg);

        Assert.True(result.Passed);
        Assert.Equal(1, result.OwnSwingBSkipped);
        Assert.Equal(0, result.M5M15SlBoundarySkipped);
        Assert.Equal(1, result.OverlappingAfterExclusion);
        Assert.Equal("M15", result.MatchedTf);
    }

    [Fact]
    public void Gate_H1Overlap_DoesNotApplyM5M15SlBoundary()
    {
        var m15Own = StateWithKeylevel(type: -1, flag: 1, top: 100, bottom: 99, bar: 50);
        var h1 = new PineStateEngine();
        AddOb(h1, type: -1, top: 99.5, bottom: 96.5);

        var plan = BuyPlan(entry: 100, sl: 97, bBar: 50, swingIdx: 0);
        var cfg = new EntrySlZoneGateConfig
        {
            TfTokens = new[] { "15", "60" },
            IncludeKeyLevels = true,
            IncludeOrderBlocks = true,
            IncludeBrokenKeyLevels = false,
            RequireSameColor = true,
            ExcludeSetupSwingBKeyLevel = true,
            M5ConfluenceObOnly = true,
            PipSize = 0.01,
        };

        var result = EntrySlZoneGate.EvaluateWithStates(
            in plan,
            new Dictionary<string, PineStateEngine> { ["15"] = m15Own, ["60"] = h1 },
            in cfg);

        Assert.True(result.Passed);
        Assert.Equal(0, result.M5M15SlBoundarySkipped);
        Assert.Equal("H1", result.MatchedTf);
    }

    [Fact]
    public void RiskZone_Buy_UsesTradePlanEntryAndSl()
    {
        var (lo, hi) = EntrySlZoneGate.ComputeRiskZone(isBuy: true, entry: 100, sl: 97);
        Assert.Equal(97, lo, 6);
        Assert.Equal(100, hi, 6);
    }

    [Fact]
    public void RiskZone_Sell_UsesTradePlanEntryAndSl()
    {
        var (lo, hi) = EntrySlZoneGate.ComputeRiskZone(isBuy: false, entry: 100, sl: 103);
        Assert.Equal(100, lo, 6);
        Assert.Equal(103, hi, 6);
    }

    [Fact]
    public void Overlap_WithTolerance()
    {
        var pip = 0.01;
        var tol = 2 * pip;
        Assert.True(EntrySlZoneGate.Overlaps(99.8, 100.2, 100, 103, tol));
        Assert.False(EntrySlZoneGate.Overlaps(95, 96, 100, 103, 0));
    }

    [Fact]
    public void OverlapPips_Formula()
    {
        var pips = EntrySlZoneGate.ComputeOverlapPips(99, 101, 100, 103, 0.01);
        Assert.Equal(100, pips, 6); // 1.0 / 0.01
    }

    [Fact]
    public void BrokenSwingLow_IsGreen()
    {
        var s = StateWithKeylevel(type: -1, flag: 2, top: 100, bottom: 99);
        var zones = MultiTfZoneSnapshot.Collect(s, "15", Cfg(kl: false, ob: false));
        var z = Assert.Single(zones);
        Assert.Equal(ZoneSourceKind.BrokenKeyLevel, z.Source);
        Assert.Equal(ZoneEffectiveColor.Red, z.EffectiveColor);
        Assert.Equal(BrokenSwingType.SwingLow, z.BrokenSwing);
    }

    [Fact]
    public void BrokenSwingHigh_IsRed()
    {
        var s = StateWithKeylevel(type: 1, flag: -3, top: 101, bottom: 100);
        var zones = MultiTfZoneSnapshot.Collect(s, "15", Cfg(kl: false, ob: false));
        var z = Assert.Single(zones);
        Assert.Equal(ZoneSourceKind.BrokenKeyLevel, z.Source);
        Assert.Equal(ZoneEffectiveColor.Green, z.EffectiveColor);
        Assert.Equal(BrokenSwingType.SwingHigh, z.BrokenSwing);
    }

    [Fact]
    public void Ob_RequiresZinAndExtending()
    {
        var s = new PineStateEngine();
        AddOb(s, type: -1, top: 100.5, bottom: 99.5, state: 1, extending: true);
        AddOb(s, type: -1, top: 100.5, bottom: 99.5, state: 2, extending: false);
        AddOb(s, type: -1, top: 100.5, bottom: 99.5, state: 2, extending: true);

        var zones = MultiTfZoneSnapshot.Collect(s, "15", Cfg(kl: false, broken: false));
        Assert.Single(zones);
        Assert.Equal(ZoneSourceKind.OrderBlock, zones[0].Source);
    }

    [Fact]
    public void Mask_DefaultCondM5NgM15()
    {
        var slots = EntrySlZoneGateConfig.ParseRulesMask("condM5,ngM15");
        Assert.Equal(new[] { 0, 1, 4, 5 }.OrderBy(x => x), slots.OrderBy(x => x));
    }

    [Fact]
    public void Mask_HlSlotsNotIncluded()
    {
        var slots = EntrySlZoneGateConfig.ParseRulesMask("condM5,ngM15");
        Assert.DoesNotContain(2, slots);
        Assert.DoesNotContain(3, slots);
    }

    [Fact]
    public void Mask_ExplicitRNumbers()
    {
        var slots = EntrySlZoneGateConfig.ParseRulesMask("1,2,5,6");
        Assert.Equal(new[] { 0, 1, 4, 5 }.OrderBy(x => x), slots.OrderBy(x => x));
    }

    [Fact]
    public void ActiveKeyLevel_GreenForLowSwing()
    {
        var s = StateWithKeylevel(type: -1, flag: 1, top: 100, bottom: 99);
        var zones = MultiTfZoneSnapshot.Collect(s, "15", Cfg(ob: false, broken: false));
        var z = Assert.Single(zones);
        Assert.Equal(ZoneSourceKind.KeyLevel, z.Source);
        Assert.Equal(ZoneEffectiveColor.Green, z.EffectiveColor);
    }

    [Fact]
    public void GateConfig_AppliesToSlot()
    {
        var cfg = new EntrySlZoneGateConfig { GatedSlotIndices = new HashSet<int> { 0, 1, 4, 5 } };
        Assert.True(cfg.AppliesToSlot(0));
        Assert.True(cfg.AppliesToSlot(5));
        Assert.False(cfg.AppliesToSlot(2));
    }

    [Fact]
    public void Gate_DoesNotPassOnOwnSwing1KeylevelOnly()
    {
        var m15 = StateWithKeylevel(type: -1, flag: 1, top: 100, bottom: 99, bar: 50);
        var plan = BuyPlan(entry: 100, sl: 97, bBar: 50, swingIdx: 0);
        var cfg = Cfg(ob: false, broken: false);

        var result = EntrySlZoneGate.EvaluateWithStates(
            in plan,
            new Dictionary<string, PineStateEngine> { ["15"] = m15 },
            in cfg);

        Assert.False(result.Passed);
        Assert.Equal(1, result.OwnSwingBSkipped);
        Assert.Equal(0, result.OverlappingAfterExclusion);
    }

    [Fact]
    public void Gate_PassesOnDifferentSameColorZone()
    {
        var m15 = StateWithKeylevel(type: -1, flag: 1, top: 100, bottom: 99, bar: 50);
        var h1 = new PineStateEngine();
        AddOb(h1, type: -1, top: 100.5, bottom: 98.5);

        var plan = BuyPlan(entry: 100, sl: 97, bBar: 50, swingIdx: 0);
        var cfg = new EntrySlZoneGateConfig
        {
            TfTokens = new[] { "15", "60" },
            IncludeKeyLevels = true,
            IncludeOrderBlocks = true,
            IncludeBrokenKeyLevels = false,
            RequireSameColor = true,
            ExcludeSetupSwingBKeyLevel = true,
            PipSize = 0.01,
        };

        var result = EntrySlZoneGate.EvaluateWithStates(
            in plan,
            new Dictionary<string, PineStateEngine>
            {
                ["15"] = m15,
                ["60"] = h1,
            },
            in cfg);

        Assert.True(result.Passed);
        Assert.Equal(1, result.OwnSwingBSkipped);
        Assert.Equal(1, result.OverlappingAfterExclusion);
        Assert.Equal("H1", result.MatchedTf);
        Assert.Equal(ZoneSourceKind.OrderBlock, result.MatchedSource);
    }

    [Fact]
    public void IsSetupSwingBKeyLevel_SkipsMatchingPivotOnSameTf()
    {
        var plan = BuyPlan(100, 97, bBar: 50, swingIdx: 0);
        var cfg = Cfg();
        var candidate = new ZoneCandidate
        {
            TfToken = "15",
            Source = ZoneSourceKind.KeyLevel,
            PivotBar = 50,
            PivotIndex = 0,
        };

        Assert.True(EntrySlZoneGate.IsSetupSwingBKeyLevel(in plan, in candidate, in cfg));
    }

    [Fact]
    public void IsSetupSwingBKeyLevel_ExcludesByPriceBoxWhenPivotBarDiffers()
    {
        var plan = BuyPlan(100, 97, bBar: 50, swingIdx: 0);
        var cfg = Cfg();
        var candidate = new ZoneCandidate
        {
            TfToken = "15",
            Source = ZoneSourceKind.KeyLevel,
            PivotBar = 999,
            PivotIndex = 99,
            Low = 99,
            High = 100,
        };

        Assert.True(EntrySlZoneGate.IsSetupSwingBKeyLevel(in plan, in candidate, in cfg));
    }

    [Fact]
    public void Gate_DoesNotPassOnOwnSwing1KeylevelOnly_ByPriceBox()
    {
        var m15 = StateWithKeylevel(type: -1, flag: 1, top: 100, bottom: 99, bar: 999);
        var plan = BuyPlan(entry: 100, sl: 97, bBar: 50, swingIdx: 0);
        var cfg = Cfg(ob: false, broken: false);

        var result = EntrySlZoneGate.EvaluateWithStates(
            in plan,
            new Dictionary<string, PineStateEngine> { ["15"] = m15 },
            in cfg);

        Assert.False(result.Passed);
        Assert.Equal(1, result.OwnSwingBSkipped);
        Assert.Equal(0, result.OverlappingAfterExclusion);
    }

    [Fact]
    public void IsSetupSwingBKeyLevel_DoesNotSkipOrderBlock()
    {
        var plan = BuyPlan(100, 97, bBar: 50, swingIdx: 0);
        var cfg = Cfg();
        var candidate = new ZoneCandidate
        {
            TfToken = "15",
            Source = ZoneSourceKind.OrderBlock,
            PivotBar = 50,
        };

        Assert.False(EntrySlZoneGate.IsSetupSwingBKeyLevel(in plan, in candidate, in cfg));
    }

    [Fact]
    public void IsM5NonObConfluence_SkipsM5KeyLevel()
    {
        var cfg = Cfg();
        var kl = new ZoneCandidate { TfToken = "5", Source = ZoneSourceKind.KeyLevel };
        var ob = new ZoneCandidate { TfToken = "5", Source = ZoneSourceKind.OrderBlock };
        var m15Kl = new ZoneCandidate { TfToken = "15", Source = ZoneSourceKind.KeyLevel };

        Assert.True(EntrySlZoneGate.IsM5NonObConfluence(in kl, in cfg));
        Assert.False(EntrySlZoneGate.IsM5NonObConfluence(in ob, in cfg));
        Assert.False(EntrySlZoneGate.IsM5NonObConfluence(in m15Kl, in cfg));
    }

    [Fact]
    public void Gate_DoesNotPassOnM5KeyLevelOnly_WhenM5ObOnly()
    {
        var m15 = StateWithKeylevel(type: -1, flag: 1, top: 100, bottom: 99, bar: 50);
        var m5 = StateWithKeylevel(type: -1, flag: 1, top: 100, bottom: 99.5, bar: 10);

        var plan = BuyPlan(entry: 100, sl: 97, bBar: 50, swingIdx: 0);
        var cfg = new EntrySlZoneGateConfig
        {
            TfTokens = new[] { "15", "5" },
            IncludeKeyLevels = true,
            IncludeOrderBlocks = false,
            IncludeBrokenKeyLevels = false,
            RequireSameColor = true,
            ExcludeSetupSwingBKeyLevel = true,
            M5ConfluenceObOnly = true,
            PipSize = 0.01,
        };

        var result = EntrySlZoneGate.EvaluateWithStates(
            in plan,
            new Dictionary<string, PineStateEngine> { ["15"] = m15, ["5"] = m5 },
            in cfg);

        Assert.False(result.Passed);
        Assert.Equal(1, result.OwnSwingBSkipped);
        Assert.True(result.M5NonObSkipped >= 1);
        Assert.Equal(0, result.OverlappingAfterExclusion);
    }

    [Fact]
    public void Gate_PassesOnM5Ob_WhenM5ObOnly()
    {
        var m15 = StateWithKeylevel(type: -1, flag: 1, top: 100, bottom: 99, bar: 50);
        var m5 = new PineStateEngine();
        AddOb(m5, type: -1, top: 100.5, bottom: 98.5);

        var plan = BuyPlan(entry: 100, sl: 97, bBar: 50, swingIdx: 0);
        var cfg = new EntrySlZoneGateConfig
        {
            TfTokens = new[] { "15", "5" },
            IncludeKeyLevels = true,
            IncludeOrderBlocks = true,
            IncludeBrokenKeyLevels = false,
            RequireSameColor = true,
            ExcludeSetupSwingBKeyLevel = true,
            M5ConfluenceObOnly = true,
            PipSize = 0.01,
        };

        var result = EntrySlZoneGate.EvaluateWithStates(
            in plan,
            new Dictionary<string, PineStateEngine> { ["15"] = m15, ["5"] = m5 },
            in cfg);

        Assert.True(result.Passed);
        Assert.Equal(1, result.OwnSwingBSkipped);
        Assert.Equal("M5", result.MatchedTf);
        Assert.Equal(ZoneSourceKind.OrderBlock, result.MatchedSource);
    }
}
