namespace TradeAlert.BacktestRobot.Execution;

/// <summary>Lựa chọn stat (pip) cho trần dưới / trần trên band guard H4 SL.</summary>
public enum H4SlBandStatRef
{
    Mean = 0,
    P25,
    P75,
    PMin,
    PMax,
}

/// <summary>
/// Lựa chọn fallback khi khoảng cách HTF bar vượt quá trần trên (dist &gt; bandHigh).
/// <list type="bullet">
///   <item><c>Mean/P25/P75/PMin/PMax</c> — dùng stat tương ứng làm SL distance.</item>
///   <item><c>SlH1</c> — tìm bar H1 trong band thay vì dùng stat. Nếu không tìm được thì fallback về Mean.
///     Áp dụng cho mọi slot (kể cả R3–R6); H1 buffer phải được cung cấp qua config.</item>
/// </list>
/// </summary>
public enum H4SlHiFallbackRef
{
    Mean = 0,
    P25,
    P75,
    PMin,
    PMax,
    SlH1,
}
