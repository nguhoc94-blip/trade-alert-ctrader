using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TradeAlert.Core.Models.Alerts;

namespace TradeAlert.Core.Engines;

/// <summary>
/// Dump toàn bộ active zone collection (Touch) trên một TF — dùng FireDbg phân tích R1 H1/H4.
/// </summary>
public static class ZoneStateDebugFormatter
{
    public static string FormatFullCatalog(
        ZoneState zones,
        double? touchProbePrice,
        string? touchProbeKind,
        string? realDebugText = null,
        int? evalTouchT = null,
        double? comparePrice = null,
        string? compareLabel = null,
        double? replayHigh = null,
        double? replayLow = null)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("barClose=").Append(zones.BarClose.ToString("F5", inv));
        sb.Append(" closeTouchT=").Append(zones.ZoneTouchResult);

        if (touchProbePrice is double probe)
        {
            sb.Append(" touchProbe=").Append(probe.ToString("F5", inv));
            if (!string.IsNullOrEmpty(touchProbeKind))
                sb.Append('@').Append(touchProbeKind);
            if (evalTouchT.HasValue)
                sb.Append(" evalTouchT=").Append(evalTouchT.Value);
        }

        if (comparePrice is double cmp && !string.IsNullOrEmpty(compareLabel))
            sb.Append(' ').Append(compareLabel).Append('=').Append(cmp.ToString("F5", inv));
        if (replayHigh is double rh)
            sb.Append(" replayH=").Append(rh.ToString("F5", inv));
        if (replayLow is double rl)
            sb.Append(" replayL=").Append(rl.ToString("F5", inv));

        AppendZoneList(sb, "GKL", zones.GreenKeyZones, touchProbePrice, comparePrice, replayHigh, replayLow, inv);
        AppendZoneList(sb, "GOB", zones.GreenObZones, touchProbePrice, comparePrice, replayHigh, replayLow, inv);
        AppendZoneList(sb, "RKL", zones.RedKeyZones, touchProbePrice, comparePrice, replayHigh, replayLow, inv);
        AppendZoneList(sb, "ROB", zones.RedObZones, touchProbePrice, comparePrice, replayHigh, replayLow, inv);

        if (!string.IsNullOrEmpty(realDebugText))
            sb.Append(" | REAL:").Append(realDebugText);

