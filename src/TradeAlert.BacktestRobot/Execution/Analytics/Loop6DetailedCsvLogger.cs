using TradeAlert.BacktestRobot.Execution;
using TradeAlert.BacktestRobot.Execution.Kl;
using TradeAlert.BacktestRobot.Execution.News;
using TradeAlert.Core.Models.Alerts;

namespace TradeAlert.BacktestRobot.Execution.Analytics;

public sealed class Loop6AnalyticsContext
{
    public string RunId { get; init; } = "";
    public string BotVersion { get; init; } = Loop6DetailedCsvLogger.BotVersion;
    public string Symbol { get; init; } = "";
    public string Timeframe { get; init; } = "";
    public DateTime ServerTimeUtc { get; init; }
    public double Bid { get; init; }
    public double Ask { get; init; }
    public double SymbolSpreadPrice { get; init; }
    public double PipSize { get; init; }
    public double TickSize { get; init; }
    public SpreadResolution Spread { get; init; }
    public double SpreadOverrideInput { get; init; }
    public double AccountEquity { get; init; }
    public double RiskPercent { get; init; }
    public double ConfiguredRewardRisk { get; init; }
    public bool EnableMinSlConstraint { get; init; }
    public bool MinSlBacktestMode { get; init; }
    public double MinSlSpreadMultiple { get; init; }
    public double MinSlPipsForex { get; init; }
    public KlAssetType AssetType { get; init; }
}

/// <summary>Streaming CSV analytics for session/symbol/rule/spread edge analysis. Logging only — no strategy changes.</summary>
public sealed class Loop6DetailedCsvLogger
{
    public const string BotVersion = "LOOP6_REV001";

    readonly string _runDir;
    readonly Action<string> _print;
    readonly string _plansPath;
    readonly string _skipsPath;
    readonly string _closesPath;
    readonly string _telemetryPath;
    readonly string _newsActionsPath;
    readonly string _fridayFlatPath;
    bool _plansHeader;
    bool _skipsHeader;
    bool _closesHeader;
    bool _telemetryHeader;
    bool _newsActionsHeader;
    bool _fridayFlatHeader;
    long _planSeq;
    long _skipSeq;

    public string RunId { get; }

