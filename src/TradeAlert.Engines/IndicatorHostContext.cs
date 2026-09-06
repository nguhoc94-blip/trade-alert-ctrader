namespace TradeAlert.Indicator;

/// <summary>Trạng thái host indicator — symbol/TF; không order/trade.</summary>
public sealed class IndicatorHostContext
{
    public string Symbol { get; init; } = "";
    public string ChartTimeframeToken { get; init; } = "";
    public bool HostChartIsRealtime { get; init; }
}
