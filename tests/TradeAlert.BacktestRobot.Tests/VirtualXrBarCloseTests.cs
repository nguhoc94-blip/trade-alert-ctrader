using System;
using TradeAlert.BacktestRobot.Execution;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests;

public sealed class VirtualXrBarCloseTests
{
    const double Entry = 2000;
    const double Sl = 1990;
    const double TriggerR = 1.25;
    static readonly double VirtualXrPrice = VirtualXrBarCloseEvaluator.ComputeVirtualXrPrice(Entry, Sl, TriggerR, isBuy: true);

    [Fact]
    public void Buy_HighTouchesXr_CloseBelowXr_NoConfirm()
    {
        var bar = new VirtualXrBarOhlc
        {
            OpenTime = DateTime.UtcNow,
            High = VirtualXrPrice + 1,
            Low = Entry - 1,
            Close = VirtualXrPrice - 0.5,
        };

        var signal = VirtualXrBarCloseEvaluator.EvaluateBar(in bar, VirtualXrPrice, isBuy: true);

        Assert.True(signal.Touched);
        Assert.False(signal.Confirmed);
    }

    [Fact]
    public void Buy_HighTouchesXr_CloseAboveXr_Confirmed()
    {
        var bar = new VirtualXrBarOhlc
        {
            OpenTime = DateTime.UtcNow,
            High = VirtualXrPrice + 2,
            Low = Entry - 1,
            Close = VirtualXrPrice + 0.5,
        };

        var signal = VirtualXrBarCloseEvaluator.EvaluateBar(in bar, VirtualXrPrice, isBuy: true);

        Assert.True(signal.Touched);
        Assert.True(signal.Confirmed);
    }

    [Fact]
    public void Sell_LowTouchesXr_CloseAboveXr_NoConfirm()
    {
        var sellEntry = 2000.0;
        var sellSl = 2010.0;
        var sellXr = VirtualXrBarCloseEvaluator.ComputeVirtualXrPrice(sellEntry, sellSl, TriggerR, isBuy: false);

        var bar = new VirtualXrBarOhlc
        {
            OpenTime = DateTime.UtcNow,
            High = sellEntry + 1,
            Low = sellXr - 1,
            Close = sellXr + 0.5,
        };

        var signal = VirtualXrBarCloseEvaluator.EvaluateBar(in bar, sellXr, isBuy: false);

        Assert.True(signal.Touched);
        Assert.False(signal.Confirmed);
    }

    [Fact]
    public void Sell_LowTouchesXr_CloseBelowXr_Confirmed()
    {
        var sellEntry = 2000.0;
        var sellSl = 2010.0;
        var sellXr = VirtualXrBarCloseEvaluator.ComputeVirtualXrPrice(sellEntry, sellSl, TriggerR, isBuy: false);

        var bar = new VirtualXrBarOhlc
        {
            OpenTime = DateTime.UtcNow,
            High = sellEntry + 1,
            Low = sellXr - 1,
            Close = sellXr - 0.5,
        };

        var signal = VirtualXrBarCloseEvaluator.EvaluateBar(in bar, sellXr, isBuy: false);

        Assert.True(signal.Touched);
        Assert.True(signal.Confirmed);
    }

    [Fact]
    public void XRBarClose_InternalR_UsesEffectiveClose_NotPlannedRr()
    {
        var effectiveClose = VirtualXrPrice + 0.25;
        var protection = new PositionProtectionSnapshot
        {
            IsBuy = true,
            Entry = Entry,
            StopLoss = Sl,
            TakeProfit = 2020,
            VolumeUnits = 80,
            PlannedRiskUsd = 1000,
            PlannedRr = 2.0,
            VirtualXrExitEnabled = true,
            VirtualXrEffectiveClose = effectiveClose,
        };
        var data = new ClosedTradeData
        {
            HasData = true,
            RawClose = effectiveClose + 5,
            GrossProfit = 900,
            Commission = -5,
            NetProfit = 895,
        };

        var score = InternalRScoreCalculator.Compute(
            in protection, in data, "XRBarClose", InternalPerformanceConfig.Default, pipSize: 0.01);

        var expectedGross = ExecutionPnLCalculator.SignedGross(Entry, effectiveClose, 80, isBuy: true);
        var expectedGrossR = expectedGross / 1000;

        Assert.Equal(expectedGrossR, score.InternalGrossR, 6);
        Assert.NotEqual(2.0, score.InternalGrossR, 3);
    }

