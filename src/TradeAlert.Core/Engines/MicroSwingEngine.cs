using System;
using System.Collections.Generic;
using TradeAlert.Core.Models.KeyLevel;

namespace TradeAlert.Core.Engines;

/// <summary>
/// Port của lib_micro_swing.pine v2.
///
/// Doji-Force helpers + f_micro_swing_detect rescue rule.
///
/// f_micro_swing_detect: khi bar A (vừa đóng) clear-body + non-doji và close chạm cạnh trong
/// của keylevel swing N → tìm bar M có cực trị giữa A và N → trả về (mBar, mPrice, mTyp).
/// Side-effect: xóa pending queue entries trong khoảng (nBar, aBar) exclusive.
/// </summary>
public sealed class MicroSwingEngine
{
    // -----------------------------------------------------------------------
    // Doji-Force helpers (unchanged from Loop 2 — verified correct)
    // -----------------------------------------------------------------------

    public bool IsDojiSellForce(
        double open, double high, double low, double close,
        double dojiBodyMaxRatio,
        double dojiForceWickRatioMin,
        double dojiForceBodyZoneMax,
        double wickDomRatio)
    {
        var range = high - low;
        if (range <= 0) return false;
        var body      = Math.Abs(close - open);
        var upperWick = high - Math.Max(open, close);
        var lowerWick = Math.Min(open, close) - low;
        var bodyLowPos = (Math.Min(open, close) - low) / range;
        var isDoji    = body / range <= dojiBodyMaxRatio;
        var hasForce  = upperWick / range >= dojiForceWickRatioMin;
        var bodyZone  = bodyLowPos <= dojiForceBodyZoneMax;
        var wickDom   = wickDomRatio <= 1.0 || lowerWick <= 0.0 || upperWick >= lowerWick * wickDomRatio;
        return isDoji && hasForce && bodyZone && wickDom;
    }

    public bool IsDojiBuyForce(
        double open, double high, double low, double close,
        double dojiBodyMaxRatio,
        double dojiForceWickRatioMin,
        double dojiForceBodyZoneMax,
        double wickDomRatio)
    {
        var range = high - low;
        if (range <= 0) return false;
        var body      = Math.Abs(close - open);
        var lowerWick = Math.Min(open, close) - low;
        var upperWick = high - Math.Max(open, close);
        var bodyHighPos = (high - Math.Max(open, close)) / range;
        var isDoji    = body / range <= dojiBodyMaxRatio;
        var hasForce  = lowerWick / range >= dojiForceWickRatioMin;
        var bodyZone  = bodyHighPos <= dojiForceBodyZoneMax;
        var wickDom   = wickDomRatio <= 1.0 || upperWick <= 0.0 || lowerWick >= upperWick * wickDomRatio;
        return isDoji && hasForce && bodyZone && wickDom;
    }

    // -----------------------------------------------------------------------
    // f_micro_swing_detect — rescue rule (now fully ported)
    // -----------------------------------------------------------------------

    /// <summary>Result of f_micro_swing_detect — mirrors Pine [mBar, mPrice, mTyp].</summary>
    public sealed record MicroSwingResult(
        int   MBar,
        double MPrice,
        int   MTyp,
        int   NBar,
        int   ABar,
        IReadOnlyList<(int Bar, double Price)> ClearedPenH)
    {
        public bool Detected => MTyp != 0;

        public static MicroSwingResult Empty { get; } =
            new(0, double.NaN, 0, 0, 0, Array.Empty<(int, double)>());
    }

