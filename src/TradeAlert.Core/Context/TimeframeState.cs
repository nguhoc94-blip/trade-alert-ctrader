using TradeAlert.Core.Models;
using TradeAlert.Core.Series;

namespace TradeAlert.Core.Context;

public sealed class TimeframeState
{
    public string Symbol { get; init; } = "";
    public string TimeframeId { get; init; } = "";
    public SeriesBuffer Series { get; init; } = null!;
    public BarRuntimeFlags CurrentBarFlags { get; set; }
    public int CurrentBarIndex { get; set; }
}