    public Loop6DetailedCsvLogger(string logDirectoryName, string runId, Action<string> print)
    {
        RunId = runId;
        _print = print;
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "cAlgo", "Data", logDirectoryName, runId);
        _runDir = root;
        Directory.CreateDirectory(_runDir);
        _plansPath = Path.Combine(_runDir, "loop6_trade_plans.csv");
        _skipsPath = Path.Combine(_runDir, "loop6_skipped_setups.csv");
        _closesPath = Path.Combine(_runDir, "loop6_closed_trades.csv");
        _telemetryPath = Path.Combine(_runDir, "loop6_runtime_telemetry.csv");
        _newsActionsPath = Path.Combine(_runDir, "loop6_news_actions.csv");
        _fridayFlatPath  = Path.Combine(_runDir, "loop6_friday_flat_actions.csv");
        _print($"[L6BT] Detailed CSV log ON | dir={_runDir}");
    }

    public string RunDirectory => _runDir;

    public void LogRuntimeTelemetry(in Loop6AnalyticsContext ctx, string spreadOverrideMode)
    {
        var row = new object?[]
        {
            "TELEMETRY", RunId, BotVersion, ctx.Symbol, ctx.Timeframe,
            ctx.ServerTimeUtc, ctx.Bid, ctx.Ask,
            ctx.SymbolSpreadPrice, ctx.PipSize, ctx.TickSize,
            ctx.SpreadOverrideInput, spreadOverrideMode,
            ctx.Spread.Price, ctx.Spread.Pips, ctx.Spread.Source,
            Loop6AnalyticsMath.MapSpreadSourceForLog(ctx.Spread.Source),
            ctx.EnableMinSlConstraint, ctx.MinSlBacktestMode,
            ctx.MinSlSpreadMultiple, ctx.MinSlPipsForex,
        };
        WriteTelemetry(row);
        _print($"[L6BT_CSV_TELEMETRY] {Loop6CsvUtil.JoinRow(row)}");
    }

    public void LogPlan(
        in TradePlan plan,
        in CompoundFireEvent ev,
        in Loop6AnalyticsContext ctx,
        string orderStatus,
        string? rejectReason = null,
        double? filledEntry = null,
        DateTime? fillTimeUtc = null,
        double volumeUnits = 0,
        double plannedRiskUsd = 0,
        double lotSizingRiskPips = 0)
    {
        _planSeq++;
        var bkk = Loop6BangkokSession.GetBangkokTime(ctx.ServerTimeUtc);
        var session = Loop6BangkokSession.GetBangkokSession(bkk);
        var spreadLog = Loop6AnalyticsMath.MapSpreadSourceForLog(plan.SpreadSourceAtPlan);
        var slStructure = plan.SlPipsBeforeFloor;
        var slFinal = plan.StopLossPips;
        var spreadToSl = Loop6AnalyticsMath.ComputeSpreadToSlRatio(plan.SpreadPipsAtPlan, slStructure);
        var slExpand = Loop6AnalyticsMath.ComputeSlExpandRatio(slFinal, slStructure);
        var rr = slFinal > 0 ? plan.TakeProfitPips / slFinal : 0;
        var minSlGate = !ctx.EnableMinSlConstraint
            || plan.SlPipsBeforeFloor >= plan.MinSlFloorPips
            || (ctx.MinSlBacktestMode && plan.SlFloorApplied);

        var row = new object?[]
        {
            _planSeq, RunId, BotVersion, ctx.Symbol, ctx.Timeframe,
            plan.Label, $"R{plan.SlotIndex + 1}", plan.TakeProfitSource.ToString(), plan.IsBuy ? "buy" : "sell",
            ctx.ServerTimeUtc, ctx.ServerTimeUtc, bkk, Loop6BangkokSession.GetBangkokHour(bkk), session,
            plan.RawEntryBid, plan.RawSlBid, plan.RawTpBid,
            filledEntry ?? plan.EntryLimit, plan.StopLoss, plan.TakeProfit,
            plan.EntryShiftBySpread, plan.SlShiftBySpread, plan.TpShiftBySpread,
            spreadLog, ctx.SpreadOverrideInput, ctx.SymbolSpreadPrice, ctx.PipSize, ctx.TickSize,
            plan.SpreadPipsAtPlan, ctx.Spread.Price, "", true, spreadToSl,
            slStructure, slFinal, plan.MinSlFloorPips, minSlGate, plan.SlFloorApplied, slExpand,
            plan.TakeProfitPips, rr, true,
            ctx.AccountEquity, ctx.RiskPercent, plannedRiskUsd, plan.LotFtmo, lotSizingRiskPips, slFinal,
            "", "", "KlEntryLot_Ftmo",
            "Limit", plan.Label, ctx.ServerTimeUtc, fillTimeUtc, plan.EntryLimit, filledEntry,
            filledEntry.HasValue && plan.EntryLimit > 0
                ? Math.Abs(filledEntry.Value - plan.EntryLimit) / ctx.PipSize
                : "",
            orderStatus, rejectReason ?? "",
            ev.ChartBarIndex, plan.SwingBPivotBar, plan.SwingBTfToken,
        };

        WritePlan(row);
        _print($"[L6BT_CSV_PLAN] {Loop6CsvUtil.JoinRow(row)}");
    }

    public void LogFill(
        in Loop6AnalyticsContext ctx,
        string label,
        int ruleSlot,
        bool isBuy,
        double expectedEntry,
        double filledEntry,
        DateTime fillTimeUtc,
        double volumeUnits,
        double plannedRiskUsd,
        double spreadPipsAtPlan,
        string spreadSourceAtPlan)
    {
        _planSeq++;
        var bkk = Loop6BangkokSession.GetBangkokTime(fillTimeUtc);
        var session = Loop6BangkokSession.GetBangkokSession(bkk);
        var spreadLog = Loop6AnalyticsMath.MapSpreadSourceForLog(spreadSourceAtPlan);

        var row = new object?[]
        {
            _planSeq, RunId, BotVersion, ctx.Symbol, ctx.Timeframe,
            label, $"R{ruleSlot + 1}", "FILL", isBuy ? "buy" : "sell",
            fillTimeUtc, fillTimeUtc, bkk, Loop6BangkokSession.GetBangkokHour(bkk), session,
            "", "", "", expectedEntry, "", "",
            "", "", "",
            spreadLog, ctx.SpreadOverrideInput, ctx.SymbolSpreadPrice, ctx.PipSize, ctx.TickSize,
            spreadPipsAtPlan, ctx.Spread.Price, "", true, "",
            "", "", "", true, false, "",
            "", "", true,
            ctx.AccountEquity, ctx.RiskPercent, plannedRiskUsd, "", "", "",
            "", "", "KlEntryLot_Ftmo",
            "Limit", label, fillTimeUtc, fillTimeUtc, expectedEntry, filledEntry,
            expectedEntry > 0 ? Math.Abs(filledEntry - expectedEntry) / ctx.PipSize : "",
            "FILLED", "",
            "", "", "",
        };

        WritePlan(row);
        _print($"[L6BT_CSV_FILL] {Loop6CsvUtil.JoinRow(row)}");
    }

    public void LogSkip(
        in Loop6AnalyticsContext ctx,
        int? ruleSlot,
        string? direction,
        string skipReasonCode,
        string skipReasonDetail,
        double? rawEntry = null,
        double? rawSl = null,
        double? rawTp = null,
        double? slStructurePips = null,
        int? chartBar = null)
    {
        _skipSeq++;
        var bkk = Loop6BangkokSession.GetBangkokTime(ctx.ServerTimeUtc);
        var session = Loop6BangkokSession.GetBangkokSession(bkk);
        var spreadToSl = Loop6AnalyticsMath.ComputeSpreadToSlRatio(ctx.Spread.Pips, slStructurePips ?? 0);

        var row = new object?[]
        {
            _skipSeq, RunId, ctx.ServerTimeUtc, ctx.Symbol,
            ruleSlot is >= 0 ? $"R{ruleSlot + 1}" : "",
            direction ?? "", session, Loop6BangkokSession.GetBangkokHour(bkk),
            rawEntry, rawSl, rawTp,
            ctx.Spread.Pips, ctx.Spread.Price, slStructurePips, spreadToSl,
            skipReasonCode, skipReasonDetail, chartBar,
        };

        WriteSkip(row);
        _print($"[L6BT_CSV_SKIP] {Loop6CsvUtil.JoinRow(row)}");
    }

    public void LogClose(
        in Loop6AnalyticsContext ctx,
        string label,
        int ruleSlot,
        bool isBuy,
        string closeReason,
        DateTime closeTimeUtc,
        double entryPrice,
        double closePrice,
        double initialSl,
        double finalSl,
        double tp,
        double volume,
        double grossProfit,
        double commission,
        double netProfit,
        double plannedRiskMoney,
        in ExcursionSnapshot excursion,
        int currentBarIndex)
    {
        var bkk = Loop6BangkokSession.GetBangkokTime(closeTimeUtc);
        var session = Loop6BangkokSession.GetBangkokSession(bkk);
        var resultRPrice = Loop6AnalyticsMath.ComputeResultRByPrice(entryPrice, initialSl, closePrice, isBuy);
        var resultRMoney = Loop6AnalyticsMath.ComputeResultRByMoney(netProfit, plannedRiskMoney);
        var barsHeld = currentBarIndex > 0 && excursion.OpenBarIndex > 0
            ? Math.Max(0, currentBarIndex - excursion.OpenBarIndex)
            : 0;
        var minutesHeld = (closeTimeUtc - excursion.OpenTimeUtc).TotalMinutes;

        var row = new object?[]
        {
            RunId, label, $"R{ruleSlot + 1}", ctx.Symbol,
            closeTimeUtc, closeTimeUtc, bkk, session,
            MapCloseBucket(closeReason), entryPrice, closePrice,
            initialSl, finalSl, tp, isBuy ? "buy" : "sell", volume,
            grossProfit, commission, 0, netProfit,
            plannedRiskMoney, plannedRiskMoney, resultRMoney, resultRPrice,
            excursion.MfePips, excursion.MaePips, excursion.MfeR, excursion.MaeR,
            barsHeld, minutesHeld,
        };

        WriteClose(row);
        _print($"[L6BT_CSV_CLOSE] {Loop6CsvUtil.JoinRow(row)} " +
               $"mfeR={excursion.MfeR:0.##} maeR={excursion.MaeR:0.##}");
    }

    /// <summary>Log a news filter action (refresh, block, force-close, error) to loop6_news_actions.csv.</summary>
    public void LogNewsAction(
        string eventType,
        in Loop6AnalyticsContext ctx,
        string provider,
        NewsEvent? newsEvent,
        double minutesToEvent,
        string action,
        int positionsClosed,
        int pendingOrdersCancelled,
        bool success,
        string error,
        bool usedCache,
        double cacheAgeHours,
        string reasonDetail)
    {
        var bkk = Loop6BangkokSession.GetBangkokTime(ctx.ServerTimeUtc);
        var eventTimeBkk = newsEvent != null ? (DateTime?)newsEvent.EventTimeUtc.AddHours(7) : null;

        var row = new object?[]
        {
            eventType, RunId, BotVersion, ctx.Symbol,
            ctx.ServerTimeUtc, bkk,
            provider,
            newsEvent?.EventName ?? "",
            newsEvent?.Currency ?? "",
            newsEvent?.Impact ?? "",
            newsEvent?.EventTimeUtc,
            eventTimeBkk,
            minutesToEvent > 0 ? (object?)minutesToEvent : null,
            action,
            positionsClosed,
            pendingOrdersCancelled,
            success,
            error,
            usedCache,
            cacheAgeHours > 0 ? (object?)cacheAgeHours : null,
            reasonDetail,
        };

        WriteNewsAction(row);
        _print($"[L6BT_CSV_NEWS] {Loop6CsvUtil.JoinRow(row)}");
    }

    /// <summary>Log a Friday Flat Guard action (force-close or entry block) to loop6_friday_flat_actions.csv.</summary>
    public void LogFridayFlatAction(
        string eventType,
        in Loop6AnalyticsContext ctx,
        DateTime bangkokTime,
        string action,
        int positionsClosed,
        int pendingsCancelled,
        bool success,
        string error,
        string reasonDetail)
    {
        var row = new object?[]
        {
            eventType, RunId, BotVersion, ctx.Symbol,
            ctx.ServerTimeUtc, bangkokTime,
            action,
            positionsClosed,
            pendingsCancelled,
            success,
            error,
            reasonDetail,
        };
        WriteFridayFlat(row);
        _print($"[L6BT_CSV_FRIDAY] {Loop6CsvUtil.JoinRow(row)}");
    }

    static string MapCloseBucket(string reason) => reason switch
    {
        "TakeProfit" => "TP",
        "StopLoss" => "SL",
        "session-stop" or "SessionStop" => "TIME_EXIT",
        "BBroken" or "BBrokenTpEntry" => "SIGNAL_EXIT",
        "flip" or "ManualClose" or "Closed" => "MANUAL",
        _ when reason.StartsWith("XRBarClose", StringComparison.Ordinal) => "TIME_EXIT",
        _ when reason.StartsWith("NEWS_FORCE_CLOSE", StringComparison.Ordinal) => "NEWS_FORCE_CLOSE",
        _ => "OTHER",
    };

    void WritePlan(IReadOnlyList<object?> row) =>
        Loop6CsvUtil.AppendCsvLine(_plansPath, PlanHeaders, row, ref _plansHeader);

    void WriteSkip(IReadOnlyList<object?> row) =>
        Loop6CsvUtil.AppendCsvLine(_skipsPath, SkipHeaders, row, ref _skipsHeader);

    void WriteClose(IReadOnlyList<object?> row) =>
        Loop6CsvUtil.AppendCsvLine(_closesPath, CloseHeaders, row, ref _closesHeader);

    void WriteTelemetry(IReadOnlyList<object?> row) =>
        Loop6CsvUtil.AppendCsvLine(_telemetryPath, TelemetryHeaders, row, ref _telemetryHeader);

    void WriteNewsAction(IReadOnlyList<object?> row) =>
        Loop6CsvUtil.AppendCsvLine(_newsActionsPath, NewsActionHeaders, row, ref _newsActionsHeader);

    void WriteFridayFlat(IReadOnlyList<object?> row) =>
        Loop6CsvUtil.AppendCsvLine(_fridayFlatPath, FridayFlatHeaders, row, ref _fridayFlatHeader);

    static readonly string[] PlanHeaders =
    {
        "plan_seq","run_id","bot_version","symbol","timeframe","plan_id","rule_id","setup_type","direction",
        "server_time","utc_time","bangkok_time","bangkok_hour","session_name",
        "raw_entry_bid","raw_sl_bid","raw_tp_bid","actual_entry","final_sl","final_tp",
        "entry_shift_by_spread","sl_shift_by_spread","tp_shift_by_spread",
        "spread_source","input_spread_override","symbol_spread_price","symbol_pip_size","symbol_tick_size",
        "resolved_spread_pips","resolved_spread_price","max_allowed_spread_pips","spread_gate_pass","spread_to_sl_ratio",
        "sl_structure_pips","sl_final_pips","sl_min_pips","min_sl_gate_pass","sl_floor_applied","sl_expand_ratio",
        "reward_pips","rr_after_adjust","rr_gate_pass",
        "account_equity","risk_percent","planned_risk_money","lot_size","lot_sizing_risk_pips","actual_risk_pips",
        "pip_value","estimated_commission","risk_model_name",
        "order_type","order_label","submit_time","fill_time","expected_entry","filled_entry","slippage_pips",
        "order_status","reject_reason","fire_bar","swing_b_bar","swing_b_tf",
    };

    static readonly string[] SkipHeaders =
    {
        "skip_seq","run_id","timestamp","symbol","rule_id","direction","session_name","bangkok_hour",
        "raw_entry_bid","raw_sl_bid","raw_tp_bid","spread_pips","spread_price","sl_structure_pips","spread_to_sl_ratio",
        "skip_reason_code","skip_reason_detail","chart_bar",
    };

    static readonly string[] CloseHeaders =
    {
        "run_id","plan_id","rule_id","symbol",
        "close_time_server","close_time_utc","close_time_bangkok","close_session_name","close_reason",
        "entry_price","close_price","initial_sl","final_sl","tp","direction","volume",
        "gross_profit","commission","swap","net_profit",
        "planned_risk_money","actual_risk_money","result_R_by_money","result_R_by_price",
        "mfe_pips","mae_pips","mfe_R","mae_R","bars_held","minutes_held",
    };

    static readonly string[] TelemetryHeaders =
    {
        "event_type","run_id","bot_version","symbol","timeframe","server_time","bid","ask",
        "symbol_spread","pip_size","tick_size","spread_override_input","spread_override_mode",
        "resolved_spread_price","resolved_spread_pips","spread_source_raw","spread_source",
        "min_sl_constraint","min_sl_backtest_mode","min_sl_spread_mult","min_sl_forex_pips",
    };

    static readonly string[] NewsActionHeaders =
    {
        "event_type","run_id","bot_version","symbol",
        "server_time_utc","bangkok_time",
        "provider",
        "event_name","event_currency","event_impact","event_time_utc","event_time_bangkok",
        "minutes_to_event",
        "action",
        "positions_closed","pending_orders_cancelled",
        "success","error",
        "used_cache","cache_age_hours",
        "reason_detail",
    };

    static readonly string[] FridayFlatHeaders =
    {
        "event_type","run_id","bot_version","symbol",
        "server_time_utc","bangkok_time",
        "action",
        "positions_closed","pending_cancelled",
        "success","error",
        "reason_detail",
    };
}
