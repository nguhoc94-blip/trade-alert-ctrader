using System.Collections.Generic;
using TradeAlert.Core.Models;

namespace TradeAlert.Core.Series;

/// <summary>
/// Buffer nến theo Pine bar_index. Offset [0] = <see cref="CurrentEvaluationBarIndex"/>;
/// [1] = bar_index - 1, … Không throw trên path đọc mặc định — dùng NaN / TryGet.
/// </summary>
public sealed class SeriesBuffer
{
    readonly Dictionary<int, BarSnapshot> _bars = new();
    readonly Dictionary<int, BarRuntimeFlags> _flags = new();

    public int CurrentEvaluationBarIndex { get; private set; }

    /// <summary>Cập nhật hoặc thêm nến. Cùng barIndex → update forming; barIndex mới → append. Sau Upsert, điểm evaluate = barIndex.</summary>
    public void Upsert(int barIndex, BarSnapshot bar, BarRuntimeFlags flags)
    {
        _bars[barIndex] = bar.NormalizeChartTime();
        _flags[barIndex] = flags;
        CurrentEvaluationBarIndex = barIndex;
    }

    public bool TryGetCurrentFlags(out BarRuntimeFlags flags) =>
        _flags.TryGetValue(CurrentEvaluationBarIndex, out flags);

    public void SetCurrentEvaluationBarIndex(int barIndex)
    {
        CurrentEvaluationBarIndex = barIndex;
    }

    public int Count => _bars.Count;

    public bool TryGetOhlcAtOffset(int offsetFromCurrent, out OhlcTuple ohlc)
    {
        ohlc = OhlcTuple.NaN;
        if (offsetFromCurrent < 0)
            return false;

        var targetIndex = CurrentEvaluationBarIndex - offsetFromCurrent;
        if (!_bars.TryGetValue(targetIndex, out var snap))
            return false;

        ohlc = new OhlcTuple(snap.Open, snap.High, snap.Low, snap.Close);
        return true;
    }

    public OhlcTuple GetOhlcAtOffset(int offsetFromCurrent) =>
        TryGetOhlcAtOffset(offsetFromCurrent, out var o) ? o : OhlcTuple.NaN;

    public double OpenAt(int offsetFromCurrent) => GetOhlcAtOffset(offsetFromCurrent).Open;
    public double HighAt(int offsetFromCurrent) => GetOhlcAtOffset(offsetFromCurrent).High;
    public double LowAt(int offsetFromCurrent) => GetOhlcAtOffset(offsetFromCurrent).Low;
    public double CloseAt(int offsetFromCurrent) => GetOhlcAtOffset(offsetFromCurrent).Close;

    public bool TryGetSnapshotAtOffset(int offsetFromCurrent, out BarSnapshot snapshot)
    {
        snapshot = default;
        if (offsetFromCurrent < 0)
            return false;
        var targetIndex = CurrentEvaluationBarIndex - offsetFromCurrent;
        return _bars.TryGetValue(targetIndex, out snapshot);
    }

    /// <summary>Xóa session buffer — dùng khi indicator reload/stop.</summary>
    public void Clear()
    {
        _bars.Clear();
        _flags.Clear();
        CurrentEvaluationBarIndex = 0;
    }
}
