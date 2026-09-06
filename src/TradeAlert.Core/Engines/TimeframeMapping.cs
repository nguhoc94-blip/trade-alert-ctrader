namespace TradeAlert.Core.Engines;

/// <summary>
/// Pine HTF/LTF/Noise TF resolvers — <c>pine code arlert new.pine</c> lines 93–100, 343–345, 187–241.
/// </summary>
public static class TimeframeMapping
{
    /// <summary>
    /// Chart TF (HTF) = <c>timeframe.period</c> — toàn bộ swing/keylevel chạy trên series này.
    /// </summary>
    public static string ChartTfToken(int timeframeInSeconds) => timeframeInSeconds switch
    {
        60 => "1",
        120 => "2",
        300 => "5",
        900 => "15",
        3600 => "60",
        14400 => "240",
        86400 => "D",
        604800 => "W",
        _ => timeframeInSeconds >= 86400 ? "D" : timeframeInSeconds.ToString()
    };

    /// <summary>
    /// Wick LTF = <c>f_resolve_ltf_by_htf()</c> — một bậc THẤP hơn chart TF.
    /// Dữ liệu qua <c>request.security_lower_tf(sym, ltfTf, ohlc)</c> → mảng nến LTF <b>bên trong</b> mỗi nến HTF.
    /// <c>oLtf[1]</c> = các nến LTF tạo nên HTF bar <c>[1]</c> (bar vừa đóng).
    /// </summary>
    public static string? ResolveLtfTokenByHtf(int htfSeconds) => ChartTfToken(htfSeconds) switch
    {
        "5" => "2",
        "15" => "5",
        "60" => "15",
        "240" => "60",
        "D" => "240",
        "W" => "D",
        _ => null
    };

    public static int? ResolveLtfSecondsByHtf(int htfSeconds)
    {
        var token = ResolveLtfTokenByHtf(htfSeconds);
        return token switch
        {
            "1" => 60,
            "2" => 120,
            "5" => 300,
            "15" => 900,
            "60" => 3600,
            "240" => 14400,
            "D" => 86400,
            null => null,
            _ => null
        };
    }

    /// <summary>
    /// Noise TF = <c>filters.f_get_noise_tf()</c> — <c>request.security(sym, noiseTF, ohlc)</c>.
    /// Khác LTF wick: ladder theo giây, dùng cho prefetch noisy OHLC (legacy; wick chính đã preprocess).
    /// </summary>
    public static string GetNoiseTfToken(int htfSeconds) => FilterEngine.GetNoiseTf(htfSeconds);

    /// <summary>M5 cố định — touch/event (Pine ~187), không phụ thuộc chart TF.</summary>
    public const string FixedM5Token = "5";

    /// <summary>M15 cố định — touch @ M15 close (Pine ~241).</summary>
    public const string FixedM15Token = "15";

    /// <summary>Chart TF token → giây (cho host LTF collector).</summary>
    public static int? ChartSecondsFromToken(string chartTfToken) => chartTfToken switch
    {
        "1" => 60,
        "2" => 120,
        "3" => 180,
        "4" => 240,
        "5" => 300,
        "6" => 360,
        "7" => 420,
        "8" => 480,
        "9" => 540,
        "10" => 600,
        "15" => 900,
        "20" => 1200,
        "30" => 1800,
        "45" => 2700,
        "60" => 3600,
        "240" => 14400,
        "D" => 86400,
        "W" => 604800,
        _ => int.TryParse(chartTfToken, out var m) && m > 0 ? m * 60 : null
    };
}
