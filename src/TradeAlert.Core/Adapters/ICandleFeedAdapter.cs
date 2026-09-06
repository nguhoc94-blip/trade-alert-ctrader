using TradeAlert.Core.Models;

namespace TradeAlert.Core.Adapters;

/// <summary>Contract: cTrader Bars → model. Loop 5 implement thật.</summary>
public interface ICandleFeedAdapter
{
    int BarCount { get; }
    BarSnapshot ToBarSnapshot(int index, bool isLastBarForming);
    BarRuntimeFlags GetRuntimeFlags(int index);
}
