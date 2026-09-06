using System.Collections.Generic;
using System.Linq;
using TradeAlert.Core.Models;

namespace TradeAlert.Core.Adapters;

/// <summary>Fake feed cho unit test — không bind cTrader.</summary>
public sealed class FakeCandleFeedAdapter : ICandleFeedAdapter
{
    readonly List<(BarSnapshot bar, BarRuntimeFlags flags)> _bars;

    public FakeCandleFeedAdapter(IEnumerable<(BarSnapshot bar, BarRuntimeFlags flags)> bars)
    {
        _bars = bars.Select(b => (b.bar.NormalizeChartTime(), b.flags)).ToList();
    }

    public int BarCount => _bars.Count;

    public BarSnapshot ToBarSnapshot(int index, bool isLastBarForming)
    {
        if ((uint)index >= (uint)_bars.Count)
            return default;
        return _bars[index].bar;
    }

    public BarRuntimeFlags GetRuntimeFlags(int index)
    {
        if ((uint)index >= (uint)_bars.Count)
            return default;
        return _bars[index].flags;
    }
}
