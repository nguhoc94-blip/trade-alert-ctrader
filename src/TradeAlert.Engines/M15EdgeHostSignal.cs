namespace TradeAlert.Indicator;

/// <summary>
/// Cạnh M15 do host phát hiện — không mô phỏng <c>ta.change(time("15"))</c>.
/// Mỗi chu kỳ đánh giá gọi <see cref="SetHostM15CloseEdgeForCurrentEvaluation"/> (mặc định false).
/// </summary>
public sealed class M15EdgeHostSignal
{
    bool _hostReportsEdgeThisEval;

    public bool M15CloseEdgeInjected => _hostReportsEdgeThisEval;

    public void SetHostM15CloseEdgeForCurrentEvaluation(bool detectedByHostNotSimulated)
    {
        _hostReportsEdgeThisEval = detectedByHostNotSimulated;
    }
}
