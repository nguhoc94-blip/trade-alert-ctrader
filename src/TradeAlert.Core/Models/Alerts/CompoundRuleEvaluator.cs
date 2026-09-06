using System.Collections.Generic;
using System.Text;

namespace TradeAlert.Core.Models.Alerts;

/// <summary>Stateless AND evaluation of compound rules at a source-event snapshot.</summary>
public static class CompoundRuleEvaluator
{
    public static CompoundRuleEvalResult EvaluateRuleAtSnapshot(
        ICompoundRuleEvalContext ctx,
        CompoundAlertRule rule,
        CompoundSourceEventCandidate candidate,
        CompoundPrimarySource primarySource,
        CompoundRuntimeOptions options)
    {
        var legs = new List<CompoundLegEval>(rule.Entries.Count);
        var missing = new List<string>();

        foreach (var entry in rule.Entries)
        {
            var active = IsLegActive(ctx, entry, candidate, primarySource, options, out var debug);
            legs.Add(new CompoundLegEval
            {
                TfToken = entry.TfToken,
                ConditionName = CompoundRuleParser.PineNameFor(entry.ConditionId),
                IsActive = active,
                Debug = debug,
            });

            if (!active)
                missing.Add($"{entry.TfToken}:{entry.ConditionId}");
        }

        return new CompoundRuleEvalResult
        {
            AllTrue = missing.Count == 0,
            Legs = legs,
            MissingLegs = missing,
        };
    }

    static bool IsLegActive(
        ICompoundRuleEvalContext ctx,
        CompoundConditionEntry entry,
        CompoundSourceEventCandidate candidate,
        CompoundPrimarySource primarySource,
        CompoundRuntimeOptions options,
        out string debug)
    {
        debug = "";

        if (CompoundRuleParser.IsStateCondition(entry.ConditionId))
        {
            var ok = ctx.IsStateConditionActive(entry.TfToken, entry.ConditionId);
            debug = ok ? "state=OK" : "state=--";
            return ok;
        }

        var isPrimaryEvent = string.Equals(entry.TfToken, primarySource.TfToken, System.StringComparison.Ordinal)
            && entry.ConditionId == primarySource.PrimaryEventCondition;

        if (isPrimaryEvent)
        {
            if (!ctx.TryGetEventFiredBarIndex(entry.TfToken, entry.ConditionId, out var firedBar))
            {
                debug = "event=never-fired";
                return false;
            }

            if (firedBar != candidate.SourceBarIndex)
            {
                debug = $"event=bar-mismatch fired={firedBar} candidate={candidate.SourceBarIndex}";
                return false;
            }

            debug = $"event=OK bar#{firedBar}";
            return true;
        }

        var windowOk = ctx.IsConditionActive(
            entry.TfToken, entry.ConditionId, options.SyncMode, options.EventValidBars);
        debug = windowOk ? "event-window=OK" : "event-window=--";
        return windowOk;
    }

    public static string FormatLegSnapshot(IReadOnlyList<CompoundLegEval> legs)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < legs.Count; i++)
        {
            var leg = legs[i];
            if (i > 0) sb.Append(' ');
            sb.Append(leg.TfToken).Append(':').Append(leg.ConditionName).Append('=')
                .Append(leg.IsActive ? "OK" : "--");
        }

        return sb.ToString();
    }
}

/// <summary>Abstraction over MTF condition cache for unit tests and orchestrator.</summary>
public interface ICompoundRuleEvalContext
{
    bool IsStateConditionActive(string tfToken, AlertConditionId condId);
    bool IsConditionActive(string tfToken, AlertConditionId condId, CompoundEvalSyncMode syncMode, int eventValidBars);
    bool TryGetEventFiredBarIndex(string tfToken, AlertConditionId condId, out int firedBarIndex);
}
