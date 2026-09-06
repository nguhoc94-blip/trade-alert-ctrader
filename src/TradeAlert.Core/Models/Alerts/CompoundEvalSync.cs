using System;
using System.Collections.Generic;
using System.Globalization;

namespace TradeAlert.Core.Models.Alerts;

/// <summary>
/// TradingView multi-condition sync — smallest TF cadence, same-moment AND.
/// </summary>
public static class CompoundEvalSync
{
    public static bool TryGetTfMinutes(string tfToken, out int minutes)
    {
        minutes = 0;
        if (string.IsNullOrWhiteSpace(tfToken))
            return false;

        if (int.TryParse(tfToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes))
            return minutes > 0;

        return tfToken switch
        {
            "60" => Assign(60, out minutes),
            "240" => Assign(240, out minutes),
            "1440" => Assign(1440, out minutes),
            _ => false,
        };

        static bool Assign(int value, out int target)
        {
            target = value;
            return true;
        }
    }

    /// <summary>Smallest TF (minutes) across all entries in active rules.</summary>
    public static bool TryGetMinTfFromRules(
        IEnumerable<CompoundAlertRule> rules,
        out int minMinutes,
        out string minTfToken)
    {
        minMinutes = int.MaxValue;
        minTfToken = "5";
        var found = false;

        foreach (var rule in rules)
        {
            foreach (var entry in rule.Entries)
            {
                if (!TryGetTfMinutes(entry.TfToken, out var m))
                    continue;

                if (!found || m < minMinutes)
                {
                    minMinutes = m;
                    minTfToken = entry.TfToken;
                    found = true;
                }
            }
        }

        if (!found)
        {
            minMinutes = 5;
            minTfToken = "5";
            return false;
        }

        return true;
    }

    /// <summary>
    /// True when compound FIRE should run — aligned to smallest TF in rule (TV cadence).
    /// </summary>
    public static bool IsEvalSyncPoint(
        string chartTfToken,
        int minTfMinutes,
        bool isBarClosed,
        bool isNewBarOnRealtimeForming,
        int barOpenMinute,
        int minTfBarsClosedThisPass)
    {
        if (!TryGetTfMinutes(chartTfToken, out var chartMinutes))
            chartMinutes = minTfMinutes;

        if (chartMinutes < minTfMinutes)
        {
            // Chart faster than min rule TF — sync at min-TF clock boundary (e.g. M5 chart, min M15).
            if (!(isBarClosed || isNewBarOnRealtimeForming))
                return false;

            return minTfMinutes > 0 && barOpenMinute % minTfMinutes == 0;
        }

        if (chartMinutes == minTfMinutes)
            return isBarClosed || isNewBarOnRealtimeForming;

        // Chart slower than min rule TF — sync when min-TF engine closed bar(s) this pass.
        return minTfBarsClosedThisPass > 0;
    }

    public static bool IsEventActiveOnCurrentBar(
        CompoundEvalSyncMode syncMode,
        int lastFiredBarIndex,
        int currentBarIndex,
        int eventValidBars)
    {
        if (lastFiredBarIndex < 0 || currentBarIndex < 0)
            return false;

        return syncMode switch
        {
            CompoundEvalSyncMode.TradingView => lastFiredBarIndex == currentBarIndex,
            CompoundEvalSyncMode.PineEventWindow =>
                currentBarIndex - lastFiredBarIndex <= eventValidBars,
            _ => currentBarIndex - lastFiredBarIndex <= eventValidBars,
        };
    }

    /// <summary>
    /// Source-aware sync: valid when the source-TF candidate bar closed this pass and
    /// state refresh completed. Does not require chart-TF close when source TF is finer.
    /// </summary>
    public static bool IsEvalSyncPointForSourceCandidate(
        string chartTfToken,
        CompoundSourceEventCandidate candidate,
        bool candidateClosedThisPass,
        bool isBackfillCompleted)
    {
        if (!isBackfillCompleted || candidate == null!)
            return false;

        if (!candidateClosedThisPass)
            return false;

        if (!TryGetTfMinutes(candidate.SourceTfToken, out var sourceMinutes))
            return true;

        if (!TryGetTfMinutes(chartTfToken, out var chartMinutes))
            return true;

        // Source close is sufficient — chart TF may be coarser (e.g. M15 chart / M5 source).
        if (chartMinutes >= sourceMinutes)
            return true;

        // Chart faster than source: still valid at source close after refresh.
        return true;
    }
}
