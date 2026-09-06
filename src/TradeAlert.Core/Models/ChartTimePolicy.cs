namespace TradeAlert.Core.Models;

/// <summary>
/// Baseline chart time: wall clock từ dữ liệu chart (DATA CHART.zip — nguồn ngữ cảnh dự án).
/// Không dùng timezone máy chạy; <see cref="DateTimeKind.Local"/> bị từ chối.
/// </summary>
public static class ChartTimePolicy
{
    /// <summary>Tham chiếu artifact baseline (path operator /mnt/data như trong GIAO TASK).</summary>
    public const string BaselineDataChartZipPath = "/mnt/data/DATA CHART.zip";

    /// <summary>Offset cố định baseline hiện tại: chart local = UTC+7 (không lấy từ máy chạy).</summary>
    public static readonly TimeSpan ChartLocalOffsetFromUtc = TimeSpan.FromHours(7);

    /// <summary>
    /// Chart local canonical: <see cref="DateTimeKind.Unspecified"/> = wall clock trên chart, không phải machine local.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="chartTime"/> là Local hoặc Utc.</exception>
    public static DateTime EnsureChartLocalUnspecified(DateTime chartTime)
    {
        if (chartTime.Kind == DateTimeKind.Local)
            throw new ArgumentException(
                "DateTimeKind.Local bị từ chối: dùng Unspecified làm chart local wall clock từ DATA CHART, không dùng clock máy.",
                nameof(chartTime));

        if (chartTime.Kind == DateTimeKind.Utc)
            throw new ArgumentException(
                "DateTimeKind.Utc không được coi là chart local; dùng UtcInstantToChartLocalWallClock nếu cần đổi từ instant UTC.",
                nameof(chartTime));

        return DateTime.SpecifyKind(chartTime, DateTimeKind.Unspecified);
    }

    /// <summary>Instant UTC → wall chart local Unspecified (UTC+7 baseline).</summary>
    public static DateTime UtcInstantToChartLocalWallClock(DateTime utcInstant)
    {
        if (utcInstant.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Chỉ nhận DateTimeKind.Utc.", nameof(utcInstant));

        var wall = utcInstant + ChartLocalOffsetFromUtc;
        return DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
    }

    /// <summary>
    /// Wall chart local (Unspecified) → instant UTC (derived, không dùng làm canonical parity).
    /// </summary>
    public static DateTime ChartLocalUnspecifiedToUtcInstant(DateTime chartLocalUnspecified)
    {
        var wall = EnsureChartLocalUnspecified(chartLocalUnspecified);
        return DateTime.SpecifyKind(wall - ChartLocalOffsetFromUtc, DateTimeKind.Utc);
    }
}