        return sb.ToString();
    }

    /// <summary>Multi-line debug table: one HTF section with all zone rows + touch probe lines.</summary>
    public static void AppendHtfZoneTable(
        StringBuilder sb,
        string tfLabel,
        int barIdx,
        DateTime barOpen,
        double barHigh,
        double barLow,
        double barClose,
        ZoneState zones,
        double buyProbe,
        string? buyProbeKind,
        int buyEvalT,
        bool buyCacheActive,
        double sellProbe,
        string? sellProbeKind,
        int sellEvalT,
        bool sellCacheActive)
    {
        AppendHtfZoneTableHeader(sb, tfLabel, barIdx, barOpen, barHigh, barLow, barClose, zones.ZoneTouchResult);
        AppendHtfZoneTableBody(
            sb, zones, buyProbe, buyProbeKind, buyEvalT, buyCacheActive,
            sellProbe, sellProbeKind, sellEvalT, sellCacheActive);
    }

    public static void AppendHtfZoneTableHeader(
        StringBuilder sb,
        string tfLabel,
        int barIdx,
        DateTime barOpen,
        double barHigh,
        double barLow,
        double barClose,
        int closeTouchT)
    {
        var inv = CultureInfo.InvariantCulture;
        sb.Append("  === ").Append(tfLabel).Append(" ===");
        sb.Append(" bar#").Append(barIdx);
        sb.Append(" O=").Append(barOpen.ToString("s"));
        sb.Append(" H=").Append(barHigh.ToString("F5", inv));
        sb.Append(" L=").Append(barLow.ToString("F5", inv));
        sb.Append(" C=").Append(barClose.ToString("F5", inv));
        sb.Append(" closeTouchT=").Append(closeTouchT);
        sb.Append('\n');
    }

    public static void AppendHtfZoneTableBody(
        StringBuilder sb,
        ZoneState zones,
        double buyProbe,
        string? buyProbeKind,
        int buyEvalT,
        bool buyCacheActive,
        double sellProbe,
        string? sellProbeKind,
        int sellEvalT,
        bool sellCacheActive)
    {
        var inv = CultureInfo.InvariantCulture;
        AppendTouchProbeLine(sb, "canBuyTouchM5", buyProbe, buyProbeKind, buyEvalT, buyCacheActive, inv);
        AppendTouchProbeLine(sb, "canSellTouchM5", sellProbe, sellProbeKind, sellEvalT, sellCacheActive, inv);

        AppendZoneTableRows(sb, "GKL", zones.GreenKeyZones, buyProbe, sellProbe, inv);
        AppendZoneTableRows(sb, "GOB", zones.GreenObZones, buyProbe, sellProbe, inv);
        AppendZoneTableRows(sb, "RKL", zones.RedKeyZones, buyProbe, sellProbe, inv);
        AppendZoneTableRows(sb, "ROB", zones.RedObZones, buyProbe, sellProbe, inv);
    }

    /// <summary>One-line shell sync metadata for HTF touch parity debug (tier 1).</summary>
    public static void AppendHtfSyncHeader(
        StringBuilder sb,
        string chartTfLabel,
        DateTime? chartBarCloseTime,
        bool isChartTfEntry,
        int dispBar,
        int fedLast,
        int nextBar,
        int lastEvalBar,
        int shellStateBar,
        bool isFormingBar,
        bool canFeedDispBar,
        string barCloseSrc,
        bool stateLag)
    {
        sb.Append("    SYNC chart=").Append(chartTfLabel);
        if (chartBarCloseTime.HasValue)
            sb.Append(" chartClose=").Append(chartBarCloseTime.Value.ToString("MM-dd HH:mm"));
        sb.Append(" shell=").Append(isChartTfEntry ? "chart-entry" : "HTF-engine");
        sb.Append(" dispBar=").Append(dispBar);
        sb.Append(" fedLast=").Append(fedLast);
        sb.Append(" next=").Append(nextBar);
        sb.Append(" lastEval=").Append(lastEvalBar);
        sb.Append(" stateBar=").Append(shellStateBar);
        sb.Append(" forming=").Append(isFormingBar ? 'Y' : 'N');
        sb.Append(" canFeedDisp=").Append(canFeedDispBar ? 'Y' : 'N');
        sb.Append(" barCloseSrc=").Append(barCloseSrc);
        if (stateLag)
            sb.Append(" **LAG**");
        sb.Append('\n');
    }

    public static void AppendM5BarTable(
        StringBuilder sb,
        int barIdx,
        DateTime barOpen,
        double barOpenPrice,
        double barHigh,
        double barLow,
        double barClose)
    {
        var inv = CultureInfo.InvariantCulture;
        sb.Append("  === M5 BAR (last closed) ===");
        sb.Append(" bar#").Append(barIdx);
        sb.Append(" O=").Append(barOpen.ToString("s"));
        sb.Append(" o=").Append(barOpenPrice.ToString("F5", inv));
        sb.Append(" H=").Append(barHigh.ToString("F5", inv));
        sb.Append(" L=").Append(barLow.ToString("F5", inv));
        sb.Append(" C=").Append(barClose.ToString("F5", inv));
        sb.Append('\n');
    }

    static void AppendTouchProbeLine(
        StringBuilder sb,
        string condLabel,
        double probe,
        string? probeKind,
        int evalT,
        bool cacheActive,
        CultureInfo inv)
    {
        sb.Append("    ").Append(condLabel).Append(": ");
        sb.Append("probe=").Append(probe.ToString("F5", inv));
        if (!string.IsNullOrEmpty(probeKind))
            sb.Append('@').Append(probeKind);
        sb.Append(" t=").Append(evalT);
        sb.Append(" cache=").Append(cacheActive ? "OK" : "--");
        sb.Append('\n');
    }

    static void AppendZoneTableRows(
        StringBuilder sb,
        string tag,
        IReadOnlyList<(double Low, double High)> list,
        double buyProbe,
        double sellProbe,
        CultureInfo inv)
    {
        sb.Append("    ").Append(tag).Append('(').Append(list.Count).Append("): ");
        if (list.Count == 0)
        {
            sb.Append("none");
        }
        else
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(" | ");
                var z = list[i];
                sb.Append('#').Append(i).Append('[')
                  .Append(z.Low.ToString("F3", inv)).Append('-').Append(z.High.ToString("F3", inv)).Append(']');
                if (buyProbe >= z.Low && buyProbe <= z.High)
                    sb.Append("*buyIN*");
                if (sellProbe >= z.Low && sellProbe <= z.High)
                    sb.Append("*sellIN*");
            }
        }
        sb.Append('\n');
    }

    static void AppendZoneList(
        StringBuilder sb,
        string tag,
        IReadOnlyList<(double Low, double High)> list,
        double? touchProbe,
        double? comparePrice,
        double? replayHigh,
        double? replayLow,
        CultureInfo inv)
    {
        sb.Append(' ').Append(tag).Append('(').Append(list.Count).Append(")=");
        if (list.Count == 0)
        {
            sb.Append("none");
            return;
        }

        for (var i = 0; i < list.Count; i++)
        {
            if (i > 0) sb.Append(';');
            var z = list[i];
            sb.Append('#').Append(i).Append('[')
              .Append(z.Low.ToString("F3", inv)).Append('-').Append(z.High.ToString("F3", inv)).Append(']');
            AppendInsideMarkers(sb, z.Low, z.High, touchProbe, comparePrice, replayHigh, replayLow);
        }
    }

    static void AppendInsideMarkers(
        StringBuilder sb,
        double lo,
        double hi,
        double? touchProbe,
        double? comparePrice,
        double? replayHigh,
        double? replayLow)
    {
        if (touchProbe is double p && p >= lo && p <= hi)
            sb.Append("*probeIN*");
        if (comparePrice is double c && c >= lo && c <= hi)
            sb.Append("*chartIN*");
        if (replayHigh is double rh && rh >= lo && rh <= hi)
            sb.Append("*rHiIN*");
        if (replayLow is double rl && rl >= lo && rl <= hi)
            sb.Append("*rLoIN*");
    }
}
