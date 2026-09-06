using TradeAlert.Core.Engines;
using TradeAlert.Core.Engines.AlertEvaluators;
using TradeAlert.Core.Models.Alerts;

namespace TradeAlert.Indicator;

/// <summary>Builds <see cref="AlertEvaluationContext"/> from live <see cref="PineStateEngine"/> state (shared by indicator, cBot, replay harness).</summary>
public static class Loop6EvaluationContextFactory
{
    public static AlertEvaluationContext Build(
        TradeAlertIndicator shell,
        int barIndex,
        string chartTimeframeToken,
        DateTime barOpenTimeChartLocal,
        double barClose,
        double barHigh,
        double barLow,
        bool isBarClosed,
        bool isRealtime,
        bool isNewBarOnRealtimeForming = false,
        bool m15HostInjected = false,
        double? touchLtfOpenPrice = null,
        string? touchLtfProbeKind = null,
        bool alertTrackingDebug = false,
        EventDetectionSessionCache? eventDetectionCache = null,
        int? pineBarIndex = null,
        bool realZoneDebug = false)
    {
        var zoneState = shell.State.BuildZoneState(barClose);
        var realtimeFilter = shell.State.BuildRealtimeFilterState(barHigh, barLow, zoneState);
        var pivotEntries = shell.State.BuildPivotEntries(barIndex);

        var tfTok = NormalizeChartTimeframeToken(chartTimeframeToken);
        var minute = barOpenTimeChartLocal.Minute;
        var isM5JustClosed = AlertBarTiming.IsM5JustClosedGate(tfTok, isBarClosed, isNewBarOnRealtimeForming);
        var isM15JustClosed = AlertBarTiming.IsM15JustClosedGate(
            tfTok, isBarClosed, isNewBarOnRealtimeForming, minute);
        var m15Edge = AlertBarTiming.ComputeM15CloseEdge(
            tfTok,
            isBarClosed,
            isRealtime,
            isNewBarOnRealtimeForming,
            minute,
            m15HostInjected || shell.M15Edge.M15CloseEdgeInjected);

        return new AlertEvaluationContext
        {
            Symbol = shell.HostContext.Symbol,
            ChartTimeframeToken = tfTok,
            SourceBarIndex = barIndex,
            PineBarIndex = pineBarIndex,
            SourceBarOpenTimeChartLocal = barOpenTimeChartLocal,
            SourceBarHigh = barHigh,
            SourceBarLow = barLow,
            IsCurrentBar = !isBarClosed,
            IsBarClosed = isBarClosed,
            IsRealtime = isRealtime,
            EvaluationOffset = isRealtime ? 0 : 1,
            M15CloseEdgeInjected = m15Edge,

            EventRawDetectionAvailable = true,
            ActiveZoneCollectionAvailable = true,
            RealtimeFilterStateAvailable = true,

            PivotEntries = pivotEntries,
            EventDedup = shell.EventDedup,
            IsM5JustClosed = isM5JustClosed,
            IsM15JustClosed = isM15JustClosed,
            ZoneState = zoneState,
            RealtimeFilter = realtimeFilter,
            TouchLtfOpenPrice = touchLtfOpenPrice,
            TouchLtfProbeKind = touchLtfProbeKind,
            AlertTrackingDebug = alertTrackingDebug,
            EventDetectionCache = eventDetectionCache,
            RealZoneDebug = realZoneDebug,
        };
    }

    static string NormalizeChartTimeframeToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return token;
        return token.StartsWith("M", StringComparison.OrdinalIgnoreCase) && token.Length > 1
            ? token[1..]
            : token;
    }
}
