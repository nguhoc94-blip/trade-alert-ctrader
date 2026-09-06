using System;
using TradeAlert.Core.Models;
using TradeAlert.Core.Series;

namespace TradeAlert.Indicator;

/// <summary>
/// Backfill lịch sử một lần rõ ràng; <see cref="OnCalculateBar"/> chỉ Upsert đúng một <paramref name="barIndex"/> — không full-scan mỗi tick.
/// </summary>
public sealed class SeriesBufferSync
{
    public SeriesBuffer Buffer { get; } = new();

    bool _backfillDone;

    public bool BackfillCompleted => _backfillDone;

    public void InitialBackfill(
        int highestBarIndexInclusive,
        Func<int, (BarSnapshot bar, BarRuntimeFlags flags)?> tryGetBar)
    {
        if (_backfillDone)
            return;

        if (highestBarIndexInclusive < 0)
        {
            _backfillDone = true;
            return;
        }

        for (var i = 0; i <= highestBarIndexInclusive; i++)
        {
            var row = tryGetBar(i);
            if (row.HasValue)
                Buffer.Upsert(i, row.Value.bar, row.Value.flags);
        }

        _backfillDone = true;
    }

    public void OnCalculateBar(int barIndex, BarSnapshot bar, BarRuntimeFlags flags)
    {
        if (!_backfillDone)
        {
            throw new InvalidOperationException(
                "Loop5: gọi InitialBackfill trước OnCalculateBar — tránh full-scan ẩn trong Calculate.");
        }

        Buffer.Upsert(barIndex, bar, flags);
    }

    public void ResetForSession()
    {
        _backfillDone = false;
        Buffer.Clear();
    }
}
