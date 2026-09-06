using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TradeAlert.Core.Engines.AlertEvaluators;
using TradeAlert.Core.Models.Alerts;

namespace TradeAlert.Core.Engines;

public sealed class AlertEngine
{
    static readonly IReadOnlyDictionary<AlertConditionId, AlertDefinition> Definitions = BuildDefinitions();

    public IReadOnlyDictionary<AlertConditionId, AlertDefinition> Registry => Definitions;

    public AlertEvaluationResult Evaluate(AlertConditionId id, in AlertEvaluationContext ctx)
    {
        if (!Definitions.ContainsKey(id))
        {
            return AlertEvaluationResult.NotFired(
                "UNKNOWN_CONDITION",
                "AlertConditionId not in registry.",
                dependencyMissing: true);
        }

        return id switch
        {
            AlertConditionId.CondBuyEventM5 or AlertConditionId.CondSellEventM5
                or AlertConditionId.CondBuyEventHLM15 or AlertConditionId.CondSellEventHLM15
                or AlertConditionId.CondBuyEventNGM15 or AlertConditionId.CondSellEventNGM15
                or AlertConditionId.CondBuyEventHLNGM15 or AlertConditionId.CondSellEventHLNGM15
                => EventFamilyEvaluators.EvaluateById(id, in ctx),
            AlertConditionId.CanBuyTouchM5 or AlertConditionId.CanSellTouchM5
                or AlertConditionId.CanBuyReal or AlertConditionId.CanSellReal
                or AlertConditionId.CanBuyRealAndM15CloseNow or AlertConditionId.CanSellRealAndM15CloseNow
                => TouchRealEvaluators.EvaluateById(id, in ctx),
            AlertConditionId.CondPhaKhungLon
                => EventFamilyEvaluators.EvaluatePhaKhungLon(in ctx),
            _ => AlertEvaluationResult.NotFired("UNKNOWN_CONDITION", "Unhandled id.", true)
        };
    }

