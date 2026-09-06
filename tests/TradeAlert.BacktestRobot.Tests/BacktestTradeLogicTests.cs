using System;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.Alerts;
using TradeAlert.Core.Models.KeyLevel;
using TradeAlert.BacktestRobot.Execution;
using TradeAlert.BacktestRobot.Execution.Kl;
using Xunit;
using static TradeAlert.BacktestRobot.Execution.Kl.KlAssetType;

namespace TradeAlert.BacktestRobot.Tests;

public class BacktestTradeLogicTests
{
    // -- Fixtures ------------------------------------------------------------
    static PineStateEngine StateWithSwing(int type, int bar, double keyTop, double keyBottom, int flag = 1)
    {
        var s = new PineStateEngine();
        var idx = s.Pivots.PushPivot(price: keyBottom, barIndex: bar, type: type, timeMs: 0, highId: 1, lowId: 1);
        s.Pivots.SetFlag(idx, flag);
        s.Pivots.SetHasKey(idx, true);
        s.Pivots.SetKeyBox(idx, new KeyBoxRef { Spec = new KeyBoxSpec { Top = keyTop, Bottom = keyBottom } });
        return s;
    }

    /// <summary>Add a pivot with full state controls so tests can build mixed flag/role scenarios.</summary>
    static int AddPivot(
        PineStateEngine s,
        int type,
        int bar,
        double keyTop,
        double keyBottom,
        int flag = 1,
        int mainRole = 0,
        bool keyExtending = true)
    {
        var idx = s.Pivots.PushPivot(price: keyBottom, barIndex: bar, type: type, timeMs: 0, highId: 1, lowId: 1);
        s.Pivots.SetFlag(idx, flag);
        s.Pivots.SetMainRole(idx, mainRole);
        s.Pivots.SetHasKey(idx, true);
        s.Pivots.SetKeyExtending(idx, keyExtending);
        s.Pivots.SetKeyBox(idx, new KeyBoxRef { Spec = new KeyBoxSpec { Top = keyTop, Bottom = keyBottom } });
        return idx;
    }

    static void AddOb(PineStateEngine s, int type, double top, double bottom, int owner, int state = 2, bool extending = true)
    {
        s.ObPool.Push(state: state, count: 1, x: 0, bar: 10, typ: type, owner: owner,
            source: 0, number: 1, pivotType: type, pivotHighId: 1, pivotLowId: 1);
        var i = s.ObPool.Count - 1;
        s.ObPool.SetExtending(i, extending);
        s.ObPool.SetBox(i, new ObBoxSpec { Top = top, Bottom = bottom, ObType = type });
    }

    static TradeMapperConfig XauCfg(double spreadPips = 0) => new()
    {
        SymbolName = "XAUUSD",
        TickSize = 0.01,
        PipSize = 0.01,
        SpreadPips = spreadPips,
        AccountBalanceFtmo = 100_000,
        RiskPercent = 1,
        RoundPrecision = 1000,
        RewardRisk = 1.5,
        SlWidthMult = 2,
        AssetType = KlAssetType.XAUUSD,
        CustomContractSize = 100,
        IsCryptoSymbol = false,
        QuoteCurrency = "USD",
        BaseAssetName = "XAU",
        ConvUsdPerQuote = 1,
        LabelPrefix = "L6BT|",
        UseSwingCEdgeTakeProfit = false,
        AtrSlWidthAdjust = new AtrSlWidthAdjustConfig { UseAtrAdjustedSlWidthMultiplier = false },
    };

    static CompoundFireEvent Fire(SignalDirection dir, int slot = 0, int bar = 100) =>
        new(slot, $"R{slot + 1}", dir, "15", bar, DateTime.UnixEpoch, 100.0, bar);

