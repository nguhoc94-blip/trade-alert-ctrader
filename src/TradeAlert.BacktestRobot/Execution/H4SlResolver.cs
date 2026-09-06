using System;
using TradeAlert.Core.Models;
using TradeAlert.Core.Series;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>
/// H4 SL mode: thay base SL bằng High/Low của H4/H1 bar đã đóng <b>trước hoặc tại</b> <c>slEvalTime</c>
/// (chống future-leak), lùi thêm keylevel width, guard theo band [trần dưới, trần trên].
/// <list type="bullet">
///   <item>Ngoài band dưới → floor về bandLow stat (thường p25).</item>
///   <item>Ngoài band trên → fallback theo <c>hiFallback</c>:
///     stat (mean/p25/p75/pmin/pmax) hoặc <c>SlH1</c> (tìm H1 bar trong band).</item>
///   <item>Không tìm được bar nào (<c>no-h4-bar</c>) → caller gọi <see cref="ApplyNoBarFallback"/>
///     để áp dụng cùng chiến lược hi-fallback.</item>
/// </list>
/// </summary>
public static class H4SlResolver
{
    public static bool TryResolve(
        bool isBuy,
        double baseSl,
        double entry,
        SeriesBuffer htfBuf,
        DateTime slEvalTime,
        TimeSpan htfPeriod,
        in BreakDepthStats stats,
        double pipSize,
        int maxLookbackBars,
        H4SlBandStatRef bandLowStat,
        H4SlBandStatRef bandHighStat,
        double keylevelWidth,
        H4SlHiFallbackRef hiFallback,
        SeriesBuffer? h1FallbackBuf,
        H4SlBandStatRef slH1NoBarFallback,
        out double resolvedSl,
        out string reason)
    {
        resolvedSl = baseSl;
        reason = string.Empty;

        if (htfBuf is null || pipSize <= 0)
        {
            reason = "no-buf";
            return false;
        }

        var (bandLow, bandHigh) = BreakDepthStatistics.ResolveBandPips(in stats, bandLowStat, bandHighStat);
        var maxBars = maxLookbackBars > 0 ? maxLookbackBars : 50;

        for (var offset = 0; offset <= maxBars; offset++)
        {
            if (!htfBuf.TryGetSnapshotAtOffset(offset, out var snap))
                continue;

            var closeTime = snap.OpenChartTimeLocal + htfPeriod;
            if (closeTime > slEvalTime)
                continue;

            double barExtreme;
            if (isBuy)
            {
                if (!(snap.Low < baseSl)) continue;
                barExtreme = snap.Low;
            }
            else
            {
                if (!(snap.High > baseSl)) continue;
                barExtreme = snap.High;
            }

            // Lùi SL thêm 1 chiều rộng keylevel ngoài mép HTF bar (BUY: thấp hơn Low; SELL: cao hơn High).
            var klOffset = keylevelWidth > 0 ? keylevelWidth : 0;
            var candidate = isBuy ? barExtreme - klOffset : barExtreme + klOffset;
            var klNote = klOffset > 0
                ? $" htf={barExtreme:0.#####} kl={klOffset / pipSize:0.##}pip"
                : string.Empty;

            var candidateDist = Math.Abs(entry - candidate) / pipSize;

            if (candidateDist >= bandLow && candidateDist <= bandHigh)
            {
                resolvedSl = candidate;
                reason = $"H4-band sl={candidate:0.#####} dist={candidateDist:0.##}pip{klNote} lo={bandLow:0.##}({BreakDepthStatistics.FormatStatRef(bandLowStat)}) hi={bandHigh:0.##}({BreakDepthStatistics.FormatStatRef(bandHighStat)})";
            }
            else if (candidateDist < bandLow)
            {
                resolvedSl = isBuy ? entry - bandLow * pipSize : entry + bandLow * pipSize;
                reason = $"H4-lo-fallback sl={resolvedSl:0.#####} dist={candidateDist:0.##}pip{klNote} {BreakDepthStatistics.FormatStatRef(bandLowStat)}={bandLow:0.##} lo={bandLow:0.##} hi={bandHigh:0.##}";
            }
            else
            {
                // dist > bandHigh: HTF bar quá xa → fallback theo hiFallback.
                reason = ApplyHiFallback(
                    isBuy, entry, candidate, candidateDist, klNote,
                    in stats, pipSize, bandLow, bandHigh,
                    bandLowStat, bandHighStat,
                    hiFallback, h1FallbackBuf, slH1NoBarFallback,
                    slEvalTime, keylevelWidth,
                    out resolvedSl);
            }
            return true;
        }

        reason = "no-h4-bar";
        return false;
    }