    /// <summary>15 định nghĩa — 12 Pine + PhaKhungLon + bot HLNG M15 pair.</summary>
    static IReadOnlyDictionary<AlertConditionId, AlertDefinition> BuildDefinitions() =>
        new ReadOnlyDictionary<AlertConditionId, AlertDefinition>(new Dictionary<AlertConditionId, AlertDefinition>
        {
            [AlertConditionId.CondBuyEventM5] = Def(
                AlertConditionId.CondBuyEventM5, "BUY EVENT M5", AlertTimingClass.BarCloseM5Event,
                "condBuyEventM5", Cs(AlertConditionId.CondBuyEventM5), "sym|tf|alert|pine|barT|barI|srcTf|ev(key)",
                "fire_ts|symbol|tf|name|cond|src_bar_t|src_bar_i|bar_flags|offset|reason|snap|dup|pine|cs"),
            [AlertConditionId.CondSellEventM5] = Def(
                AlertConditionId.CondSellEventM5, "SELL EVENT M5", AlertTimingClass.BarCloseM5Event,
                "condSellEventM5", Cs(AlertConditionId.CondSellEventM5), "sym|tf|alert|pine|barT|barI|srcTf|ev(key)",
                "fire_ts|symbol|tf|name|cond|src_bar_t|src_bar_i|bar_flags|offset|reason|snap|dup|pine|cs"),
            [AlertConditionId.CondBuyEventHLM15] = Def(
                AlertConditionId.CondBuyEventHLM15, "BUY EVENT HL M15", AlertTimingClass.BarCloseM15Event,
                "condBuyEventHLM15", Cs(AlertConditionId.CondBuyEventHLM15), "sym|tf|alert|pine|barT|barI|srcTf|ev(key)",
                "fire_ts|symbol|tf|name|cond|src_bar_t|src_bar_i|bar_flags|offset|reason|snap|dup|pine|cs"),
            [AlertConditionId.CondSellEventHLM15] = Def(
                AlertConditionId.CondSellEventHLM15, "SELL EVENT HL M15", AlertTimingClass.BarCloseM15Event,
                "condSellEventHLM15", Cs(AlertConditionId.CondSellEventHLM15), "sym|tf|alert|pine|barT|barI|srcTf|ev(key)",
                "fire_ts|symbol|tf|name|cond|src_bar_t|src_bar_i|bar_flags|offset|reason|snap|dup|pine|cs"),
            [AlertConditionId.CondBuyEventNGM15] = Def(
                AlertConditionId.CondBuyEventNGM15, "BUY EVENT NG M15", AlertTimingClass.BarCloseM15Event,
                "condBuyEventNGM15", Cs(AlertConditionId.CondBuyEventNGM15), "sym|tf|alert|pine|barT|barI|srcTf|ev(key)",
                "fire_ts|symbol|tf|name|cond|src_bar_t|src_bar_i|bar_flags|offset|reason|snap|dup|pine|cs"),
            [AlertConditionId.CondSellEventNGM15] = Def(
                AlertConditionId.CondSellEventNGM15, "SELL EVENT NG M15", AlertTimingClass.BarCloseM15Event,
                "condSellEventNGM15", Cs(AlertConditionId.CondSellEventNGM15), "sym|tf|alert|pine|barT|barI|srcTf|ev(key)",
                "fire_ts|symbol|tf|name|cond|src_bar_t|src_bar_i|bar_flags|offset|reason|snap|dup|pine|cs"),
            [AlertConditionId.CondBuyEventHLNGM15] = Def(
                AlertConditionId.CondBuyEventHLNGM15, "BUY EVENT HLNG M15", AlertTimingClass.BarCloseM15Event,
                "condBuyEventHLNGM15", Cs(AlertConditionId.CondBuyEventHLNGM15), "sym|tf|alert|pine|barT|barI|srcTf|ev(key)",
                "fire_ts|symbol|tf|name|cond|src_bar_t|src_bar_i|bar_flags|offset|reason|snap|dup|pine|cs"),
            [AlertConditionId.CondSellEventHLNGM15] = Def(
                AlertConditionId.CondSellEventHLNGM15, "SELL EVENT HLNG M15", AlertTimingClass.BarCloseM15Event,
                "condSellEventHLNGM15", Cs(AlertConditionId.CondSellEventHLNGM15), "sym|tf|alert|pine|barT|barI|srcTf|ev(key)",
                "fire_ts|symbol|tf|name|cond|src_bar_t|src_bar_i|bar_flags|offset|reason|snap|dup|pine|cs"),
            [AlertConditionId.CanBuyTouchM5] = Def(
                AlertConditionId.CanBuyTouchM5, "CAN BUY TOUCH M5", AlertTimingClass.RealtimeTouchBar0,
                "canBuyTouchM5", Cs(AlertConditionId.CanBuyTouchM5), "sym|tf|alert|pine|barT|barI|srcTf|zonekey?",
                "fire_ts|symbol|tf|name|cond|src_bar_t|src_bar_i|bar_flags|offset|reason|snap|dup|pine|cs"),
            [AlertConditionId.CanSellTouchM5] = Def(
                AlertConditionId.CanSellTouchM5, "CAN SELL TOUCH M5", AlertTimingClass.RealtimeTouchBar0,
                "canSellTouchM5", Cs(AlertConditionId.CanSellTouchM5), "sym|tf|alert|pine|barT|barI|srcTf|zonekey?",
                "fire_ts|symbol|tf|name|cond|src_bar_t|src_bar_i|bar_flags|offset|reason|snap|dup|pine|cs"),
            [AlertConditionId.CanBuyReal] = Def(
                AlertConditionId.CanBuyReal, "CAN BUY REAL", AlertTimingClass.RealtimeFilterBar0,
                "canBuyReal", Cs(AlertConditionId.CanBuyReal), "sym|tf|alert|pine|barT|barI|srcTf|filterEpoch?",
                "fire_ts|symbol|tf|name|cond|src_bar_t|src_bar_i|bar_flags|offset|reason|snap|dup|pine|cs"),
            [AlertConditionId.CanSellReal] = Def(
                AlertConditionId.CanSellReal, "CAN SELL REAL", AlertTimingClass.RealtimeFilterBar0,
                "canSellReal", Cs(AlertConditionId.CanSellReal), "sym|tf|alert|pine|barT|barI|srcTf|filterEpoch?",
                "fire_ts|symbol|tf|name|cond|src_bar_t|src_bar_i|bar_flags|offset|reason|snap|dup|pine|cs"),
            [AlertConditionId.CanBuyRealAndM15CloseNow] = Def(
                AlertConditionId.CanBuyRealAndM15CloseNow, "CAN BUY REAL @ M15 CLOSE",
                AlertTimingClass.RealtimeFilterBar0WithM15CloseEdge,
                "canBuyReal  and m15CloseNow", Cs(AlertConditionId.CanBuyRealAndM15CloseNow),
                "sym|tf|alert|pine|barT|barI|srcTf|m15edge|filterEpoch?",
                "fire_ts|symbol|tf|name|cond|src_bar_t|src_bar_i|bar_flags|offset|reason|snap|dup|pine|cs"),
            [AlertConditionId.CanSellRealAndM15CloseNow] = Def(
                AlertConditionId.CanSellRealAndM15CloseNow, "CAN SELL REAL @ M15 CLOSE",
                AlertTimingClass.RealtimeFilterBar0WithM15CloseEdge,
                "canSellReal and m15CloseNow", Cs(AlertConditionId.CanSellRealAndM15CloseNow),
                "sym|tf|alert|pine|barT|barI|srcTf|m15edge|filterEpoch?",
                "fire_ts|symbol|tf|name|cond|src_bar_t|src_bar_i|bar_flags|offset|reason|snap|dup|pine|cs"),
            [AlertConditionId.CondPhaKhungLon] = Def(
                AlertConditionId.CondPhaKhungLon, "Phá Khung Lớn",
                AlertTimingClass.BarCloseAnyTfPhaKhungLon,
                "condPhaKhungLon", Cs(AlertConditionId.CondPhaKhungLon),
                "sym|tf|alert|pine|barT|barI|srcTf|pkl(key)",
                "fire_ts|symbol|tf|name|cond|src_bar_t|src_bar_i|bar_flags|offset|reason|snap|dup|pine|cs"),
        });

