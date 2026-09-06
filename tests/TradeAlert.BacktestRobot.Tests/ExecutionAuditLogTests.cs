using TradeAlert.BacktestRobot.Execution;
using TradeAlert.BacktestRobot.Execution.Analytics;
using TradeAlert.Core.Models.Alerts;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests;

public class ExecutionAuditLogTests
{
    [Fact]
    public void FormatPlanAbs_IncludesPlannedRiskUsdAndVolume()
    {
        var plan = new TradePlan
        {
            Label = "L6BT|R2|B100",
            IsBuy = true,
            EntryLimit = 100,
            StopLoss = 97,
            TakeProfit = 106,
            StopLossPips = 30,
            TakeProfitPips = 60,
        };

        var lines = TradePlanRrLog.FormatPlanAbs(in plan, cfgRr: 2.0, plannedRiskUsd: 1000, volumeUnits: 5000);

        Assert.Equal(4, lines.Count);
        Assert.Contains("plannedRiskUsd=1000", lines[3]);
        Assert.Contains("volumeUnits=5000", lines[3]);
    }

    [Fact]
    public void ApproxUsdFromPriceDistance_XauUsd_UsesPriceTimesVolume()
    {
        var money = new ExecutionAuditMoneyContext
        {
            UsePriceDistanceTimesVolume = true,
            PipSize = 0.01,
        };

        var usd = ExecutionPnLCalculator.ApproxUsdFromPriceDistance(
            entry: 2000,
            targetPrice: 2010,
            volumeInUnits: 65,
            in money);

        Assert.Equal(650, usd, 6);
    }

    [Fact]
    public void ComputeCloseAudit_TakeProfit_ClampsOvershoot()
    {
        var protection = new PositionProtectionSnapshot
        {
            IsBuy = true,
            Entry = 2000,
            StopLoss = 1990,
            TakeProfit = 2010,
            VolumeUnits = 65,
            PlannedRiskUsd = 1000,
            PlannedRr = 0.3,
        };

        var pnl = ExecutionPnLCalculator.ComputeCloseAudit(
            in protection,
            rawClose: 2015,
            rawGross: 975,
            commission: -5,
            rawNet: 970,
            closeReason: "TakeProfit",
            pipSize: 0.01);

        Assert.Equal(2015, pnl.RawClose, 6);
        Assert.Equal(2010, pnl.ClampedClose, 6);
        Assert.Equal(650, pnl.ClampedGross, 6);
        Assert.Equal(645, pnl.ClampedNet, 6);
        Assert.Equal(0.645, pnl.ClampedR, 3);
        Assert.True(pnl.HasTpOvershoot);
        Assert.Equal(500, pnl.TpOvershootPips, 3);
    }

    [Fact]
    public void ComputeCloseAudit_StopLoss_ClampsToSl()
    {
        var protection = new PositionProtectionSnapshot
        {
            IsBuy = true,
            Entry = 2000,
            StopLoss = 1990,
            TakeProfit = 2010,
            VolumeUnits = 65,
            PlannedRiskUsd = 1000,
            PlannedRr = 0.3,
        };

        var pnl = ExecutionPnLCalculator.ComputeCloseAudit(
            in protection,
            rawClose: 1988,
            rawGross: -780,
            commission: -5,
            rawNet: -785,
            closeReason: "StopLoss",
            pipSize: 0.01);

        Assert.Equal(1990, pnl.ClampedClose, 6);
        Assert.Equal(-650, pnl.ClampedGross, 6);
        Assert.Equal(-655, pnl.ClampedNet, 6);
    }

    [Fact]
    public void MapCloseReason_NormalizesSessionAndBBroken()
    {
        Assert.Equal("SessionStop", TradePlanRrLog.MapCloseReason("session-stop"));
        Assert.Equal("BBroken", TradePlanRrLog.MapCloseReason("1-Broken-tp-reject"));
        Assert.Equal("ManualClose", TradePlanRrLog.MapCloseReason("flip"));
    }

    [Fact]
    public void InternalCloseReasonClassifier_PriceAtTp_ReturnsTakeProfit()
    {
        var protection = new PositionProtectionSnapshot
        {
            IsBuy = true,
            StopLoss = 1990,
            TakeProfit = 2010,
        };

        var reason = InternalCloseReasonClassifier.Classify(
            rawClose: 2015, in protection, tolerancePips: 2, pipSize: 0.01, handlerReason: "session-stop");

        Assert.Equal("TakeProfit", reason);
    }

