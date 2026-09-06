using TradeAlert.Core.Engines;
using TradeAlert.Core.Engines.AlertEvaluators;
using TradeAlert.Core.Models.Alerts;

namespace TradeAlert.Indicator;

/// <summary>
/// Shell Loop 6 — lõi chỉ báo + pipeline cảnh báo; có thể dùng trong test hoặc từ <see cref="Host.TradeAlertLoop6Host"/>.
/// </summary>
public sealed class TradeAlertIndicator
{
    public TradeAlertIndicator(IndicatorHostContext hostContext, IAlertFireSink? alertSink = null)
    {
        HostContext = hostContext;
        if (alertSink != null)
            AlertSink = alertSink;
        else
            AlertSink = new ListAlertFireSink();

        Alerts = new AlertPipelineHost(fireSink: AlertSink);
        State  = new PineStateEngine();
        EventDedup = new EventDedupSets();
        EventDetectionCache = new EventDetectionSessionCache();
    }

    public IndicatorHostContext HostContext { get; }

    public IAlertFireSink AlertSink { get; }

    public SeriesBufferSync Series { get; } = new();

    public MinimalRenderSink Render { get; } = new();

    public AlertPipelineHost Alerts { get; }

    public M15EdgeHostSignal M15Edge { get; } = new();

    /// <summary>Pine state aggregator — pivots + ob pool + real zones, updated mỗi bar.</summary>
    public PineStateEngine State { get; }

    /// <summary>Per-session deduplication sets cho EVENT family (shared cross-bars).</summary>
    public EventDedupSets EventDedup { get; private set; }

    /// <summary>Per-bar cache for EVENT raw detection + alert tracking debug lines.</summary>
    public EventDetectionSessionCache EventDetectionCache { get; }

    public void OnStopOrReload()
    {
        Series.ResetForSession();
        Alerts.ResetSession();
        M15Edge.SetHostM15CloseEdgeForCurrentEvaluation(false);
        Render.ClearCommands();
        State.ResetForSession();
        EventDedup = new EventDedupSets();
        EventDetectionCache.Reset();
    }
}
