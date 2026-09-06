using System;
using System.Collections.Generic;
using cAlgo.API;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.KeyLevel;
using TradeAlert.Indicator;

namespace TradeAlert.Indicator.Host;

/// <summary>
/// Thu thập nến LTF trong phạm vi một nến HTF vừa đóng — tương đương Pine <c>oLtf[1]</c> + <c>f_push_ltf_snapshot</c>.
/// </summary>
public sealed class HostLtfCollector
{
    Bars? _ltfBars;
    TimeFrame _ltfTimeFrame;
    bool _ltfTfResolved;
    int _htfSeconds;
    int _ltfSeconds;
    string? _boundSymbolName;
    Action<string>? _warnLog;

    /// <summary>HTF có mapping LTF trên ladder (M5→M2, H4→H1, …).</summary>
    public bool HasLtfMapping => _ltfTfResolved;

    /// <summary>LTF token one step below chart TF (e.g. M15 chart → "5").</summary>
    public string LtfTfToken =>
        _ltfTfResolved ? TimeframeMapping.ChartTfToken(_ltfSeconds) : "?";

    /// <summary>When set, all MarketData.GetBars calls use this symbol only.</summary>
    public void BindSymbol(string? symbolName, Action<string>? warnLog = null)
    {
        _boundSymbolName = string.IsNullOrWhiteSpace(symbolName) ? null : symbolName.Trim();
        _warnLog = warnLog;
    }

    /// <summary>Mapping OK và LTF <see cref="Bars"/> đã bind, có ít nhất 1 nến.</summary>
    public bool IsConfigured => _ltfTfResolved && _ltfBars is { Count: > 0 };

    public void Configure(TimeFrame chartTimeFrame, string chartTfToken)
    {
        _ltfBars = null;
        _ltfTfResolved = false;

        var htfSec = TimeframeMapping.ChartSecondsFromToken(chartTfToken);
        if (htfSec == null)
            return;

        var ltfSec = TimeframeMapping.ResolveLtfSecondsByHtf(htfSec.Value);
        if (ltfSec == null)
            return;

        var ltfTf = SecondsToTimeFrame(ltfSec.Value);
        if (ltfTf is null || ltfTf.Equals(chartTimeFrame))
            return;

        _ltfTimeFrame = ltfTf;
        _htfSeconds = htfSec.Value;
        _ltfSeconds = ltfSec.Value;
        _ltfTfResolved = true;
    }

    public Bars? TryResolveLtfBars(cAlgo.API.Internals.MarketData marketData) =>
        !_ltfTfResolved ? null : ChartSymbolMarketGuard.GetBars(marketData, _ltfTimeFrame, _boundSymbolName, _warnLog);

    public void BindLtfBars(Bars? ltfBars) => _ltfBars = ltfBars;

    /// <summary>Rebind LTF series từ MarketData (gọi khi init null hoặc định kỳ trước collect).</summary>
    public bool TryRebind(cAlgo.API.Internals.MarketData marketData)
    {
        if (!_ltfTfResolved)
            return false;

        var bars = TryResolveLtfBars(marketData);
        if (bars is not { Count: > 0 })
            return false;

        _ltfBars = bars;
        return true;
    }

    /// <summary>Đảm bảo LTF bars sẵn sàng; rebind nếu chưa có hoặc series rỗng.</summary>
    public bool EnsureLtfBars(cAlgo.API.Internals.MarketData marketData)
    {
        if (!_ltfTfResolved)
            return false;
        if (_ltfBars is { Count: > 0 })
            return true;
        return TryRebind(marketData);
    }

    /// <summary>Symbol-specific variant for multi-symbol scanner / backtest.</summary>
    public bool EnsureLtfBarsForSymbol(cAlgo.API.Internals.MarketData marketData, string? symbolName)
    {
        if (!_ltfTfResolved)
            return false;
        if (_ltfBars is { Count: > 0 })
            return true;

        if (!string.IsNullOrWhiteSpace(symbolName))
            BindSymbol(symbolName, _warnLog);

        if (string.IsNullOrWhiteSpace(_boundSymbolName))
            return TryRebind(marketData);

        var bars = ChartSymbolMarketGuard.GetBars(marketData, _ltfTimeFrame, _boundSymbolName, _warnLog);
        if (bars is not { Count: > 0 })
            return false;

        _ltfBars = bars;
        return true;
    }

    /// <summary>Collect + tự rebind/retry một lần nếu slice rỗng.</summary>
    public LtfBarBundle? CollectForClosedHtfBarWithRebind(
        cAlgo.API.Internals.MarketData marketData,
        Bars htfBars,
        int closedHtfBarIndex)
    {
        if (!HasLtfMapping)
            return null;

        EnsureLtfBars(marketData);
        var bundle = CollectForClosedHtfBar(htfBars, closedHtfBarIndex);
        if (bundle != null)
            return bundle;

        if (!TryRebind(marketData))
            return null;

        return CollectForClosedHtfBar(htfBars, closedHtfBarIndex);
    }

    /// <summary>
    /// LTF bars trong HTF đang hình thành, chỉ tới trước <paramref name="clipBeforeExclusive"/>.
    /// </summary>
    public LtfBarBundle? CollectForFormingHtfBarUpTo(
        cAlgo.API.Internals.MarketData marketData,
        Bars htfBars,
        int formingHtfBarIndex,
        DateTime clipBeforeExclusive)
    {
        if (!HasLtfMapping)
            return null;

        EnsureLtfBars(marketData);
        var bundle = CollectForFormingHtfBarUpTo(htfBars, formingHtfBarIndex, clipBeforeExclusive);
        if (bundle != null)
            return bundle;

        if (!TryRebind(marketData))
            return null;

        return CollectForFormingHtfBarUpTo(htfBars, formingHtfBarIndex, clipBeforeExclusive);
    }

