namespace TradeAlert.Core.Engines;

/// <summary>Port read-only từ lib_filters — chữ ký Func&lt;int,double&gt; dùng offset [0..] giống Pine series indexing tại bar đánh giá hiện tại.</summary>
public static class FilterEngine
{
    /// <summary>Noise TF ladder — see also <see cref="TimeframeMapping.GetNoiseTfToken"/>.</summary>
    public static string GetNoiseTf(int timeframeInSeconds) =>
        timeframeInSeconds >= 240 * 60 ? "60"
        : timeframeInSeconds >= 60 * 60 ? "15"
        : timeframeInSeconds >= 15 * 60 ? "5"
        : timeframeInSeconds >= 5 * 60 ? "2"
        : "1";

    public static bool IsNoisyWick(
        int typ,
        bool useNoisy,
        double atrHtf,
        int barsCheck,
        double atrMultNoise,
        double strongBodyAtrMult,
        Func<int, double> openHtf,
        Func<int, double> highHtf,
        Func<int, double> lowHtf,
        Func<int, double> closeHtf)
    {
        if (!useNoisy)
            return false;

        double totalWick = 0;
        var hasStrongBody = false;
        for (var i = 0; i < barsCheck; i++)
        {
            var o = openHtf(i);
            var c = closeHtf(i);
            var h = highHtf(i);
            var l = lowHtf(i);
            if (double.IsNaN(o) || double.IsNaN(c))
                return false;

            var body = Math.Abs(c - o);
            var wick = typ == 1
                ? h - Math.Max(o, c)
                : Math.Min(o, c) - l;

            if (wick > body)
                totalWick += wick;

            if (body > atrHtf * strongBodyAtrMult)
                hasStrongBody = true;
        }

        return !hasStrongBody && totalWick > atrHtf * atrMultNoise;
    }

    public static bool IsRealPullback(int typ, int pivotLag, bool useFilter, Func<int, double> open, Func<int, double> close, int barIndex)
    {
        if (!useFilter)
            return true;

        var i = pivotLag;
        if (i + 1 > barIndex)
            return false;

        var curBodyHigh = Math.Max(open(i), close(i));
        var curBodyLow = Math.Min(open(i), close(i));
        var prevBodyHigh = Math.Max(open(i + 1), close(i + 1));
        var prevBodyLow = Math.Min(open(i + 1), close(i + 1));
        var curGreen = close(i) > open(i);
        var curRed = close(i) < open(i);
        var prevGreen = close(i + 1) > open(i + 1);
        var prevRed = close(i + 1) < open(i + 1);

        if (typ == -1)
            return prevGreen && curRed && curBodyLow < prevBodyLow;
        return prevRed && curGreen && curBodyHigh > prevBodyHigh;
    }

    public static bool ValidPullbackLow(
        int pivotBar,
        bool useFilter,
        Func<int, double> open,
        Func<int, double> close,
        Func<int, double> bodyLow,
        int barIndex)
    {
        if (!useFilter)
            return true;

        var lag = barIndex - pivotBar;
        if (lag <= 0)
            return false;

        var isRedNow = close(lag) < open(lag);
        var foundGreen = false;
        var prevGreenBodyLow = double.NaN;

        for (var i = lag + 1; i <= lag + 10; i++)
        {
            if (i >= barIndex)
                continue;
            if (close(i) <= open(i))
                continue;
            foundGreen = true;
            prevGreenBodyLow = Math.Min(open(i), close(i));
            break;
        }

        return isRedNow && foundGreen && bodyLow(lag) < prevGreenBodyLow;
    }

    public static bool ValidPullbackHigh(
        int pivotBar,
        bool useFilter,
        Func<int, double> open,
        Func<int, double> close,
        Func<int, double> bodyHigh,
        int barIndex)
    {
        if (!useFilter)
            return true;

        var lag = barIndex - pivotBar;
        if (lag <= 0)
            return false;

        var isGreenNow = close(lag) > open(lag);
        var foundRed = false;
        var prevRedBodyHigh = double.NaN;

        for (var i = lag + 1; i <= lag + 10; i++)
        {
            if (i >= barIndex)
                continue;
            if (close(i) >= open(i))
                continue;
            foundRed = true;
            prevRedBodyHigh = Math.Max(open(i), close(i));
            break;
        }

        return isGreenNow && foundRed && bodyHigh(lag) > prevRedBodyHigh;
    }

    public static bool FailedByContinuationHigh(int pivBar, double pivPrice, Func<int, double> highAtOffset, int barIndex)
    {
        var failed = false;
        for (var i = pivBar + 1; i <= barIndex; i++)
        {
            var lag = barIndex - i;
            if (lag < 0)
                continue;
            var hi = highAtOffset(lag);
            if (!double.IsNaN(hi) && hi > pivPrice)
            {
                failed = true;
                break;
            }
        }

        return failed;
    }

    public static bool FailedByContinuationLow(int pivBar, double pivPrice, Func<int, double> lowAtOffset, int barIndex)
    {
        var failed = false;
        for (var i = pivBar + 1; i <= barIndex; i++)
        {
            var lag = barIndex - i;
            if (lag < 0)
                continue;
            var lo = lowAtOffset(lag);
            if (!double.IsNaN(lo) && lo < pivPrice)
            {
                failed = true;
                break;
            }
        }

        return failed;
    }
}
