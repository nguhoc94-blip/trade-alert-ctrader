using System;
using TradeAlert.BacktestRobot.Execution;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.Alerts;
using TradeAlert.Core.Models.KeyLevel;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests;

public class SwingCWaitDiagnosticTests
{
    static TradeMapperConfig BaseCfg() => new()
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

    static SwingBResult BuyB(int bar = 50) => new()
    {
        PivotIndex = 0,
        PivotBar = bar,
        Type = SwingBResolver.TypeLow,
        KeyTop = 100,
        KeyBottom = 99,
        Source = SwingBSource.Active,
    };

    [Fact]
    public void IsAwaitingSwingCConfirmation_True_WhenNoPivotAfterB()
    {
        var s = new PineStateEngine();
        var cfg = BaseCfg();

        Assert.True(SwingCWaitDiagnostic.IsAwaitingSwingCConfirmation(
            s, BuyB(), SignalDirection.Buy, slotIndex: 0, in cfg));
    }

    [Fact]
    public void IsAwaitingSwingCConfirmation_True_WhenPivotExistsButNotConfirmed()
    {
        var s = new PineStateEngine();
        var cIdx = s.Pivots.PushPivot(105, 70, SwingBResolver.TypeHigh, 0, 1, 1);
        s.Pivots.SetFlag(cIdx, 0); // INIT — not ACTIVE/D_SWING

        var cfg = BaseCfg();

        Assert.True(SwingCWaitDiagnostic.IsAwaitingSwingCConfirmation(
            s, BuyB(), SignalDirection.Buy, slotIndex: 0, in cfg));
    }

    [Fact]
    public void Mapper_ConfirmedC_WrongTpSide_IsNotNoSwingCReason()
    {
        var s = new PineStateEngine();
        var bIdx = s.Pivots.PushPivot(99.5, 50, SwingBResolver.TypeLow, 0, 1, 1);
        s.Pivots.SetFlag(bIdx, SwingBResolver.FlagActive);
        s.Pivots.SetHasKey(bIdx, true);
        s.Pivots.SetKeyBox(bIdx, new KeyBoxRef { Spec = new KeyBoxSpec { Top = 100, Bottom = 99 } });

        // C confirmed but edge TP ends up on wrong side of provisional entry for BUY.
        var cIdx = s.Pivots.PushPivot(98.5, 60, SwingBResolver.TypeHigh, 0, 1, 1);
        s.Pivots.SetFlag(cIdx, SwingBResolver.FlagActive);
        s.Pivots.SetHasKey(cIdx, true);
        s.Pivots.SetKeyBox(cIdx, new KeyBoxRef { Spec = new KeyBoxSpec { Top = 99, Bottom = 98 } });

        var cfg = BaseCfg();
        var ev = new CompoundFireEvent(0, "R1", SignalDirection.Buy, "15", 100, DateTime.UnixEpoch, 100, 100);
        var pinnedB = new SwingBResult
        {
            PivotIndex = bIdx,
            PivotBar = 50,
            Type = SwingBResolver.TypeLow,
            KeyTop = 100,
            KeyBottom = 99,
            Source = SwingBSource.Active,
        };

        var plans = CompoundFireTradeMapper.TryBuildPlans(in ev, s, in cfg, out var reason, pinnedB: pinnedB);

        Assert.Empty(plans);
        Assert.DoesNotContain("no confirmed swing C", reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(SwingCWaitDiagnostic.IsAwaitingSwingCConfirmation(
            s, pinnedB, SignalDirection.Buy, slotIndex: 0, in cfg));
    }

    [Fact]
    public void PivotAudit_ListsOnlyPivotsAfterB_AndMarksPick()
    {
        var s = new PineStateEngine();
        var bIdx = s.Pivots.PushPivot(100, 50, SwingBResolver.TypeHigh, 0, 1, 1);
        s.Pivots.SetFlag(bIdx, SwingBResolver.FlagActive);

        var oldLow = s.Pivots.PushPivot(99.2, 44, SwingBResolver.TypeLow, 0, 1, 1);
        s.Pivots.SetFlag(oldLow, SwingCEdgeTakeProfitResolver.FlagDSwing);

        var initLow = s.Pivots.PushPivot(98.8, 52, SwingBResolver.TypeLow, 0, 1, 1);
        s.Pivots.SetFlag(initLow, 0);

        var pickLow = s.Pivots.PushPivot(98.5, 55, SwingBResolver.TypeLow, 0, 1, 1);
        s.Pivots.SetFlag(pickLow, SwingBResolver.FlagActive);

        var pinnedB = new SwingBResult
        {
            PivotIndex = bIdx,
            PivotBar = 50,
            Type = SwingBResolver.TypeHigh,
            KeyTop = 101,
            KeyBottom = 100,
            Source = SwingBSource.Active,
        };
        var cfg = BaseCfg();

        var rows = SwingCPivotAudit.CollectAfterB(s, pinnedB, SignalDirection.Sell, slotIndex: 3, in cfg);
        Assert.Equal(2, rows.Count);
        Assert.Equal(52, rows[0].Bar);
        Assert.False(rows[0].ValidCCandidate);
        Assert.Equal(55, rows[1].Bar);
        Assert.True(rows[1].ValidCCandidate);

        var log = SwingCPivotAudit.FormatLogBlock(
            s, pinnedB, SignalDirection.Sell, slotIndex: 3, in cfg,
            ruleSlot: 3, fireBar: 50, chartBar: 59, phase: "defer-exec", pickedCBar: 55);
        Assert.Contains("SWING-C-AUDIT R4 SELL", log);
        Assert.Contains("bar=55", log);
        Assert.Contains("<<PICKED", log);
        Assert.Contains("validC=N", log);
        Assert.DoesNotContain("bar=44", log);
    }
}
