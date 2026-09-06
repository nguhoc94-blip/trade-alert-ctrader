using System;

namespace TradeAlert.Core.Models.Alerts;

/// <summary>Resolves the primary event source timeframe for each compound rule.</summary>
public static class CompoundRuleSourceResolver
{
    public static CompoundPrimarySource Resolve(CompoundAlertRule rule, string chartTfToken = "15")
    {
        CompoundConditionEntry? best = null;
        var bestMinutes = int.MaxValue;

        foreach (var entry in rule.Entries)
        {
            if (CompoundRuleParser.IsStateCondition(entry.ConditionId))
                continue;

            if (entry.ConditionId == AlertConditionId.CondPhaKhungLon)
                continue;

            if (!CompoundEvalSync.TryGetTfMinutes(entry.TfToken, out var minutes))
                continue;

            if (best == null || minutes < bestMinutes)
            {
                best = entry;
                bestMinutes = minutes;
            }
        }

        if (best != null)
        {
            return new CompoundPrimarySource
            {
                TfToken = best.Value.TfToken,
                PrimaryEventCondition = best.Value.ConditionId,
            };
        }

        return new CompoundPrimarySource
        {
            TfToken = chartTfToken,
            PrimaryEventCondition = AlertConditionId.CondBuyEventM5,
            UsedChartTfFallback = true,
        };
    }

    public static string ResolveRuleId(CompoundAlertRule rule) =>
        string.IsNullOrWhiteSpace(rule.RuleName)
            ? $"R{rule.SlotIndex}"
            : rule.RuleName.StartsWith("R", StringComparison.OrdinalIgnoreCase) && rule.RuleName.Length <= 3
                ? rule.RuleName
                : $"R{rule.SlotIndex}";
}
