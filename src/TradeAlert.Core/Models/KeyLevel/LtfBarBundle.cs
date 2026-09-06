using System.Collections.Generic;
using TradeAlert.Core.Engines;

namespace TradeAlert.Core.Models.KeyLevel;

/// <summary>Mảng LTF trong một nến HTF — dùng cho wick + OB confirm.</summary>
public sealed class LtfBarBundle : WickNeutralizeEngine.ILtfBarBundle
{
    readonly double[] _open;
    readonly double[] _high;
    readonly double[] _low;
    readonly double[] _close;

    public LtfBarBundle(IReadOnlyList<double> open, IReadOnlyList<double> high,
        IReadOnlyList<double> low, IReadOnlyList<double> close)
    {
        _open = new double[open.Count];
        _high = new double[high.Count];
        _low = new double[low.Count];
        _close = new double[close.Count];
        for (var i = 0; i < open.Count; i++)
        {
            _open[i] = open[i];
            _high[i] = high[i];
            _low[i] = low[i];
            _close[i] = close[i];
        }
    }

    public int Count => _open.Length;
    public double OpenAt(int i) => _open[i];
    public double CloseAt(int i) => _close[i];
    public double HighAt(int i) => _high[i];
    public double LowAt(int i) => _low[i];

    public (double o, double h, double l, double c) OhlcAt(int i) =>
        (_open[i], _high[i], _low[i], _close[i]);
}