    [Fact]
    public void InternalCloseReasonClassifier_StopLoss_StillWorksWithVirtualXr()
    {
        var protection = new PositionProtectionSnapshot
        {
            IsBuy = true,
            StopLoss = Sl,
            TakeProfit = 2020,
            VirtualXrExitEnabled = true,
            VirtualXrBrokerTpEnabled = false,
        };

        var reason = InternalCloseReasonClassifier.Classify(
            rawClose: Sl - 1,
            in protection,
            tolerancePips: 2,
            pipSize: 0.01,
            handlerReason: "XRBarClose");

        Assert.Equal("StopLoss", reason);
    }

    [Fact]
    public void InternalCloseReasonClassifier_BBrokenTpEntry_PriorityOverVirtualXrHandler()
    {
        var protection = new PositionProtectionSnapshot
        {
            IsBuy = true,
            StopLoss = Sl,
            TakeProfit = Entry,
            BBrokenMoveTpToEntryApplied = true,
            VirtualXrExitEnabled = true,
        };

        var reason = InternalCloseReasonClassifier.Classify(
            rawClose: Entry,
            in protection,
            tolerancePips: 2,
            pipSize: 0.01,
            handlerReason: "XRBarClose");

        Assert.Equal("BBrokenTpEntry", reason);
    }

    [Fact]
    public void InternalCloseReasonClassifier_VirtualXrHandlerReason_ReturnsXrBarClose()
    {
        var protection = new PositionProtectionSnapshot
        {
            IsBuy = true,
            StopLoss = Sl,
            TakeProfit = 2020,
            VirtualXrExitEnabled = true,
            VirtualXrEffectiveClose = VirtualXrPrice,
        };

        var reason = InternalCloseReasonClassifier.Classify(
            rawClose: VirtualXrPrice,
            in protection,
            tolerancePips: 2,
            pipSize: 0.01,
            handlerReason: "XRBarClose");

        Assert.Equal("XRBarClose", reason);
    }

    [Fact]
    public void VirtualXrConfig_ParseFillMode_AndCloseReason()
    {
        var cfg = VirtualXrConfig.Parse(true, 1.25, "15", "NextBarOpen", false, 0);
        Assert.Equal(VirtualXrFillMode.NextBarOpen, cfg.FillMode);
        Assert.Equal("XRBarCloseNextOpen", VirtualXrConfig.CloseReasonForFillMode(cfg.FillMode));
    }

    [Fact]
    public void AuditClose_XRBarClose_IncludesVirtualFieldsInCsv()
    {
        var tracker = new ExecutionAuditTracker();
        tracker.SetPipSize(0.01);
        var protection = new PositionProtectionSnapshot
        {
            IsBuy = true,
            Entry = Entry,
            StopLoss = Sl,
            TakeProfit = 0,
            VolumeUnits = 80,
            PlannedRiskUsd = 1000,
            PlannedRr = 1.25,
            VirtualXrExitEnabled = true,
            VirtualXrTriggerR = TriggerR,
            VirtualXrPrice = VirtualXrPrice,
            VirtualXrEffectiveClose = VirtualXrPrice,
        };
        var internalScore = new InternalCloseScore
        {
            CloseReason = "XRBarClose",
            InternalGrossR = 1.25,
            InternalGrossUsd = 1250,
            InternalNetUsd = 1245,
            InternalNetR = 1.245,
        };

        var line = TradePlanRrLog.FormatCloseCsv(
            "L6BT|R1|B1", "XRBarClose", "XRBarClose", in protection, in internalScore, 1240,
            default, hasExcursion: false);

        Assert.Contains(",True,", line);
        Assert.Contains($",{VirtualXrPrice:0.##},", line);
        Assert.Contains($",{VirtualXrPrice:0.##},", line);
    }
}