    [Fact]
    public void InternalCloseReasonClassifier_SessionStopWhenNotAtTpSl()
    {
        var protection = new PositionProtectionSnapshot
        {
            IsBuy = true,
            StopLoss = 1990,
            TakeProfit = 2010,
        };

        var reason = InternalCloseReasonClassifier.Classify(
            rawClose: 2000, in protection, tolerancePips: 2, pipSize: 0.01, handlerReason: "session-stop");

        Assert.Equal("SessionStop", reason);
    }

    [Fact]
    public void InternalRScoreCalculator_TakeProfit_UsesPlannedRrNotRawOvershoot()
    {
        var protection = new PositionProtectionSnapshot
        {
            IsBuy = true,
            Entry = 2000,
            StopLoss = 1990,
            TakeProfit = 2010,
            VolumeUnits = 65,
            PlannedRiskUsd = 1000,
            PlannedRr = 0.65,
        };
        var data = new ClosedTradeData
        {
            HasData = true,
            RawClose = 2015,
            GrossProfit = 975,
            Commission = -5,
            NetProfit = 970,
        };
        var config = InternalPerformanceConfig.Default;

        var score = InternalRScoreCalculator.Compute(in protection, in data, "TakeProfit", in config, pipSize: 0.01);

        Assert.Equal(0.65, score.InternalGrossR, 6);
        Assert.Equal(650, score.InternalGrossUsd, 6);
        Assert.Equal(645, score.InternalNetUsd, 6);
        Assert.True(score.RawVsInternalNetDiff > 300);
    }

    [Fact]
    public void InternalRScoreCalculator_StopLoss_UsesMinusOneR()
    {
        var protection = new PositionProtectionSnapshot
        {
            IsBuy = true,
            Entry = 2000,
            StopLoss = 1990,
            TakeProfit = 2010,
            VolumeUnits = 65,
            PlannedRiskUsd = 1000,
            PlannedRr = 0.65,
        };
        var data = new ClosedTradeData
        {
            HasData = true,
            RawClose = 1988,
            GrossProfit = -780,
            Commission = -5,
            NetProfit = -785,
        };

        var score = InternalRScoreCalculator.Compute(
            in protection, in data, "StopLoss", InternalPerformanceConfig.Default, pipSize: 0.01);

        Assert.Equal(-1.0, score.InternalGrossR, 6);
        Assert.Equal(-1000, score.InternalGrossUsd, 6);
        Assert.Equal(-1005, score.InternalNetUsd, 6);
    }

    [Fact]
    public void InternalCloseReasonClassifier_BBrokenMoveTpEntry_NotTakeProfit()
    {
        var protection = new PositionProtectionSnapshot
        {
            IsBuy = true,
            Entry = 2000,
            StopLoss = 1990,
            TakeProfit = 2000,
            BBrokenMoveTpToEntryApplied = true,
            BBrokenMoveTpToEntryPrice = 2000,
        };

        var reason = InternalCloseReasonClassifier.Classify(
            rawClose: 2000, in protection, tolerancePips: 2, pipSize: 0.01, handlerReason: null);

        Assert.Equal("BBrokenTpEntry", reason);
    }

    [Fact]
    public void InternalRScoreCalculator_BBrokenTpEntry_UsesMovedTpNotPlannedRr()
    {
        var protection = new PositionProtectionSnapshot
        {
            IsBuy = true,
            Entry = 2000,
            StopLoss = 1990,
            TakeProfit = 2000,
            VolumeUnits = 65,
            PlannedRiskUsd = 1000,
            PlannedRr = 0.65,
            BBrokenMoveTpToEntryApplied = true,
            BBrokenMoveTpToEntryPrice = 2000,
        };
        var data = new ClosedTradeData
        {
            HasData = true,
            RawClose = 2000,
            GrossProfit = 0,
            Commission = -5,
            NetProfit = -5,
        };

        var score = InternalRScoreCalculator.Compute(
            in protection, in data, "BBrokenTpEntry", InternalPerformanceConfig.Default, pipSize: 0.01);

        Assert.Equal(0, score.InternalGrossUsd, 6);
        Assert.Equal(0, score.InternalGrossR, 6);
        Assert.Equal(-5, score.InternalNetUsd, 6);
        Assert.NotEqual(0.65, score.InternalGrossR);
    }