    public LtfBarBundle? CollectForFormingHtfBarUpTo(
        Bars htfBars,
        int formingHtfBarIndex,
        DateTime clipBeforeExclusive)
    {
        if (_ltfBars == null || formingHtfBarIndex < 0 || formingHtfBarIndex >= htfBars.Count)
            return null;

        var htfOpen = ChartTimeFromBars.NormalizeBarOpenTime(htfBars.OpenTimes[formingHtfBarIndex]);
        var htfOpenNext = formingHtfBarIndex + 1 < htfBars.Count
            ? ChartTimeFromBars.NormalizeBarOpenTime(htfBars.OpenTimes[formingHtfBarIndex + 1])
            : DateTime.MaxValue;

        if (clipBeforeExclusive <= htfOpen)
            return null;

        var openTimes = new List<DateTime>(_ltfBars.Count);
        var o = new List<double>(_ltfBars.Count);
        var h = new List<double>(_ltfBars.Count);
        var l = new List<double>(_ltfBars.Count);
        var c = new List<double>(_ltfBars.Count);
        for (var i = 0; i < _ltfBars.Count; i++)
        {
            openTimes.Add(_ltfBars.OpenTimes[i]);
            o.Add(_ltfBars.OpenPrices[i]);
            h.Add(_ltfBars.HighPrices[i]);
            l.Add(_ltfBars.LowPrices[i]);
            c.Add(_ltfBars.ClosePrices[i]);
        }

        return LtfBarSliceCollector.Collect(
            htfOpen,
            htfOpenNext,
            openTimes,
            o, h, l, c,
            ChartTimeFromBars.NormalizeBarOpenTime,
            _htfSeconds,
            _ltfSeconds,
            clipBeforeExclusive);
    }

    /// <summary>
    /// LTF bars trong thời gian hình thành HTF vừa đóng.
    /// M5/M2: mở rộng thêm 1 nến M2 (4 nến / M5, overlap OK).
    /// </summary>
    public LtfBarBundle? CollectForClosedHtfBar(Bars htfBars, int closedHtfBarIndex)
    {
        if (_ltfBars == null || closedHtfBarIndex < 0 || closedHtfBarIndex >= htfBars.Count)
            return null;

        var htfOpen = ChartTimeFromBars.NormalizeBarOpenTime(htfBars.OpenTimes[closedHtfBarIndex]);
        var htfOpenNext = closedHtfBarIndex + 1 < htfBars.Count
            ? ChartTimeFromBars.NormalizeBarOpenTime(htfBars.OpenTimes[closedHtfBarIndex + 1])
            : DateTime.MaxValue;

        var start = FindFirstLtfIndexAtOrAfter(_ltfBars, htfOpen);
        if (start >= _ltfBars.Count)
            return null;

        var o = new List<double>();
        var h = new List<double>();
        var l = new List<double>();
        var c = new List<double>();

        if (LtfBarSliceCollector.UsesConsecutiveLtfSlice(_htfSeconds, _ltfSeconds))
        {
            var take = LtfBarSliceCollector.CountLtfBarsForHtf(_htfSeconds, _ltfSeconds);
            for (var n = 0; n < take && start + n < _ltfBars.Count; n++)
            {
                var i = start + n;
                o.Add(_ltfBars.OpenPrices[i]);
                h.Add(_ltfBars.HighPrices[i]);
                l.Add(_ltfBars.LowPrices[i]);
                c.Add(_ltfBars.ClosePrices[i]);
            }
        }
        else
        {
            for (var i = start; i < _ltfBars.Count; i++)
            {
                var t = ChartTimeFromBars.NormalizeBarOpenTime(_ltfBars.OpenTimes[i]);
                if (t < htfOpen)
                    continue;
                if (t >= htfOpenNext)
                    break;

                o.Add(_ltfBars.OpenPrices[i]);
                h.Add(_ltfBars.HighPrices[i]);
                l.Add(_ltfBars.LowPrices[i]);
                c.Add(_ltfBars.ClosePrices[i]);
            }
        }

        return o.Count == 0 ? null : new LtfBarBundle(o, h, l, c);
    }

    static int FindFirstLtfIndexAtOrAfter(Bars ltfBars, DateTime target)
    {
        var lo = 0;
        var hi = ltfBars.Count - 1;
        var result = ltfBars.Count;
        while (lo <= hi)
        {
            var mid = lo + (hi - lo) / 2;
            var t = ChartTimeFromBars.NormalizeBarOpenTime(ltfBars.OpenTimes[mid]);
            if (t >= target)
            {
                result = mid;
                hi = mid - 1;
            }
            else
                lo = mid + 1;
        }
        return result;
    }

    static TimeFrame? SecondsToTimeFrame(int seconds) => seconds switch
    {
        60 => TimeFrame.Minute,
        120 => TimeFrame.Minute2,
        300 => TimeFrame.Minute5,
        900 => TimeFrame.Minute15,
        3600 => TimeFrame.Hour,
        14400 => TimeFrame.Hour4,
        86400 => TimeFrame.Daily,
        _ => (TimeFrame?)null
    };
}