    // -- Rule 1: Swing-1 gate ------------------------------------------------
    [Fact]
    public void Resolve_Buy_RequiresLowSwing()
    {
        var s = StateWithSwing(type: SwingBResolver.TypeLow, bar: 50, keyTop: 100, keyBottom: 99);
        var b = SwingBResolver.Resolve(s, isBuy: true, out _);
        Assert.NotNull(b);
        Assert.True(b!.IsLow);
        Assert.Equal(50, b.PivotBar);
    }

    [Fact]
    public void Resolve_Buy_RejectsHighSwing()
    {
        var s = StateWithSwing(type: SwingBResolver.TypeHigh, bar: 50, keyTop: 101, keyBottom: 100);
        var b = SwingBResolver.Resolve(s, isBuy: true, out var reason);
        Assert.Null(b);
        Assert.Contains("not swing LOW", reason);
    }

    [Fact]
    public void Resolve_PicksMostRecentActiveSwing()
    {
        var s = new PineStateEngine();
        var older = s.Pivots.PushPivot(99, 40, SwingBResolver.TypeHigh, 0, 1, 1);
        s.Pivots.SetFlag(older, 1);
        s.Pivots.SetHasKey(older, true);
        s.Pivots.SetKeyBox(older, new KeyBoxRef { Spec = new KeyBoxSpec { Top = 105, Bottom = 104 } });

        var newer = s.Pivots.PushPivot(99, 60, SwingBResolver.TypeLow, 0, 1, 1);
        s.Pivots.SetFlag(newer, 1);
        s.Pivots.SetHasKey(newer, true);
        s.Pivots.SetKeyBox(newer, new KeyBoxRef { Spec = new KeyBoxSpec { Top = 100, Bottom = 99 } });

        var b = SwingBResolver.Resolve(s, isBuy: true, out _);
        Assert.NotNull(b);
        Assert.Equal(60, b!.PivotBar);
    }

    [Fact]
    public void Resolve_NoKeylevel_ReturnsNull()
    {
        var s = new PineStateEngine();
        var idx = s.Pivots.PushPivot(99, 50, SwingBResolver.TypeLow, 0, 1, 1);
        s.Pivots.SetFlag(idx, 1);
        // no key box
        var b = SwingBResolver.Resolve(s, isBuy: true, out var reason);
        Assert.Null(b);
        Assert.Contains("no keylevel", reason);
    }

    [Fact]
    public void Resolve_Sell_AcceptsDSwingHigh()
    {
        var s = StateWithSwing(
            type: SwingBResolver.TypeHigh, bar: 70, keyTop: 102, keyBottom: 101,
            flag: SwingBResolver.FlagDSwing);
        var b = SwingBResolver.Resolve(s, isBuy: false, out var reason);
        Assert.NotNull(b);
        Assert.Equal(SwingBSource.DSwing, b!.Source);
        Assert.True(b.IsHigh);
        Assert.Equal("ok", reason);
    }

    [Fact]
    public void Resolve_Sell_PicksDSwingHigh_WhenLatestActiveIsWrongType()
    {
        // NG-style: newest ACTIVE is LOW but an older D_SWING HIGH is valid for SELL.
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeHigh, bar: 220, keyTop: 106, keyBottom: 105,
                 flag: SwingBResolver.FlagDSwing);
        AddPivot(s, SwingBResolver.TypeLow, bar: 227, keyTop: 100, keyBottom: 99,
                 flag: SwingBResolver.FlagActive);