    [Fact]
    public void AuditClose_BBrokenTpEntry_CountsInSummaryBucket()
    {
        var tracker = new ExecutionAuditTracker();
        tracker.SetPipSize(0.01);
        tracker.RecordPlanned(new TradePlan
        {
            Label = "L6BT|R1|B1",
            IsBuy = true,
            EntryLimit = 2000,
            StopLoss = 1990,
            TakeProfit = 2010,
            StopLossPips = 10,
            TakeProfitPips = 6.5,
        }, cfgRr: 0.65, expectTpOnPlace: true, plannedRiskUsd: 1000, volumeUnits: 65);

        tracker.RecordBBrokenMoveTpToEntry("L6BT|R1|B1", newTakeProfit: 2000, originalTp: 2010, stopLossAtMove: 1990, moveTime: DateTime.UtcNow);

        var logged = new System.Collections.Generic.List<string>();
        tracker.AuditClose("L6BT|R1|B1", new ClosedTradeData
        {
            HasData = true,
            RawClose = 2000,
            GrossProfit = 0,
            Commission = -5,
            NetProfit = -5,
        }, "TakeProfit", logged.Add);

        Assert.Contains(logged, l => l.Contains("reason=BBrokenTpEntry"));
        Assert.Contains(logged, l => l.Contains("bBrokenMoveTpToEntryApplied=True"));
        Assert.DoesNotContain(logged, l => l.Contains("WARNING BBROKEN TP-ENTRY MISCLASSIFIED"));
    }

    [Fact]
    public void FormatCloseAudit_IncludesInternalRFields()
    {
        var protection = new PositionProtectionSnapshot
        {
            Entry = 2000,
            TakeProfit = 2000,
            StopLoss = 1990,
            PlannedRiskUsd = 1000,
            PlannedRr = 0.65,
            AnchorMode = ProtectionAnchorMode.ActualFillRelative,
            BBrokenMoveTpToEntryApplied = true,
            BBrokenMoveTpToEntryPrice = 2000,
        };
        var data = new ClosedTradeData
        {
            HasData = true,
            RawClose = 2000,
            GrossProfit = 0,
            Commission = -5,
            NetProfit = -5,
        };
        var internalScore = new InternalCloseScore
        {
            CloseReason = "BBrokenTpEntry",
            InternalGrossR = 0,
            InternalNetR = -0.005,
            InternalGrossUsd = 0,
            InternalNetUsd = -5,
            RawVsInternalNetDiff = 0,
        };

        var line = TradePlanRrLog.FormatCloseAudit(
            "L6BT|R1|B1", "BBrokenTpEntry", "BBrokenTpEntry", planMissing: false,
            in protection, in data, in internalScore, effectiveClose: 2000,
            default, hasExcursion: false);

        Assert.Contains("reason=BBrokenTpEntry", line);
        Assert.Contains("internalReason=BBrokenTpEntry", line);
        Assert.Contains("bBrokenMoveTpToEntryApplied=True", line);
        Assert.Contains("internalGrossR=0", line);
        Assert.Contains("internalNetUsd=-5", line);
        Assert.Contains("mfeR=n/a", line);
        Assert.Contains("maeR=n/a", line);

        var excursion = new ExcursionSnapshot { MfePips = 80, MaePips = 20, MfeR = 0.8, MaeR = 0.2 };
        var lineWithExcursion = TradePlanRrLog.FormatCloseAudit(
            "L6BT|R1|B1", "TakeProfit", "TakeProfit", planMissing: false,
            in protection, in data, in internalScore, effectiveClose: 2000,
            in excursion, hasExcursion: true);
        Assert.Contains("mfeR=0.8", lineWithExcursion);
        Assert.Contains("maeR=0.2", lineWithExcursion);
        Assert.Contains("mfePips=80", lineWithExcursion);
        Assert.Contains("maePips=20", lineWithExcursion);

        var csv = TradePlanRrLog.FormatCloseCsv(
            "L6BT|R1|B1", "BBrokenTpEntry", "BBrokenTpEntry", in protection, in internalScore, -5,
            in excursion, hasExcursion: true);
        Assert.StartsWith("[L6BT_CSV_CLOSE],", csv);
        Assert.Contains(",True,", csv);
    }

