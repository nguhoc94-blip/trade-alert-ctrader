using TradeAlert.Core.Models;

namespace TradeAlert.Indicator;

/// <summary>Pine barstate parity — chỉ một phần (Integration).</summary>
public static class Loop6BarRuntimeMapper
{
    public static BarRuntimeFlags ForHistoricalBackfillRow(int indexInclusive, int highestBarIndexInclusive) =>
        new(
            IsFirst: indexInclusive == 0,
            IsLast: indexInclusive == highestBarIndexInclusive,
            IsConfirmed: true,
            IsRealtime: false,
            IsNew: true,
            IsHistory: true);

    public static BarRuntimeFlags ForClosedChartBarBeforeLast() =>
        new(IsFirst: false, IsLast: false, IsConfirmed: true, IsRealtime: false, IsNew: false, IsHistory: true);

    /// <summary>Lần đầu Calculate cho nến cuối sau attach (sóng history) — không realtime forming.</summary>
    public static BarRuntimeFlags ForAttachHistoryPhaseLastBar() =>
        new(IsFirst: false, IsLast: true, IsConfirmed: true, IsRealtime: false, IsNew: false, IsHistory: true);

    /// <summary>Nến cuối realtime đang hình thành (sau khi live bắt đầu).</summary>
    public static BarRuntimeFlags ForLiveRealtimeFormingLastBar(bool isNewBarViaOpenTime) =>
        new(
            IsFirst: false,
            IsLast: true,
            IsConfirmed: false,
            IsRealtime: true,
            IsNew: isNewBarViaOpenTime,
            IsHistory: false);

    /// <summary>Backfill sweep row — Pine historical bar at close (offset [0]).</summary>
    public static BarRuntimeFlags ForNonRealtimePlatformBar(int barIndexInclusive, int highestBarIndexInclusive) =>
        ForHistoricalBackfillRow(indexInclusive: barIndexInclusive, highestBarIndexInclusive: highestBarIndexInclusive);

    /// <summary>Backtest nến cuối đang hình thành — IsNew chỉ khi OpenTime đổi.</summary>
    public static BarRuntimeFlags ForBacktestFormingLastBar(bool isNewBarViaOpenTime) =>
        new(
            IsFirst: false,
            IsLast: true,
            IsConfirmed: false,
            IsRealtime: false,
            IsNew: isNewBarViaOpenTime,
            IsHistory: false);

    /// <summary>Backtest nến cuối chart đã đóng hẳn (không còn bar sau) — key-break offset [0].</summary>
    public static BarRuntimeFlags ForBacktestClosedHistoricalLastBar() =>
        new(
            IsFirst: false,
            IsLast: true,
            IsConfirmed: true,
            IsRealtime: false,
            IsNew: false,
            IsHistory: false);
}