        var b = SwingBResolver.Resolve(s, isBuy: false, out _);
        Assert.NotNull(b);
        Assert.Equal(SwingBSource.DSwing, b!.Source);
        Assert.Equal(220, b.PivotBar);
    }

    [Fact]
    public void Resolve_PrefersNewerActiveOverOlderDSwing()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeHigh, bar: 50, keyTop: 105, keyBottom: 104,
                 flag: SwingBResolver.FlagDSwing);
        AddPivot(s, SwingBResolver.TypeHigh, bar: 70, keyTop: 102, keyBottom: 101,
                 flag: SwingBResolver.FlagActive);

        var b = SwingBResolver.Resolve(s, isBuy: false, out _);
        Assert.NotNull(b);
        Assert.Equal(SwingBSource.Active, b!.Source);
        Assert.Equal(70, b.PivotBar);
    }

    [Fact]
    public void Mapper_R6SellSlot_UsesDSwing_WhenLatestActiveWrongType()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeHigh, bar: 220, keyTop: 106, keyBottom: 105,
                 flag: SwingBResolver.FlagDSwing);
        AddPivot(s, SwingBResolver.TypeLow, bar: 227, keyTop: 100, keyBottom: 99,
                 flag: SwingBResolver.FlagActive);

        var plan = CompoundFireTradeMapper.TryBuild(Fire(SignalDirection.Sell, slot: 5), s, XauCfg(), out var reason);
        Assert.NotNull(plan);
        Assert.Equal("ok", reason);
        Assert.Contains("[B=D_SWING]", plan!.Reason);
    }

    // -- Rule 1: MAIN_C fallback (NG M15 — R5/R6) ----------------------------
    [Fact]
    public void Resolve_NoFallback_NoActiveSwing_ReturnsNoActive()
    {
        // Only MAIN_C pivot present, no ACTIVE. Without fallback flag, returns "no active swing".
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, bar: 50, keyTop: 100, keyBottom: 99,
                 flag: SwingBResolver.FlagMainC, mainRole: 1);

        var b = SwingBResolver.Resolve(s, isBuy: true, allowMainCFallback: false, out var reason);
        Assert.Null(b);
        Assert.Equal("no active swing", reason);
    }

    [Fact]
    public void Resolve_MainCFallback_BuyTakesMainCLow_WhenNoActive()
    {
        // No ACTIVE swing exists, but a MAIN_C low does ? BUY accepts it as B.
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, bar: 50, keyTop: 100, keyBottom: 99,
                 flag: SwingBResolver.FlagMainC, mainRole: 1);

        var b = SwingBResolver.Resolve(s, isBuy: true, allowMainCFallback: true, out var reason);
        Assert.NotNull(b);
        Assert.Equal(SwingBSource.MainCFallback, b!.Source);
        Assert.Equal(50, b.PivotBar);
        Assert.True(b.IsLow);
        Assert.Contains("MAIN_C fallback", reason);
    }

    [Fact]
    public void Resolve_MainCFallback_SellTakesMainCHigh_WhenNoActive()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeHigh, bar: 60, keyTop: 101, keyBottom: 100,
                 flag: SwingBResolver.FlagMainC, mainRole: 1);

        var b = SwingBResolver.Resolve(s, isBuy: false, allowMainCFallback: true, out var reason);
        Assert.NotNull(b);
        Assert.Equal(SwingBSource.MainCFallback, b!.Source);
        Assert.True(b.IsHigh);
        Assert.Contains("MAIN_C fallback", reason);
    }

    [Fact]
    public void Resolve_MainCFallback_PrefersActive_WhenBothExist()
    {
        // ACTIVE wins even when MAIN_C exists.
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, bar: 40, keyTop: 100, keyBottom: 99,
                 flag: SwingBResolver.FlagMainC, mainRole: 1);
        AddPivot(s, SwingBResolver.TypeLow, bar: 60, keyTop: 102, keyBottom: 101,
                 flag: SwingBResolver.FlagActive);

        var b = SwingBResolver.Resolve(s, isBuy: true, allowMainCFallback: true, out var reason);
        Assert.NotNull(b);
        Assert.Equal(SwingBSource.Active, b!.Source);
        Assert.Equal(60, b.PivotBar);
        Assert.Equal("ok", reason);
    }

    [Fact]
    public void Resolve_MainCFallback_PicksMostRecentMainCOfCorrectDirection()
    {
        // Older MAIN_C low + newer MAIN_C high; BUY (need LOW) gets the older one because newer is wrong direction.
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow,  bar: 40, keyTop: 100, keyBottom: 99,
                 flag: SwingBResolver.FlagMainC, mainRole: 1);
        AddPivot(s, SwingBResolver.TypeHigh, bar: 60, keyTop: 105, keyBottom: 104,
                 flag: SwingBResolver.FlagMainC, mainRole: 1);

        var b = SwingBResolver.Resolve(s, isBuy: true, allowMainCFallback: true, out _);
        Assert.NotNull(b);
        Assert.Equal(40, b!.PivotBar);
        Assert.True(b.IsLow);
    }

    [Fact]
    public void Resolve_MainCFallback_SkipsClosedKeyAndPicksOlderExtending()
    {
        // Newer MAIN_C low has key not extending ? skip, take the older extending one.
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, bar: 40, keyTop: 100, keyBottom: 99,
                 flag: SwingBResolver.FlagMainC, mainRole: 1, keyExtending: true);
        AddPivot(s, SwingBResolver.TypeLow, bar: 60, keyTop: 102, keyBottom: 101,
                 flag: SwingBResolver.FlagMainC, mainRole: 1, keyExtending: false);

        var b = SwingBResolver.Resolve(s, isBuy: true, allowMainCFallback: true, out _);
        Assert.NotNull(b);
        Assert.Equal(40, b!.PivotBar);
    }

    [Fact]
    public void Resolve_MainCFallback_RejectsMainCWithoutMainRoleFlag()
    {
        // flag=0 but mainRole=0 ? not MAIN_C (could be a generic non-active pivot), do not accept.
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, bar: 50, keyTop: 100, keyBottom: 99,
                 flag: SwingBResolver.FlagMainC, mainRole: 0);

        var b = SwingBResolver.Resolve(s, isBuy: true, allowMainCFallback: true, out var reason);
        Assert.Null(b);
        Assert.Contains("no MAIN_C", reason);
    }

    [Fact]
    public void Resolve_MainCFallback_NoCandidates_ReturnsCombinedReason()
    {
        // Empty state — both passes find nothing.
        var s = new PineStateEngine();
        var b = SwingBResolver.Resolve(s, isBuy: true, allowMainCFallback: true, out var reason);
        Assert.Null(b);
        Assert.Contains("no active swing", reason);
        Assert.Contains("no MAIN_C", reason);
    }

    [Fact]
    public void Mapper_R5BuySlot_UsesMainCFallback_WhenNoActiveLow()
    {
        // R5 = slot 4 (NG M15 buy). Only a MAIN_C low exists ? mapper should still build a plan.
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, bar: 50, keyTop: 100, keyBottom: 99,
                 flag: SwingBResolver.FlagMainC, mainRole: 1);

        var plan = CompoundFireTradeMapper.TryBuild(Fire(SignalDirection.Buy, slot: 4), s, XauCfg(), out var reason);
        Assert.NotNull(plan);
        Assert.Equal("ok", reason);
        Assert.Equal(100.0, plan!.EntryLimit, 6);
        Assert.Contains("[B=MAIN_C]", plan.Reason);
    }

    [Fact]
    public void Mapper_R1BuySlot_DoesNotUseMainCFallback_WhenNoActiveLow()
    {
        // R1 = slot 0 (M5 buy). Same MAIN_C-only state ? mapper should refuse (back-compat for R1–R4).
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeLow, bar: 50, keyTop: 100, keyBottom: 99,
                 flag: SwingBResolver.FlagMainC, mainRole: 1);

        var plan = CompoundFireTradeMapper.TryBuild(Fire(SignalDirection.Buy, slot: 0), s, XauCfg(), out var reason);
        Assert.Null(plan);
        Assert.Equal("no active swing", reason);
    }

    [Fact]
    public void Mapper_R6SellSlot_UsesMainCFallback_WhenNoActiveHigh()
    {
        var s = new PineStateEngine();
        AddPivot(s, SwingBResolver.TypeHigh, bar: 70, keyTop: 102, keyBottom: 101,
                 flag: SwingBResolver.FlagMainC, mainRole: 1);

        var plan = CompoundFireTradeMapper.TryBuild(Fire(SignalDirection.Sell, slot: 5), s, XauCfg(), out var reason);
        Assert.NotNull(plan);
        Assert.Equal("ok", reason);
        Assert.False(plan!.IsBuy);
        Assert.Contains("[B=MAIN_C]", plan.Reason);
    }

    // -- Rules 2-4: mapper SL / entry / TP -----------------------------------
    [Fact]
    public void Mapper_Buy_DefaultEntryAtKeyTop()
    {
        var s = StateWithSwing(SwingBResolver.TypeLow, 50, keyTop: 100, keyBottom: 99);
        var plan = CompoundFireTradeMapper.TryBuild(Fire(SignalDirection.Buy), s, XauCfg(), out var reason);

        Assert.NotNull(plan);
        Assert.Equal("ok", reason);
        Assert.True(plan!.IsBuy);
        Assert.Equal(100.0, plan.EntryLimit, 6);   // kT
        Assert.Equal(98.0, plan.StopLoss, 6);       // entry - w*2
        Assert.Equal(103.0, plan.TakeProfit, 6);    // entry + 1.5*R, R=2
        Assert.Equal(200.0, plan.StopLossPips, 6);
        Assert.Equal(300.0, plan.TakeProfitPips, 6);
        Assert.Equal(new TradeDedupKey(0, 50), plan.DedupKey);
    }

    [Fact]
    public void Mapper_Sell_DefaultEntryAtKeyBottom()
    {
        var s = StateWithSwing(SwingBResolver.TypeHigh, 50, keyTop: 101, keyBottom: 100);
        var plan = CompoundFireTradeMapper.TryBuild(Fire(SignalDirection.Sell), s, XauCfg(), out _);

        Assert.NotNull(plan);
        Assert.False(plan!.IsBuy);
        Assert.Equal(100.0, plan.EntryLimit, 6);   // kB
        Assert.Equal(102.0, plan.StopLoss, 6);      // entry + w*2
        Assert.Equal(97.0, plan.TakeProfit, 6);     // entry - 1.5*R, R=2
    }

    [Fact]
    public void Mapper_Buy_ObOverrideRaisesEntry()
    {
        var s = StateWithSwing(SwingBResolver.TypeLow, 50, keyTop: 100, keyBottom: 99);
        // green OB (type=-1) overlapping keylevel, top above kT
        AddOb(s, type: -1, top: 100.5, bottom: 99.8, owner: 0);

        var plan = CompoundFireTradeMapper.TryBuild(Fire(SignalDirection.Buy), s, XauCfg(), out _);

        Assert.NotNull(plan);
        Assert.Equal(100.5, plan!.EntryLimit, 6);   // OB.Top
        Assert.Equal(98.0, plan.StopLoss, 6);        // swing-X edge anchor (default SlFromEntry=false)
        Assert.Equal(104.25, plan.TakeProfit, 6);   // entry + 1.5 * (entry - sl) = 100.5 + 1.5*2.5
    }

    [Fact]
    public void Mapper_Buy_ObOverride_SlFromEntry_AnchorsSlToEntry()
    {
        var s = StateWithSwing(SwingBResolver.TypeLow, 50, keyTop: 100, keyBottom: 99);
        AddOb(s, type: -1, top: 100.5, bottom: 99.8, owner: 0);

        var cfg = XauCfg() with { SlFromEntry = true };
        var plan = CompoundFireTradeMapper.TryBuild(Fire(SignalDirection.Buy), s, cfg, out _);

        Assert.NotNull(plan);
        Assert.Equal(100.5, plan!.EntryLimit, 6);
        Assert.Equal(98.5, plan.StopLoss, 6);       // entry - w*2
        Assert.Equal(103.5, plan.TakeProfit, 6);    // entry + 1.5*2
    }

    [Fact]
    public void Mapper_Buy_WrongColourObIgnored()
    {
        var s = StateWithSwing(SwingBResolver.TypeLow, 50, keyTop: 100, keyBottom: 99);
        // red OB (type=1) should NOT override a buy (needs green/-1)
        AddOb(s, type: 1, top: 100.5, bottom: 99.8, owner: 0);

        var plan = CompoundFireTradeMapper.TryBuild(Fire(SignalDirection.Buy), s, XauCfg(), out _);

        Assert.NotNull(plan);
        Assert.Equal(100.0, plan!.EntryLimit, 6);   // default kT, OB ignored
    }

    [Fact]
    public void Mapper_DirectionGateMismatch_ReturnsNull()
    {
        var s = StateWithSwing(SwingBResolver.TypeHigh, 50, keyTop: 101, keyBottom: 100);
        var plan = CompoundFireTradeMapper.TryBuild(Fire(SignalDirection.Buy), s, XauCfg(), out var reason);
        Assert.Null(plan);
        Assert.Contains("not swing LOW", reason);
    }

    [Fact]
    public void Mapper_SpreadAdjust_ShiftsBuyEntryUp_WhenEnabled()
    {
        var s = StateWithSwing(SwingBResolver.TypeLow, 50, keyTop: 100, keyBottom: 99);
        // RecalcTpAfterSpread=true ? spread shift is applied ? entry += spread (10 pips * 0.01 = 0.1)
        var cfg = XauCfg(spreadPips: 10) with { RecalcTpAfterSpread = true };
        var plan = CompoundFireTradeMapper.TryBuild(Fire(SignalDirection.Buy), s, cfg, out _);
        Assert.NotNull(plan);
        Assert.Equal(100.1, plan!.EntryLimit, 6);
    }

    [Fact]
    public void Mapper_SpreadAdjust_NoShift_WhenDisabled()
    {
        var s = StateWithSwing(SwingBResolver.TypeLow, 50, keyTop: 100, keyBottom: 99);
        // RecalcTpAfterSpread=false (default) ? no spread shift ? entry stays at bid KeyTop
        var plan = CompoundFireTradeMapper.TryBuild(Fire(SignalDirection.Buy), s, XauCfg(spreadPips: 10), out _);
        Assert.NotNull(plan);
        Assert.Equal(100.0, plan!.EntryLimit, 6);
    }

    // -- Exit: Swing-1 broken ------------------------------------------------
    [Fact]
    public void Broken_ActiveSwing_NotBroken()
    {
        var s = StateWithSwing(SwingBResolver.TypeLow, 50, 100, 99);
        Assert.False(SwingBrokenChecker.IsBroken(s, 50));
    }

    [Fact]
    public void Broken_FlagLeftActive_IsBroken()
    {
        var s = StateWithSwing(SwingBResolver.TypeLow, 50, 100, 99, flag: 2);
        Assert.True(SwingBrokenChecker.IsBroken(s, 50));
    }

    [Fact]
    public void Broken_KeyBreakLabeled_IsBroken()
    {
        var s = StateWithSwing(SwingBResolver.TypeLow, 50, 100, 99);
        s.Pivots.SetKeyBreakLabeled(0, true);
        Assert.True(SwingBrokenChecker.IsBroken(s, 50));
    }

    [Fact]
    public void Broken_UnknownBar_NotBroken()
    {
        var s = StateWithSwing(SwingBResolver.TypeLow, 50, 100, 99);
        Assert.False(SwingBrokenChecker.IsBroken(s, 999));
    }

    // -- Dedup / position book -----------------------------------------------
    [Fact]
    public void Dedup_SameSlotAndBar_AreEqual()
    {
        Assert.Equal(new TradeDedupKey(2, 77), new TradeDedupKey(2, 77));
        Assert.NotEqual(new TradeDedupKey(2, 77), new TradeDedupKey(3, 77));
        Assert.NotEqual(new TradeDedupKey(2, 77), new TradeDedupKey(2, 78));
    }

    // -- SymbolSizingProfile auto-detect ------------------------------------
    [Theory]
    [InlineData("XAUUSD",     KlAssetType.XAUUSD, 100,     "USD", false)]
    [InlineData("XAGUSD",     KlAssetType.XAUUSD, 100,     "USD", false)]
    [InlineData("GOLD",       KlAssetType.XAUUSD, 100,     "USD", false)]
    [InlineData("EURUSD",     KlAssetType.Forex,  100000,  "USD", false)]
    [InlineData("AUDCAD",     KlAssetType.Forex,  100000,  "CAD", false)]
    [InlineData("EURGBP",     KlAssetType.Forex,  100000,  "GBP", false)]
    [InlineData("USDJPY",     KlAssetType.Forex,  100000,  "JPY", false)]
    [InlineData("GBPNZD",     KlAssetType.Forex,  100000,  "NZD", false)]
    [InlineData("BTCUSD",     KlAssetType.BTCUSD, 1,       "USD", true)]
    [InlineData("ETHUSD",     KlAssetType.Custom, 1,       "USD", true)]
    [InlineData("BNBUSD",     KlAssetType.Custom, 1,       "USD", true)]
    [InlineData("GERMANY40",  KlAssetType.Custom, 1,       "EUR", false)]
    [InlineData("GER40",      KlAssetType.Custom, 1,       "EUR", false)]
    [InlineData("DAX",        KlAssetType.Custom, 1,       "EUR", false)]
    [InlineData("US100",      KlAssetType.Custom, 1,       "USD", false)]
    [InlineData("NAS100",     KlAssetType.Custom, 1,       "USD", false)]
    [InlineData("UK100",      KlAssetType.Custom, 1,       "GBP", false)]
    [InlineData("JPN225",     KlAssetType.Custom, 1,       "JPY", false)]
    [InlineData("AUS200",     KlAssetType.Custom, 1,       "AUD", false)]
    public void Profile_Detect_Correct(string sym, KlAssetType wantType, int wantContract, string wantQuote, bool wantCrypto)
    {
        var p = SymbolSizingProfile.Detect(sym);
        Assert.Equal(wantType,     p.AssetType);
        Assert.Equal(wantContract, p.ContractSize);
        Assert.Equal(wantQuote,    p.QuoteCurrency);
        Assert.Equal(wantCrypto,   p.IsCrypto);
    }

    [Fact]
    public void Profile_Override_ReplacesFields()
    {
        var p = SymbolSizingProfile.Detect("EURGBP");
        Assert.Equal(KlAssetType.Forex, p.AssetType);
        Assert.Equal("GBP", p.QuoteCurrency);

        // user overrides contract size only
        var p2 = p.WithOverrides(null, 50000, null, null, null);
        Assert.Equal(KlAssetType.Forex, p2.AssetType);  // unchanged
        Assert.Equal(50000, p2.ContractSize);            // overridden
        Assert.Equal("GBP", p2.QuoteCurrency);           // unchanged
    }

    [Fact]
    public void Book_HasDedup_TracksAndRemoves()
    {
        var book = new OpenPositionBook();
        var key = new TradeDedupKey(0, 50);
        book.Track(new PendingOrderContext
        {
            Label = "L6BT|R1|B50",
            DedupKey = key,
            SwingBPivotBar = 50,
            IsBuy = true,
            EntryPrice = 100,
            OriginalTakeProfit = 104.5,
        }, initialTakeProfit: 104.5);

        Assert.True(book.HasDedup(key));
        Assert.True(book.HasLabel("L6BT|R1|B50"));

        // reconcile drops stale labels
        book.RetainOnly(new System.Collections.Generic.HashSet<string>());
        Assert.False(book.HasDedup(key));
    }
}