    /// <summary>
    /// Khi không tìm được HTF bar nào (no-h4-bar), áp dụng cùng chiến lược hi-fallback
    /// như trường hợp dist &gt; bandHigh — không giữ base SL.
    /// </summary>
    public static void ApplyNoBarFallback(
        bool isBuy,
        double entry,
        in BreakDepthStats stats,
        double pipSize,
        H4SlBandStatRef bandLowStat,
        H4SlBandStatRef bandHighStat,
        H4SlHiFallbackRef hiFallback,
        SeriesBuffer? h1FallbackBuf,
        H4SlBandStatRef slH1NoBarFallback,
        DateTime slEvalTime,
        double keylevelWidth,
        out double resolvedSl,
        out string reason)
    {
        var (bandLow, bandHigh) = BreakDepthStatistics.ResolveBandPips(in stats, bandLowStat, bandHighStat);
        reason = ApplyHiFallback(
            isBuy, entry,
            candidate: 0, candidateDist: 0, klNote: " no-bar",
            in stats, pipSize, bandLow, bandHigh,
            bandLowStat, bandHighStat,
            hiFallback, h1FallbackBuf, slH1NoBarFallback,
            slEvalTime, keylevelWidth,
            out resolvedSl);
        // Prefix "nobar" để phân biệt trong log với trường hợp dist > bandHigh.
        reason = reason.Replace("H4-hi-", "H4-nobar-", StringComparison.Ordinal);
    }

    // ── private helpers ─────────────────────────────────────────────────────

    static string ApplyHiFallback(
        bool isBuy,
        double entry,
        double candidate,
        double candidateDist,
        string klNote,
        in BreakDepthStats stats,
        double pipSize,
        double bandLow,
        double bandHigh,
        H4SlBandStatRef bandLowStat,
        H4SlBandStatRef bandHighStat,
        H4SlHiFallbackRef hiFallback,
        SeriesBuffer? h1FallbackBuf,
        H4SlBandStatRef slH1NoBarFallback,
        DateTime slEvalTime,
        double keylevelWidth,
        out double resolvedSl)
    {
        if (hiFallback == H4SlHiFallbackRef.SlH1 && h1FallbackBuf is not null)
        {
            // Tìm H1 bar trong band thay vì dùng stat.
            if (TryFindH1InBand(
                    isBuy, entry, h1FallbackBuf, slEvalTime,
                    in stats, pipSize, bandLow, bandHigh, keylevelWidth,
                    out var h1Sl, out var h1Note))
            {
                resolvedSl = h1Sl;
                return $"H4-hi-slh1 sl={h1Sl:0.#####} h4dist={candidateDist:0.##}pip{klNote}{h1Note} lo={bandLow:0.##}({BreakDepthStatistics.FormatStatRef(bandLowStat)}) hi={bandHigh:0.##}({BreakDepthStatistics.FormatStatRef(bandHighStat)})";
            }

            // H1 bar không tìm được → dùng slH1NoBarFallback stat (configurable).
            var noBarDist = BreakDepthStatistics.ResolveStatPips(in stats, slH1NoBarFallback);
            if (noBarDist <= 0) noBarDist = bandHigh;
            resolvedSl = isBuy ? entry - noBarDist * pipSize : entry + noBarDist * pipSize;
            var noBarLabel = BreakDepthStatistics.FormatStatRef(slH1NoBarFallback);
            return $"H4-hi-slh1-nobar sl={resolvedSl:0.#####} h4dist={candidateDist:0.##}pip{klNote} {noBarLabel}={noBarDist:0.##} lo={bandLow:0.##}({BreakDepthStatistics.FormatStatRef(bandLowStat)}) hi={bandHigh:0.##}({BreakDepthStatistics.FormatStatRef(bandHighStat)})";
        }

        // Stat-based fallback.
        var fallbackDist = HiFallbackDist(in stats, hiFallback, bandHigh);
        resolvedSl = isBuy ? entry - fallbackDist * pipSize : entry + fallbackDist * pipSize;
        var fbLabel = FormatHiFallbackRef(hiFallback);
        return $"H4-hi-fallback sl={resolvedSl:0.#####} dist={candidateDist:0.##}pip{klNote} {fbLabel}={fallbackDist:0.##} lo={bandLow:0.##}({BreakDepthStatistics.FormatStatRef(bandLowStat)}) hi={bandHigh:0.##}({BreakDepthStatistics.FormatStatRef(bandHighStat)})";
    }

