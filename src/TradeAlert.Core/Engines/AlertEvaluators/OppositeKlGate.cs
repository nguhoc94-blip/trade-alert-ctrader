using System.Collections.Generic;
using TradeAlert.Core.Models.Alerts;

namespace TradeAlert.Core.Engines.AlertEvaluators;

/// <summary>
/// R5/R6 HL branch gate: HL M15 counts only when at least one opposite-direction
/// non-broken KeyLevel exists on M15 (ACTIVE or MAIN_C with extending keybox).
/// OB and broken KL are excluded.
/// </summary>
public static class OppositeKlGate
{
    /// <summary>BUY needs RED (HIGH) KL; SELL needs GREEN (LOW) KL.</summary>
    public static bool HasOppositeNonBrokenKl(IReadOnlyList<PivotEntry> pivots, bool isBuy)
    {
        foreach (var piv in pivots)
        {
            if (!piv.HasKeyBox || !piv.KeyExtending)
                continue;

            if (!IsNonBrokenActiveKl(piv.FlagNow, piv.MainRole))
                continue;

            if (isBuy && piv.Type == 1)
                return true;

            if (!isBuy && piv.Type == -1)
                return true;
        }

        return false;
    }

    /// <summary>Mirror Pine/ZoneCollector non-broken active KL flags (includes MAIN_C).</summary>
    public static bool IsNonBrokenActiveKl(int flag, int mainRole) =>
        flag == 1 || flag == 3 || flag == 4 || flag == 7
        || (flag == 0 && mainRole == 1)
        || flag == 5 || flag == 50;
}