    /// <summary>
    /// Port Pine f_micro_swing_detect (lib_micro_swing v2 lines 62-169).
    ///
    /// Parameters:
    ///   barIndex          = current bar_index (Pine bar_index at eval time)
    ///   pivots            = IPivotReadOnlyView (pivIndex[], pivPrice[], pivType[], pivKeyBox[])
    ///   effClearBody      = A bar is clear-body
    ///   effIsDoji         = A bar is doji
    ///   effBull           = A bar is bullish (close > open)
    ///   effBear           = A bar is bearish (close &lt; open)
    ///   closeA            = close[1] — bar A = bar_index-1 (đã đóng)
    ///   lastPushedSwingType = 1=HIGH pushed, -1=LOW pushed
    ///   clean             = cleanLow/cleanHigh series (offset from barIndex)
    ///   penH / penL       = pending HIGH / LOW queues (mutated in-place)
    /// </summary>
    public MicroSwingResult Detect(
        bool              useMicroSwingRule,
        int               barIndex,
        IPivotReadOnlyView pivots,
        bool              effClearBody,
        bool              effIsDoji,
        bool              effBull,
        bool              effBear,
        double            closeA,
        int               lastPushedSwingType,
        ICleanOhlcSeries  clean,
        PendingSwingQueue penH,
        PendingSwingQueue penL)
    {
        if (!useMicroSwingRule || barIndex <= 1 || pivots.Count == 0
            || !effClearBody || effIsDoji)
            return MicroSwingResult.Empty;

        // N = last pivot (highest index in array)
        var nIdx   = pivots.Count - 1;
        var nBar   = pivots.GetBarIndex(nIdx);
        var nPr    = pivots.GetPrice(nIdx);
        var nTyp   = pivots.GetType(nIdx);
        var aBar   = barIndex - 1;  // bar A = vừa đóng

        if (nTyp != lastPushedSwingType || (aBar - nBar) < 3)
            return MicroSwingResult.Empty;

        pivots.TryGetKeyBoxRef(nIdx, out var kb);

        bool trigger = false;
        int  typPush = 0;

        if (effBull && nTyp == 1)
        {
            // check if closeA >= keylevel bottom (or pivot price if no box)
            var lvl = kb?.Spec.Bottom ?? nPr;
            if (closeA >= lvl)
            {
                trigger = true;
                typPush = -1;  // rescue LOW
            }
        }
        else if (effBear && nTyp == -1)
        {
            // check if closeA <= keylevel top (or pivot price if no box)
            var lvl = kb?.Spec.Top ?? nPr;
            if (closeA <= lvl)
            {
                trigger = true;
                typPush = 1;  // rescue HIGH
            }
        }

        if (!trigger)
            return MicroSwingResult.Empty;

        // Scan bars between N and A (exclusive) for extreme price
        var maxOff = barIndex - nBar - 2;  // number of bar offsets to check
        int   barRet = -1;
        double prRet = double.NaN;

        if (maxOff >= 1)
        {
            for (var off = 1; off <= maxOff; off++)
            {
                double mv = typPush == -1
                    ? clean.CleanLowAtOffset(off)
                    : clean.CleanHighAtOffset(off);

                if (double.IsNaN(mv))
                    continue;

                var better = barRet == -1
                    || (typPush == -1 && mv < prRet)
                    || (typPush ==  1 && mv > prRet);

                if (better)
                {
                    prRet  = mv;
                    barRet = barIndex - 1 - off;
                }
            }
        }

        if (barRet == -1 || double.IsNaN(prRet))
            return MicroSwingResult.Empty;

        var clearedPenH = new List<(int Bar, double Price)>();
        for (var ci = 0; ci < penH.Count; ci++)
        {
            var ce = penH[ci];
            if (ce.Bar > nBar && ce.Bar < aBar)
                clearedPenH.Add((ce.Bar, ce.Price));
        }

        // Side-effect: clear pending queues for bars strictly inside (nBar, aBar)
        penH.ClearRange(nBar, aBar);
        penL.ClearRange(nBar, aBar);

        return new MicroSwingResult(barRet, prRet, typPush, nBar, aBar, clearedPenH);
    }

    // -----------------------------------------------------------------------
    // Guard context (kept for backward compat — now delegates to Detect)
    // -----------------------------------------------------------------------

    public readonly record struct MicroSwingGuardContext(
        IPivotReadOnlyView? Pivots,
        ICleanOhlcSeries? Clean,
        bool PendingCandidatesAvailable,
        IReadOnlyList<KeyBoxRef?>? PivotKeyBoxesAligned);

    public enum MicroSwingGuardReason
    {
        None,
        MissingPivotView,
        MissingCleanSeries,
        MissingPendingCandidates,
        MissingKeyBoxPerPivot,
        DetectNotPortedLoop3    // kept for backward compat — no longer thrown
    }

    /// <summary>
    /// Guard-only check. Now always returns false with <c>None</c> when all deps present
    /// (caller should use <see cref="Detect"/> directly).
    /// Kept for hosts that check preconditions before calling Detect.
    /// </summary>
    public bool TryMicroSwing3etect(in MicroSwingGuardContext ctx, out MicroSwingGuardReason reason)
    {
        if (ctx.Pivots is null || ctx.Pivots.Count == 0)
        { reason = MicroSwingGuardReason.MissingPivotView; return false; }

        if (ctx.Clean is null)
        { reason = MicroSwingGuardReason.MissingCleanSeries; return false; }

        if (!ctx.PendingCandidatesAvailable)
        { reason = MicroSwingGuardReason.MissingPendingCandidates; return false; }

        if (ctx.PivotKeyBoxesAligned is null || ctx.PivotKeyBoxesAligned.Count != ctx.Pivots.Count)
        { reason = MicroSwingGuardReason.MissingKeyBoxPerPivot; return false; }

        for (var i = 0; i < ctx.Pivots.Count; i++)
        {
            if (ctx.PivotKeyBoxesAligned[i] is null)
            { reason = MicroSwingGuardReason.MissingKeyBoxPerPivot; return false; }
        }

        // All deps present — caller should call Detect() directly
        reason = MicroSwingGuardReason.None;
        return true;
    }

    /// <summary>Legacy throw — now replaced by Detect(). Do not call.</summary>
    [Obsolete("Use Detect() instead. MicroSwing3etect() has been ported.")]
    public void MicroSwing3etect() =>
        throw new InvalidOperationException(
            "MicroSwing3etect() is deprecated. Use Detect() directly — logic is now ported.");
}
