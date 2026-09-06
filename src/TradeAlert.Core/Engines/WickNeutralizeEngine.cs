using System;

namespace TradeAlert.Core.Engines;

/// <summary>
/// Port Pine "NEUTRALIZE WICK NOISE" (lines 258–341) + cleanHigh1Raw/cleanLow1Raw.
/// LTF confirm: <see cref="ILtfBarBundle"/> = nến từ <c>request.security_lower_tf</c> (LTF một bậc dưới HTF chart),
/// không phải <c>request.security</c>. Chỉ neutralize wick khi có LTF bundle và LTF confirm — không fallback ATR.
/// </summary>
public static class WickNeutralizeEngine
{
    /// <summary>
    /// Nến LTF nằm trong một nến HTF tại <paramref name="offset"/> (Pine <c>oLtf[offset]</c> mảng từ <c>security_lower_tf</c>).
    /// </summary>
    public interface ILtfBarBundle
    {
        int Count { get; }
        double OpenAt(int i);
        double HighAt(int i);
        double LowAt(int i);
        double CloseAt(int i);
    }

    public readonly record struct NeutralizeResult(
        double CleanHigh,
        double CleanLow,
        double? NeutralizedHigh,
        double? NeutralizedLow);

    /// <summary>Diagnostic snapshot for wick neutralize at one HTF bar offset (typically lag 1 = bar A).</summary>
    public sealed record WickNeutralizeDiag
    {
        public bool UseWickFilter { get; init; }
        public bool UseLtfWickConfirm { get; init; }
        public double LtfConfirmRatio { get; init; }
        public bool HasLtfData { get; init; }
        public int LtfBarCount { get; init; }
        public bool UseLtfMode { get; init; }
        public double AvgRangePrev { get; init; } = double.NaN;
        public double BodySizePrev { get; init; } = double.NaN;
        public double UpperWick { get; init; } = double.NaN;
        public double LowerWick { get; init; } = double.NaN;
        public bool ValidUpperWick { get; init; }
        public bool ValidLowerWick { get; init; }
        public double LtfMaxBodyHigh { get; init; } = double.NaN;
        public double LtfMinBodyLow { get; init; } = double.NaN;
        /// <summary>Max high across all LTF bars in bundle (wick confirm context).</summary>
        public double LtfMaxHigh { get; init; } = double.NaN;
        /// <summary>Min low across all LTF bars in bundle.</summary>
        public double LtfMinLow { get; init; } = double.NaN;
        public double UpperPenetrationRatio { get; init; } = double.NaN;
        public double LowerPenetrationRatio { get; init; } = double.NaN;
        public bool UpperConfirmed { get; init; }
        public bool LowerConfirmed { get; init; }
        public double CleanHigh { get; init; } = double.NaN;
        public double CleanLow { get; init; } = double.NaN;
    }

    /// <summary>Neutralize bar at <paramref name="offset"/> from evaluation bar (1 = bar A).</summary>
    public static NeutralizeResult NeutralizeBarAtOffset(
        bool useWickNoiseFilter,
        int wickAvgLen,
        bool useLtfWickConfirm,
        double ltfConfirmRatio,
        int offset,
        Func<int, (double open, double high, double low, double close)> ohlcAtLag,
        ILtfBarBundle? ltfAtOffset = null) =>
        NeutralizeBarAtOffsetDetailed(
            useWickNoiseFilter, wickAvgLen, useLtfWickConfirm, ltfConfirmRatio,
            offset, ohlcAtLag, ltfAtOffset).Result;

