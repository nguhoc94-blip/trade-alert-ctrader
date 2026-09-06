using System.Collections.Generic;

namespace TradeAlert.Core.Context;

public sealed class EngineContext
{
    public string Symbol { get; init; } = "";
    public IReadOnlyDictionary<string, TimeframeState> Timeframes { get; init; } =
        new Dictionary<string, TimeframeState>();

    public string PrimaryTimeframeId { get; init; } = "";

    public TimeframeState Primary => Timeframes[PrimaryTimeframeId];
}
