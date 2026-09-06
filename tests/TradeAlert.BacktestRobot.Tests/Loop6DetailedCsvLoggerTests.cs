using TradeAlert.BacktestRobot.Execution;
using TradeAlert.BacktestRobot.Execution.Analytics;
using TradeAlert.Core.Models.Alerts;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests;

public class Loop6DetailedCsvLoggerTests
{
    [Fact]
    public void Logger_WritesHeadersAndSampleRows()
    {
        var dir = Path.Combine(Path.GetTempPath(), "loop6_csv_test_" + Guid.NewGuid().ToString("N"));
        var log = new Loop6DetailedCsvLogger("Loop6LogsTest", "TEST_RUN", _ => { });

        var ctx = new Loop6AnalyticsContext
        {
            RunId = log.RunId,
            Symbol = "XAUUSD",
            Timeframe = "Minute15",
            ServerTimeUtc = new DateTime(2024, 6, 15, 7, 0, 0, DateTimeKind.Utc),
            Bid = 2350.0,
            Ask = 2350.4,
            SymbolSpreadPrice = 0.40,
            PipSize = 0.01,
            TickSize = 0.01,
            Spread = new SpreadResolution { Pips = 40, Price = 0.40, Source = SpreadTelemetrySources.Symbol },
            SpreadOverrideInput = 0,
            AccountEquity = 100000,
            RiskPercent = 1,
            ConfiguredRewardRisk = 2,
            EnableMinSlConstraint = true,
            MinSlBacktestMode = true,
            MinSlSpreadMultiple = 10,
            MinSlPipsForex = 5,
            AssetType = Execution.Kl.KlAssetType.XAUUSD,
        };

        log.LogRuntimeTelemetry(in ctx, "Auto");

        var plan = new TradePlan
        {
            SlotIndex = 2,
            Label = "L6BT|R3|B1200",
            IsBuy = true,
            EntryLimit = 2350.5,
            StopLoss = 2348.5,
            TakeProfit = 2354.5,
            StopLossPips = 200,
            TakeProfitPips = 400,
            LotFtmo = 0.5,
            RawEntryBid = 2350.4,
            RawSlBid = 2348.4,
            RawTpBid = 2354.4,
            EntryShiftBySpread = 0.1,
            SlShiftBySpread = 0.1,
            TpShiftBySpread = 0.1,
            SpreadPipsAtPlan = 40,
            SpreadSourceAtPlan = SpreadTelemetrySources.Symbol,
            MinSlFloorPips = 400,
            SlPipsBeforeFloor = 200,
            SlFloorApplied = true,
            SwingBPivotBar = 1200,
            SwingBTfToken = "15",
            TakeProfitSource = TakeProfitSource.RewardRisk,
        };
        var ev = new CompoundFireEvent { SlotIndex = 2, ChartBarIndex = 1200, Direction = SignalDirection.Buy };
        log.LogPlan(in plan, in ev, in ctx, "PLAN", plannedRiskUsd: 1000, lotSizingRiskPips: 200);

        log.LogSkip(in ctx, 1, "sell", Loop6SkipReasonCodes.SlBelowMin,
            "SL too tight: 3p < min 50p", 2350, 2349.5, 2345, 3, chartBar: 1201);

        log.LogClose(in ctx, "L6BT|R3|B1200", 2, true, "TakeProfit",
            new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc),
            2350.5, 2354.5, 2348.5, 2348.5, 2354.5, 50,
            200, -2, 198, 1000,
            new ExcursionSnapshot { MfePips = 450, MaePips = 80, MfeR = 2.25, MaeR = 0.4, OpenBarIndex = 1200 },
            1210);

        Assert.True(File.Exists(Path.Combine(log.RunDirectory, "loop6_trade_plans.csv")));
        Assert.True(File.Exists(Path.Combine(log.RunDirectory, "loop6_skipped_setups.csv")));
        Assert.True(File.Exists(Path.Combine(log.RunDirectory, "loop6_closed_trades.csv")));
        Assert.True(File.Exists(Path.Combine(log.RunDirectory, "loop6_runtime_telemetry.csv")));
    }
}