    public static (NeutralizeResult Result, WickNeutralizeDiag Diag) NeutralizeBarAtOffsetDetailed(
        bool useWickNoiseFilter,
        int wickAvgLen,
        bool useLtfWickConfirm,
        double ltfConfirmRatio,
        int offset,
        Func<int, (double open, double high, double low, double close)> ohlcAtLag,
        ILtfBarBundle? ltfAtOffset = null)
    {
        if (!TryGetOhlc(ohlcAtLag, offset, out var o, out var h, out var l, out var c))
        {
            return (new NeutralizeResult(double.NaN, double.NaN, null, null), new WickNeutralizeDiag
            {
                UseWickFilter = useWickNoiseFilter,
                UseLtfWickConfirm = useLtfWickConfirm,
                LtfConfirmRatio = ltfConfirmRatio,
            });
        }

        if (!useWickNoiseFilter)
        {
            var raw = new NeutralizeResult(h, l, null, null);
            return (raw, new WickNeutralizeDiag
            {
                UseWickFilter = false,
                UseLtfWickConfirm = useLtfWickConfirm,
                LtfConfirmRatio = ltfConfirmRatio,
                HasLtfData = ltfAtOffset is { Count: > 0 },
                LtfBarCount = ltfAtOffset?.Count ?? 0,
                CleanHigh = h,
                CleanLow = l,
            });
        }

        var avgRangePrev = ComputeAvgRange(ohlcAtLag, offset + 1, wickAvgLen);
        if (double.IsNaN(avgRangePrev))
        {
            var noAvg = new NeutralizeResult(h, l, null, null);
            return (noAvg, new WickNeutralizeDiag
            {
                UseWickFilter = true,
                UseLtfWickConfirm = useLtfWickConfirm,
                LtfConfirmRatio = ltfConfirmRatio,
                HasLtfData = ltfAtOffset is { Count: > 0 },
                LtfBarCount = ltfAtOffset?.Count ?? 0,
                CleanHigh = h,
                CleanLow = l,
            });
        }

        var bodySizePrev = Math.Abs(c - o);
        var hasLtfData = ltfAtOffset is { Count: > 0 };
        var useLtfMode = useLtfWickConfirm && hasLtfData;

        var ltfMaxHigh = double.NaN;
        var ltfMinLow = double.NaN;
        if (useLtfMode)
        {
            for (var i = 0; i < ltfAtOffset!.Count; i++)
            {
                var lh = ltfAtOffset.HighAt(i);
                var ll = ltfAtOffset.LowAt(i);
                ltfMaxHigh = double.IsNaN(ltfMaxHigh) ? lh : Math.Max(ltfMaxHigh, lh);
                ltfMinLow = double.IsNaN(ltfMinLow) ? ll : Math.Min(ltfMinLow, ll);
            }
        }

        double? neutralizedHigh = null;
        double? neutralizedLow = null;

        // --- Upper wick ---
        var upperWickPrev = h - Math.Max(o, c);
        var validUpperWick = upperWickPrev > avgRangePrev && upperWickPrev > bodySizePrev * 0.5;

        var upperPenRatio = double.NaN;
        var ltfMaxBody = double.NaN;
        var upperConfirmed = false;
        if (validUpperWick)
        {
            if (useLtfMode)
            {
                for (var i = 0; i < ltfAtOffset!.Count; i++)
                {
                    var bodyH = Math.Max(ltfAtOffset.OpenAt(i), ltfAtOffset.CloseAt(i));
                    ltfMaxBody = double.IsNaN(ltfMaxBody) ? bodyH : Math.Max(ltfMaxBody, bodyH);
                }
                if (!double.IsNaN(ltfMaxBody))
                {
                    var penetration = h - ltfMaxBody;
                    upperPenRatio = upperWickPrev > 0 ? penetration / upperWickPrev : double.NaN;
                    upperConfirmed = upperWickPrev > 0 && upperPenRatio > ltfConfirmRatio;
                }
            }

            if (upperConfirmed)
                neutralizedHigh = Math.Max(o, c);
        }

        // --- Lower wick ---
        var rawBodyLowPrev = Math.Min(o, c);
        var lowerWickPrev = rawBodyLowPrev - l;
        var validLowerWick = lowerWickPrev > avgRangePrev && lowerWickPrev > bodySizePrev * 0.5;

        var lowerPenRatio = double.NaN;
        var ltfMinBody = double.NaN;
        var lowerConfirmed = false;
        if (validLowerWick)
        {
            if (useLtfMode)
            {
                for (var j = 0; j < ltfAtOffset!.Count; j++)
                {
                    var bodyL = Math.Min(ltfAtOffset.OpenAt(j), ltfAtOffset.CloseAt(j));
                    ltfMinBody = double.IsNaN(ltfMinBody) ? bodyL : Math.Min(ltfMinBody, bodyL);
                }
                if (!double.IsNaN(ltfMinBody))
                {
                    var penetrationLow = ltfMinBody - l;
                    lowerPenRatio = lowerWickPrev > 0 ? penetrationLow / lowerWickPrev : double.NaN;
                    lowerConfirmed = lowerWickPrev > 0 && lowerPenRatio > ltfConfirmRatio;
                }
            }

            if (lowerConfirmed)
                neutralizedLow = Math.Min(o, c);
        }

        var cleanHigh = neutralizedHigh ?? h;
        var cleanLow = neutralizedLow ?? l;
        var result = new NeutralizeResult(cleanHigh, cleanLow, neutralizedHigh, neutralizedLow);
        var diag = new WickNeutralizeDiag
        {
            UseWickFilter = true,
            UseLtfWickConfirm = useLtfWickConfirm,
            LtfConfirmRatio = ltfConfirmRatio,
            HasLtfData = hasLtfData,
            LtfBarCount = ltfAtOffset?.Count ?? 0,
            UseLtfMode = useLtfMode,
            AvgRangePrev = avgRangePrev,
            BodySizePrev = bodySizePrev,
            UpperWick = upperWickPrev,
            LowerWick = lowerWickPrev,
            ValidUpperWick = validUpperWick,
            ValidLowerWick = validLowerWick,
            LtfMaxBodyHigh = ltfMaxBody,
            LtfMinBodyLow = ltfMinBody,
            LtfMaxHigh = ltfMaxHigh,
            LtfMinLow = ltfMinLow,
            UpperPenetrationRatio = upperPenRatio,
            LowerPenetrationRatio = lowerPenRatio,
            UpperConfirmed = upperConfirmed,
            LowerConfirmed = lowerConfirmed,
            CleanHigh = cleanHigh,
            CleanLow = cleanLow,
        };
        return (result, diag);
    }

    static double ComputeAvgRange(
        Func<int, (double open, double high, double low, double close)> ohlcAtLag,
        int startOffset,
        int len)
    {
        double sum = 0;
        var n = 0;
        for (var i = 0; i < len; i++)
        {
            if (!TryGetOhlc(ohlcAtLag, startOffset + i, out _, out var h, out var l, out _))
                return double.NaN;
            sum += h - l;
            n++;
        }
        return n < len ? double.NaN : sum / len;
    }

    static bool TryGetOhlc(
        Func<int, (double open, double high, double low, double close)> fn,
        int off,
        out double o,
        out double h,
        out double l,
        out double c)
    {
        var t = fn(off);
        o = t.open;
        h = t.high;
        l = t.low;
        c = t.close;
        return !double.IsNaN(h) && !double.IsNaN(l);
    }
}
