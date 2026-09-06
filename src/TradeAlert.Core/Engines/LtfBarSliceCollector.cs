using System;
using System.Collections.Generic;
using TradeAlert.Core.Models.KeyLevel;

namespace TradeAlert.Core.Engines;

/// <summary>
/// Pure slice: LTF bars trong thời gian hình thành HTF.
/// HTF/LTF chia hết (H4/H1, M15/M5): [htfOpen, htfOpenNext).
/// Không chia hết (M5/M2): lấy <see cref="CountLtfBarsForHtf"/> nến LTF liên tiếp từ open đầu tiên ≥ htfOpen (overlap OK).
/// </summary>
public static class LtfBarSliceCollector
{
    /// <summary>Số nến LTF cho 1 nến HTF — M5/M2 = ceil(2.5)+1 = 4.</summary>
    public static int CountLtfBarsForHtf(int htfSeconds, int ltfSeconds)
    {
        if (ltfSeconds <= 0)
            return 0;
        if (htfSeconds % ltfSeconds == 0)
            return htfSeconds / ltfSeconds;

        var ceilRatio = (int)Math.Ceiling((double)htfSeconds / ltfSeconds);
        return ceilRatio + 1;
    }

    public static bool UsesConsecutiveLtfSlice(int htfSeconds, int ltfSeconds) =>
        ltfSeconds > 0 && htfSeconds % ltfSeconds != 0;

    public static LtfBarBundle? Collect(
        DateTime htfOpen,
        DateTime htfOpenNext,
        IReadOnlyList<DateTime> ltfOpenTimes,
        IReadOnlyList<double> ltfOpen,
        IReadOnlyList<double> ltfHigh,
        IReadOnlyList<double> ltfLow,
        IReadOnlyList<double> ltfClose,
        Func<DateTime, DateTime>? normalizeTime = null,
        int? htfSeconds = null,
        int? ltfSeconds = null,
        DateTime? clipBeforeExclusive = null) =>
        CollectCore(
            htfOpen, htfOpenNext, ltfOpenTimes, ltfOpen, ltfHigh, ltfLow, ltfClose,
            normalizeTime, htfSeconds, ltfSeconds, clipBeforeExclusive);

    static LtfBarBundle? CollectCore(
        DateTime htfOpen,
        DateTime htfOpenNext,
        IReadOnlyList<DateTime> ltfOpenTimes,
        IReadOnlyList<double> ltfOpen,
        IReadOnlyList<double> ltfHigh,
        IReadOnlyList<double> ltfLow,
        IReadOnlyList<double> ltfClose,
        Func<DateTime, DateTime>? normalizeTime,
        int? htfSeconds,
        int? ltfSeconds,
        DateTime? clipBeforeExclusive)
    {
        normalizeTime ??= static t => t;
        if (ltfOpenTimes.Count == 0)
            return null;

        var start = FindFirstIndexAtOrAfter(ltfOpenTimes, htfOpen, normalizeTime);
        if (start >= ltfOpenTimes.Count)
            return null;

        var o = new List<double>();
        var h = new List<double>();
        var l = new List<double>();
        var c = new List<double>();

        if (htfSeconds is > 0 && ltfSeconds is > 0 && UsesConsecutiveLtfSlice(htfSeconds.Value, ltfSeconds.Value))
        {
            var take = CountLtfBarsForHtf(htfSeconds.Value, ltfSeconds.Value);
            for (var n = 0; n < take && start + n < ltfOpenTimes.Count; n++)
            {
                var i = start + n;
                var t = normalizeTime(ltfOpenTimes[i]);
                if (t < htfOpen)
                    continue;
                if (clipBeforeExclusive.HasValue && t >= clipBeforeExclusive.Value)
                    break;

                o.Add(ltfOpen[i]);
                h.Add(ltfHigh[i]);
                l.Add(ltfLow[i]);
                c.Add(ltfClose[i]);
            }
        }
        else
        {
            for (var i = start; i < ltfOpenTimes.Count; i++)
            {
                var t = normalizeTime(ltfOpenTimes[i]);
                if (t < htfOpen)
                    continue;
                if (t >= htfOpenNext)
                    break;
                if (clipBeforeExclusive.HasValue && t >= clipBeforeExclusive.Value)
                    break;

                o.Add(ltfOpen[i]);
                h.Add(ltfHigh[i]);
                l.Add(ltfLow[i]);
                c.Add(ltfClose[i]);
            }
        }

        return o.Count == 0 ? null : new LtfBarBundle(o, h, l, c);
    }

    static int FindFirstIndexAtOrAfter(
        IReadOnlyList<DateTime> openTimes,
        DateTime target,
        Func<DateTime, DateTime> normalizeTime)
    {
        var lo = 0;
        var hi = openTimes.Count - 1;
        var result = openTimes.Count;
        while (lo <= hi)
        {
            var mid = lo + (hi - lo) / 2;
            var t = normalizeTime(openTimes[mid]);
            if (t >= target)
            {
                result = mid;
                hi = mid - 1;
            }
            else
                lo = mid + 1;
        }
        return result;
    }
}