    /// <summary>Quét H1 bar trong band [bandLow, bandHigh]. Trả true + SL nếu tìm được bar thỏa.</summary>
    static bool TryFindH1InBand(
        bool isBuy,
        double entry,
        SeriesBuffer h1Buf,
        DateTime slEvalTime,
        in BreakDepthStats stats,
        double pipSize,
        double bandLow,
        double bandHigh,
        double keylevelWidth,
        out double sl,
        out string note)
    {
        sl = 0;
        note = string.Empty;
        var h1Period = TimeSpan.FromHours(1);
        var maxBars = 50;

        for (var offset = 0; offset <= maxBars; offset++)
        {
            if (!h1Buf.TryGetSnapshotAtOffset(offset, out var snap))
                continue;

            var closeTime = snap.OpenChartTimeLocal + h1Period;
            if (closeTime > slEvalTime)
                continue;

            double barExtreme;
            if (isBuy)
                barExtreme = snap.Low;
            else
                barExtreme = snap.High;

            var klOffset = keylevelWidth > 0 ? keylevelWidth : 0;
            var candidate = isBuy ? barExtreme - klOffset : barExtreme + klOffset;
            var dist = Math.Abs(entry - candidate) / pipSize;

            if (dist < bandLow || dist > bandHigh)
                continue;

            sl = candidate;
            var klPart = klOffset > 0 ? $" kl={klOffset / pipSize:0.##}pip" : string.Empty;
            note = $" H1={barExtreme:0.#####}{klPart} h1dist={dist:0.##}pip";
            return true;
        }

        return false;
    }

    static double HiFallbackDist(in BreakDepthStats stats, H4SlHiFallbackRef fallback, double bandHigh) =>
        fallback switch
        {
            H4SlHiFallbackRef.P25  => stats.P25  > 0 ? stats.P25  : bandHigh,
            H4SlHiFallbackRef.P75  => stats.P75  > 0 ? stats.P75  : bandHigh,
            H4SlHiFallbackRef.PMin => stats.PMin > 0 ? stats.PMin : bandHigh,
            H4SlHiFallbackRef.PMax => stats.PMax > 0 ? stats.PMax : bandHigh,
            _                      => stats.Mean > 0 ? stats.Mean : bandHigh,  // Mean (default)
        };

    static string FormatHiFallbackRef(H4SlHiFallbackRef fallback) =>
        fallback switch
        {
            H4SlHiFallbackRef.P25  => "p25",
            H4SlHiFallbackRef.P75  => "p75",
            H4SlHiFallbackRef.PMin => "pmin",
            H4SlHiFallbackRef.PMax => "pmax",
            H4SlHiFallbackRef.SlH1 => "slh1",
            _                      => "mean",
        };
}
