using System.Globalization;
using System.Text;
using TradeAlert.Core.Models.KeyLevel;

namespace TradeAlert.Core.Engines;

/// <summary>Pivot-level audit for HTF touch table parity debug — mirrors <see cref="ZoneCollector"/> filters.</summary>
public static class ZoneCollectorDebug
{
    /// <summary>
    /// Dump pivot keys near the bar range / probe. Only rows with hasKey or box overlap bar/probe are shown.
    /// </summary>
    public static void AppendPivotKeyAudit(
        StringBuilder sb,
        PivotStateStore pivots,
        double barLow,
        double barHigh,
        double buyProbe,
        double sellProbe,
        int maxRows = 16)
    {
        var inv = CultureInfo.InvariantCulture;
        sb.Append("    PIVOT-AUDIT (hasKey or overlaps bar/probe):\n");

        var written = 0;
        for (var i = 0; i < pivots.Count && written < maxRows; i++)
        {
            var hasKey = pivots.GetHasKey(i);
            var kb     = pivots.GetKeyBox(i);
            var typ    = pivots.GetTypeAt(i);
            var flag   = pivots.GetFlag(i);
            var role   = pivots.GetMainRole(i);
            var ext    = pivots.GetKeyExtending(i);

            double lo = 0, hi = 0;
            var hasBox = kb is not null;
            if (hasBox)
            {
                lo = System.Math.Min(kb!.Spec.Top, kb.Spec.Bottom);
                hi = System.Math.Max(kb.Spec.Top, kb.Spec.Bottom);
            }

            var overlapsBar = hasBox && hi >= barLow && lo <= barHigh;
            var overlapsProbe = hasBox
                && ((buyProbe >= lo && buyProbe <= hi) || (sellProbe >= lo && sellProbe <= hi));

            if (!hasKey && !overlapsBar && !overlapsProbe)
                continue;

            var sid = typ == 1 ? $"H{pivots.GetHighId(i)}"
                : typ == -1 ? $"L{pivots.GetLowId(i)}"
                : $"?{i}";

            var outcome = DescribePivotCollectOutcome(hasKey, kb, ext, typ, flag, role);
            sb.Append("      ").Append(sid);
            if (hasBox)
            {
                sb.Append('[').Append(lo.ToString("F3", inv)).Append('-').Append(hi.ToString("F3", inv)).Append(']');
            }
            else
            {
                sb.Append("[no-box]");
            }

            sb.Append(" typ=").Append(typ);
            sb.Append(" flag=").Append(flag);
            sb.Append(" role=").Append(role);
            sb.Append(" ext=").Append(ext ? 'Y' : 'N');
            sb.Append(" → ").Append(outcome);
            sb.Append('\n');
            written++;
        }

        if (written == 0)
            sb.Append("      (none near bar/probe)\n");
    }

    public static string DescribePivotCollectOutcome(
        bool hasKey,
        KeyBoxRef? kb,
        bool keyExtending,
        int typ,
        int flag,
        int mainRole)
    {
        if (!hasKey) return "SKIP !hasKey";
        if (kb is null) return "SKIP kb==null";
        if (!keyExtending) return "SKIP !keyExtending";

        ZoneCollector.ClassifyKeyZone(typ, flag, mainRole, out var isGreen, out var isRed);
        if (isGreen && isRed) return "GKL+RKL";
        if (isGreen) return "GKL";
        if (isRed) return "RKL";
        return FormattableString.Invariant($"SKIP unclassified typ={typ} flag={flag} role={mainRole}");
    }
}
