using System;
using System.Collections.Generic;
using System.Globalization;

namespace TradeAlert.Core.Models.Alerts;

/// <summary>
/// Parses compound alert rule strings. Condition names match Pine 100% (camelCase).
/// Format: <c>TF:pineConditionName+TF:pineConditionName</c>
/// </summary>
public static class CompoundRuleParser
{
    public const string HelpText =
        "Format: TF:pineConditionName + TF:pineConditionName ...\n" +
        "TFs: M5, M15, H1, H4 (or 5, 15, 60, 240)\n" +
        "Conditions (Pine names, case-insensitive):\n" +
        "  condBuyEventM5, condSellEventM5,\n" +
        "  condBuyEventHLM15, condSellEventHLM15, condBuyEventNGM15, condSellEventNGM15,\n" +
        "  condBuyEventHLNGM15, condSellEventHLNGM15 (bot R5/R6 — HL gated by opposite KL),\n" +
        "  canBuyTouchM5, canSellTouchM5, canBuyReal, canSellReal,\n" +
        "  canBuyRealAndM15CloseNow, canSellRealAndM15CloseNow, condPhaKhungLon\n" +
        "Leave blank to disable rule.";

    static readonly Dictionary<string, AlertConditionId> PineNameMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["condBuyEventM5"] = AlertConditionId.CondBuyEventM5,
            ["condSellEventM5"] = AlertConditionId.CondSellEventM5,
            ["condBuyEventHLM15"] = AlertConditionId.CondBuyEventHLM15,
            ["condSellEventHLM15"] = AlertConditionId.CondSellEventHLM15,
            ["condBuyEventNGM15"] = AlertConditionId.CondBuyEventNGM15,
            ["condSellEventNGM15"] = AlertConditionId.CondSellEventNGM15,
            ["condBuyEventHLNGM15"] = AlertConditionId.CondBuyEventHLNGM15,
            ["condSellEventHLNGM15"] = AlertConditionId.CondSellEventHLNGM15,
            ["canBuyTouchM5"] = AlertConditionId.CanBuyTouchM5,
            ["canSellTouchM5"] = AlertConditionId.CanSellTouchM5,
            ["canBuyReal"] = AlertConditionId.CanBuyReal,
            ["canSellReal"] = AlertConditionId.CanSellReal,
            ["canBuyRealAndM15CloseNow"] = AlertConditionId.CanBuyRealAndM15CloseNow,
            ["canSellRealAndM15CloseNow"] = AlertConditionId.CanSellRealAndM15CloseNow,
            ["condPhaKhungLon"] = AlertConditionId.CondPhaKhungLon,
        };

    static readonly Dictionary<string, string> TfAliasMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["M1"] = "1",
            ["M2"] = "2",
            ["M5"] = "5",
            ["M15"] = "15",
            ["M30"] = "30",
            ["H1"] = "60",
            ["H4"] = "240",
            ["D1"] = "1440",
            ["1"] = "1",
            ["2"] = "2",
            ["5"] = "5",
            ["15"] = "15",
            ["30"] = "30",
            ["60"] = "60",
            ["240"] = "240",
            ["1440"] = "1440",
        };

    public static bool TryParse(
        string? ruleText,
        int ruleIndex,
        out CompoundAlertRule rule,
        out string? error,
        string? ruleDisplayName = null)
    {
        rule = new CompoundAlertRule
        {
            SlotIndex = ruleIndex + 1,
            RuleName = string.IsNullOrWhiteSpace(ruleDisplayName)
                ? $"R{ruleIndex + 1}"
                : ruleDisplayName.Trim(),
        };
        error = null;

        if (string.IsNullOrWhiteSpace(ruleText))
            return true;

        var trimmed = ruleText.Trim();
        var parts = trimmed.Split(new[] { '+', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return true;

        foreach (var part in parts)
        {
            var colon = part.IndexOf(':');
            if (colon <= 0 || colon >= part.Length - 1)
            {
                error = $"Invalid segment '{part}' — expected TF:pineConditionName";
                return false;
            }

            var tfRaw = part[..colon].Trim();
            var condRaw = part[(colon + 1)..].Trim();

            if (!TryNormalizeTfToken(tfRaw, out var tfToken))
            {
                error = $"Unknown timeframe '{tfRaw}' in '{part}'";
                return false;
            }

            if (!PineNameMap.TryGetValue(condRaw, out var condId))
            {
                error = $"Unknown Pine condition '{condRaw}' in '{part}'";
                return false;
            }

            rule.Entries.Add(new CompoundConditionEntry(tfToken, condId));
        }

        // Derive direction from the first EVENT entry (non-state condition).
        // Uses AlertConditionIdExtensions.GetDirection() — no string parsing.
        foreach (var e in rule.Entries)
        {
            if (!IsStateCondition(e.ConditionId))
            {
                rule = new CompoundAlertRule
                {
                    SlotIndex  = rule.SlotIndex,
                    RuleName   = rule.RuleName,
                    Entries    = rule.Entries,
                    Direction  = e.ConditionId.GetDirection(),
                };
                break;
            }
        }

        return true;
    }

    public static string? ValidateTfConditionCompat(in CompoundConditionEntry entry)
    {
        var tf = entry.TfToken;
        var id = entry.ConditionId;

        switch (id)
        {
            case AlertConditionId.CondBuyEventM5:
            case AlertConditionId.CondSellEventM5:
                return tf == "5" ? null : $"{PineNameFor(id)} on TF '{tf}' will never fire (Pine gates on currentTF==\"5\")";

            case AlertConditionId.CanBuyTouchM5:
            case AlertConditionId.CanSellTouchM5:
                // Pine: can*TouchM5 has no currentTF gate — HTF chart evaluates touch on that TF's close/zones.
                return null;

            case AlertConditionId.CondBuyEventHLM15:
            case AlertConditionId.CondSellEventHLM15:
            case AlertConditionId.CondBuyEventNGM15:
            case AlertConditionId.CondSellEventNGM15:
            case AlertConditionId.CondBuyEventHLNGM15:
            case AlertConditionId.CondSellEventHLNGM15:
                if (IsTfAtLeastMinutes(tf, 15) || tf == "5")
                    return null;
                return $"{PineNameFor(id)} on TF '{tf}' unlikely to fire (M15 event family)";

            case AlertConditionId.CanBuyRealAndM15CloseNow:
            case AlertConditionId.CanSellRealAndM15CloseNow:
                if (tf == "5" || tf == "15" || IsTfAtLeastMinutes(tf, 15))
                    return null;
                return $"{PineNameFor(id)} on TF '{tf}' needs M15 close edge";

            default:
                return null;
        }
    }

    public static HashSet<string> CollectRequiredTfTokens(IEnumerable<CompoundAlertRule> rules)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            foreach (var e in rule.Entries)
                set.Add(e.TfToken);
        }

        return set;
    }

    public static Dictionary<string, AlertConditionId[]> BuildTrackedConditionsPerTf(IEnumerable<CompoundAlertRule> rules)
    {
        var map = new Dictionary<string, HashSet<AlertConditionId>>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            foreach (var e in rule.Entries)
            {
                if (!map.TryGetValue(e.TfToken, out var set))
                {
                    set = new HashSet<AlertConditionId>();
                    map[e.TfToken] = set;
                }

                set.Add(e.ConditionId);
            }
        }

        var result = new Dictionary<string, AlertConditionId[]>(StringComparer.Ordinal);
        foreach (var kv in map)
            result[kv.Key] = kv.Value.ToArray();

        return result;
    }

    public static string PineNameFor(AlertConditionId id) =>
        id switch
        {
            AlertConditionId.CondBuyEventM5 => "condBuyEventM5",
            AlertConditionId.CondSellEventM5 => "condSellEventM5",
            AlertConditionId.CondBuyEventHLM15 => "condBuyEventHLM15",
            AlertConditionId.CondSellEventHLM15 => "condSellEventHLM15",
            AlertConditionId.CondBuyEventNGM15 => "condBuyEventNGM15",
            AlertConditionId.CondSellEventNGM15 => "condSellEventNGM15",
            AlertConditionId.CondBuyEventHLNGM15 => "condBuyEventHLNGM15",
            AlertConditionId.CondSellEventHLNGM15 => "condSellEventHLNGM15",
            AlertConditionId.CanBuyTouchM5 => "canBuyTouchM5",
            AlertConditionId.CanSellTouchM5 => "canSellTouchM5",
            AlertConditionId.CanBuyReal => "canBuyReal",
            AlertConditionId.CanSellReal => "canSellReal",
            AlertConditionId.CanBuyRealAndM15CloseNow => "canBuyRealAndM15CloseNow",
            AlertConditionId.CanSellRealAndM15CloseNow => "canSellRealAndM15CloseNow",
            AlertConditionId.CondPhaKhungLon => "condPhaKhungLon",
            _ => id.ToString(),
        };

    public static bool IsStateCondition(AlertConditionId id) =>
        id is AlertConditionId.CanBuyReal
            or AlertConditionId.CanSellReal
            or AlertConditionId.CanBuyTouchM5
            or AlertConditionId.CanSellTouchM5
            or AlertConditionId.CanBuyRealAndM15CloseNow
            or AlertConditionId.CanSellRealAndM15CloseNow;

    public static bool TryNormalizeTfToken(string tfRaw, out string tfToken)
    {
        tfToken = "";
        if (string.IsNullOrWhiteSpace(tfRaw))
            return false;

        if (TfAliasMap.TryGetValue(tfRaw.Trim(), out var mapped))
        {
            tfToken = mapped;
            return true;
        }

        return false;
    }

    static bool IsTfAtLeastMinutes(string tfToken, int minutes)
    {
        if (!int.TryParse(tfToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var m))
            return tfToken is "60" or "240" or "1440";
        return m >= minutes;
    }
}