    static string Cs(AlertConditionId id) => $"{nameof(AlertEngine)}.{id}";

    static AlertDefinition Def(
        AlertConditionId id,
        string title,
        AlertTimingClass timing,
        string pine,
        string csHint,
        string dupDesc,
        string logDesc) =>
        new()
        {
            Id = id,
            Title = title,
            MessageTemplateRef = title,
            TimingClass = timing,
            PineConditionSymbol = pine,
            CSharpTargetHint = csHint,
            DuplicateKeyFieldDescriptor = dupDesc,
            LogFieldDescriptor = logDesc,
        };

    public static AlertLogEntry ToLogEntry(
        AlertDefinition def,
        in AlertEvaluationContext ctx,
        in AlertEvaluationResult result,
        in DuplicateKey dupKey,
        DateTime fireChartLocal)
    {
        return new AlertLogEntry
        {
            FireTimestampChartLocal = fireChartLocal,
            Symbol = ctx.Symbol,
            Timeframe = ctx.ChartTimeframeToken,
            AlertName = def.Title,
            AlertType = def.Title,
            ConditionId = def.Id.ToString(),
            SourceBarTimeChartLocal = ctx.SourceBarOpenTimeChartLocal,
            SourceBarIndex = ctx.SourceBarIndex,
            IsCurrentBar = ctx.IsCurrentBar,
            IsBarClosed = ctx.IsBarClosed,
            IsRealtime = ctx.IsRealtime,
            IsBarCloseTiming =
                def.TimingClass is AlertTimingClass.BarCloseM5Event
                                or AlertTimingClass.BarCloseM15Event
                                or AlertTimingClass.BarCloseAnyTfPhaKhungLon,
            EvaluationOffset = ctx.EvaluationOffset,
            ReasonCode = result.ReasonCode,
            ReasonText = result.ReasonText,
            StateSnapshotRef = ctx.StateSnapshot,
            DuplicateKeyCanonical = dupKey.ToCanonicalString(),
            PineConditionSymbol = def.PineConditionSymbol,
            CSharpTarget = def.CSharpTargetHint,
        };
    }
}
