using System.Collections.Generic;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>
/// Snapshot thống kê khoảng cách phá vỡ swing (đơn vị pip). Dùng struct thường (KHÔNG record struct)
/// để chắc build trên cTrader/cAlgo với phiên bản C# cũ.
/// </summary>
public readonly struct BreakDepthStats
{
    public readonly double P25;
    public readonly double Mean;
    public readonly double P75;
    public readonly double PMin;
    public readonly double PMax;
    public readonly int Count;

    public BreakDepthStats(double p25, double mean, double p75, double pMin, double pMax, int count)
    {
        P25 = p25;
        Mean = mean;
        P75 = p75;
        PMin = pMin;
        PMax = pMax;
        Count = count;
    }
}

/// <summary>
/// Ring buffer giữ tối đa <c>capacity</c> mẫu khoảng cách (pip) khi swing bị broken, để xuất p25/mean/p75
/// làm min/fallback/max SL cho H4 SL mode. Mỗi hướng (BUY stop / SELL stop) dùng một instance riêng.
/// </summary>
public sealed class BreakDepthStatistics
{
    readonly Queue<double> _samples = new Queue<double>();
    readonly int _capacity;

    public BreakDepthStatistics(int capacity)
    {
        _capacity = capacity > 0 ? capacity : 100;
    }

    public int Count => _samples.Count;

    public void Record(double pipDistance)
    {
        if (pipDistance <= 0) return;
        _samples.Enqueue(pipDistance);
        while (_samples.Count > _capacity) _samples.Dequeue();
    }

    /// <summary>Preview stats từ mẫu hiện có (cần ≥1 mẫu). Dùng debug UI — không thay gate minSamples.</summary>
    public bool TryGetPreviewStats(out BreakDepthStats stats) => TryGetStats(1, out stats);

    /// <summary>
    /// Trả p25/mean/p75 khi đủ <paramref name="minSamples"/>. Value-type, không trả null:
    /// chưa đủ mẫu → false, <paramref name="stats"/> = default.
    /// </summary>
    public bool TryGetStats(int minSamples, out BreakDepthStats stats)
    {
        stats = default;
        if (_samples.Count < minSamples) return false;

        var sorted = _samples.ToArray();
        System.Array.Sort(sorted);

        double sum = 0;
        for (var i = 0; i < sorted.Length; i++) sum += sorted[i];
        var mean = sum / sorted.Length;

        var p25 = Percentile(sorted, 0.25);
        var p75 = Percentile(sorted, 0.75);
        stats = new BreakDepthStats(p25, mean, p75, sorted[0], sorted[^1], sorted.Length);
        return true;
    }

    /// <summary>Resolve pip distance from a stat choice.</summary>
    public static double ResolveStatPips(in BreakDepthStats stats, H4SlBandStatRef choice) =>
        choice switch
        {
            H4SlBandStatRef.P25  => stats.P25,
            H4SlBandStatRef.P75  => stats.P75,
            H4SlBandStatRef.PMin => stats.PMin,
            H4SlBandStatRef.PMax => stats.PMax,
            _                    => stats.Mean,
        };

    /// <summary>Band [low, high] from two stat choices; swap if low &gt; high.</summary>
    public static (double Low, double High) ResolveBandPips(
        in BreakDepthStats stats,
        H4SlBandStatRef lowChoice,
        H4SlBandStatRef highChoice)
    {
        var lo = ResolveStatPips(in stats, lowChoice);
        var hi = ResolveStatPips(in stats, highChoice);
        if (lo > hi)
            (lo, hi) = (hi, lo);
        return (lo, hi);
    }

    public static string FormatStatRef(H4SlBandStatRef choice) =>
        choice switch
        {
            H4SlBandStatRef.P25  => "p25",
            H4SlBandStatRef.P75  => "p75",
            H4SlBandStatRef.PMin => "pmin",
            H4SlBandStatRef.PMax => "pmax",
            _                    => "mean",
        };

    /// <summary>Linear-interpolation percentile trên mảng đã sort tăng dần.</summary>
    static double Percentile(double[] sortedAsc, double q)
    {
        var n = sortedAsc.Length;
        if (n == 0) return 0;
        if (n == 1) return sortedAsc[0];

        var rank = q * (n - 1);
        var lo = (int)System.Math.Floor(rank);
        var hi = (int)System.Math.Ceiling(rank);
        if (lo == hi) return sortedAsc[lo];

        var frac = rank - lo;
        return sortedAsc[lo] + (sortedAsc[hi] - sortedAsc[lo]) * frac;
    }
}