    [Fact]
    public void AuditClose_PlanMissing_StillLogsAndCounts()
    {
        var tracker = new ExecutionAuditTracker();
        tracker.SetPipSize(0.01);

        var logged = new System.Collections.Generic.List<string>();
        var data = new ClosedTradeData
        {
            HasData = true,
            RawClose = 2010,
            GrossProfit = 300,
            Commission = -5,
            NetProfit = 295,
        };

        tracker.AuditClose("L6BT|R2|B545", data, "TakeProfit", logged.Add);

        Assert.Equal(1, tracker.CloseAuditCount);
        Assert.Equal(1, tracker.CloseAuditMissingCount);
        Assert.Contains(logged, l => l.Contains("planMissing=true"));
    }

    [Fact]
    public void AuditClose_DedupsByLabel()
    {
        var tracker = new ExecutionAuditTracker();
        tracker.SetPipSize(0.01);

        var logged = new System.Collections.Generic.List<string>();
        var data = new ClosedTradeData { HasData = true, NetProfit = 100 };

        tracker.AuditClose("L6BT|R2|B1", data, "TakeProfit", logged.Add);
        tracker.AuditClose("L6BT|R2|B1", data, "TakeProfit", logged.Add);

        Assert.Equal(1, tracker.CloseAuditCount);
    }

    [Fact]
    public void ActualRrFromPrices_Buy_ComputesRewardOverRisk()
    {
        var rr = TradePlanRrLog.ActualRrFromPrices(
            entry: 100,
            stopLoss: 97,
            takeProfit: 106,
            isBuy: true);

        Assert.Equal(2.0, rr, 6);
    }

    [Fact]
    public void ExpectedActualProtection_Buy_AnchorsToFillPrice()
    {
        var (sl, tp) = ExecutionPnLCalculator.ExpectedActualProtection(
            fillEntry: 2872.74, plannedSlPips: 30, plannedTpPips: 85.3, pipSize: 0.01, isBuy: true);

        Assert.Equal(2872.74 - 0.30, sl, 6);
        Assert.Equal(2872.74 + 0.853, tp, 6);
    }

    [Fact]
    public void ExpectedActualProtection_Sell_InvertsDirection()
    {
        var (sl, tp) = ExecutionPnLCalculator.ExpectedActualProtection(
            fillEntry: 100, plannedSlPips: 20, plannedTpPips: 40, pipSize: 0.1, isBuy: false);

        Assert.Equal(102.0, sl, 6);
        Assert.Equal(96.0, tp, 6);
    }

    [Fact]
    public void FormatProtectionSync_ReportsRejectedResult()
    {
        var line = TradePlanRrLog.FormatProtectionSync(
            "L6BT|R2|B1", oldSl: 100, oldTp: 110, newSl: 99, newTp: 112, success: false);

        Assert.Contains("PROTECTION SYNC", line);
        Assert.Contains("mode=PlannedAbsolute", line);
        Assert.Contains("result=rejected", line);
    }

    [Fact]
    public void FormatProtectionAudit_IncludesMismatchPips()
    {
        var line = TradePlanRrLog.FormatProtectionAudit(
            "L6BT|R2|B1", preFillPlannedEntry: 2873.63, actualEntry: 2872.74,
            expectedTp: 2873.59, positionTp: 2873.59, expectedSl: 2872.44, positionSl: 2872.44,
            tpMismatchPips: 0, slMismatchPips: 0);

        Assert.Contains("PROTECTION AUDIT", line);
        Assert.Contains("mode=ActualFillRelative", line);
        Assert.Contains("actualEntry=2872.74", line);
    }

    [Fact]
    public void ProtectionAnchorModeParser_ParsesPlannedAbsolute()
    {
        Assert.Equal(ProtectionAnchorMode.PlannedAbsolute, ProtectionAnchorModeParser.Parse("PlannedAbsolute"));
        Assert.Equal(ProtectionAnchorMode.ActualFillRelative, ProtectionAnchorModeParser.Parse("anything"));
        Assert.Equal(ProtectionAnchorMode.ActualFillRelative, ProtectionAnchorModeParser.Parse(null));
    }
}
