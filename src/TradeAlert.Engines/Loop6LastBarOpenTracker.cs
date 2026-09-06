using System;

namespace TradeAlert.Indicator;

/// <summary>
/// Seed open time trong attach silent (không IsNew). Sau live: IsNew=true chỉ khi OpenTime đổi; tick trùng → false.
/// </summary>
public sealed class Loop6LastBarOpenTracker
{
    DateTime _lastTrackedOpen;
    bool _hasTracked;

    /// <summary>Ghi nhận OpenTime lịch sử/attach mà không báo nến mới.</summary>
    public void SeedSilent(DateTime barOpenChartLocal)
    {
        _lastTrackedOpen = barOpenChartLocal;
        _hasTracked = true;
    }

    /// <summary>
    /// Lần đầu được gọi sau reset: chỉ seed, luôn false.
    /// Sau đó: true khi <paramref name="barOpenChartLocal"/> khác mốc đã tracked.
    /// </summary>
    public bool TryConsumeNewBarOpen(DateTime barOpenChartLocal)
    {
        if (!_hasTracked)
        {
            _lastTrackedOpen = barOpenChartLocal;
            _hasTracked = true;
            return false;
        }

        if (_lastTrackedOpen == barOpenChartLocal)
            return false;

        _lastTrackedOpen = barOpenChartLocal;
        return true;
    }

    public void Reset()
    {
        _lastTrackedOpen = default;
        _hasTracked = false;
    }
}
