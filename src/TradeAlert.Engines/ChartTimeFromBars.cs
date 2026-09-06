using System;
using TradeAlert.Core.Models;

namespace TradeAlert.Indicator;

/// <summary>
/// Chuẩn hóa <c>Bars.OpenTimes[i]</c> → chart-local wall <see cref="DateTimeKind.Unspecified"/> (Loop 2/3/5).
/// UTC → +7 cố định; Local → reject; không <c>ToLocalTime()</c> máy.
/// </summary>
public static class ChartTimeFromBars
{
    public static DateTime NormalizeBarOpenTime(DateTime barsOpenTime)
    {
        return barsOpenTime.Kind switch
        {
            DateTimeKind.Unspecified => ChartTimePolicy.EnsureChartLocalUnspecified(barsOpenTime),
            DateTimeKind.Utc => ChartTimePolicy.UtcInstantToChartLocalWallClock(barsOpenTime),
            DateTimeKind.Local => throw new ArgumentException(
                "Bars.OpenTimes Kind=Local bị từ chối: không dùng clock máy; feed chart-local Unspecified hoặc UTC instant.",
                nameof(barsOpenTime)),
            _ => ChartTimePolicy.EnsureChartLocalUnspecified(barsOpenTime)
        };
    }
}
