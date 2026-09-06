using System;
using System.Collections.Generic;
using TradeAlert.Core.Engines.AlertEvaluators;

namespace TradeAlert.Core.Models.Alerts;

/// <summary>Snapshot read-only tại một bước đánh giá — host inject cờ phụ thuộc (M15 edge, detector, zones…).</summary>
public readonly struct AlertEvaluationContext
{
    public string Symbol { get; init; }
    public string ChartTimeframeToken { get; init; }
    public int SourceBarIndex { get; init; }
    /// <summary>Pine <c>bar_index</c> when OnBar ran (defaults to <see cref="SourceBarIndex"/>).</summary>
    public int? PineBarIndex { get; init; }
    public DateTime SourceBarOpenTimeChartLocal { get; init; }
    public double SourceBarHigh { get; init; }
    public double SourceBarLow { get; init; }
    public bool IsCurrentBar { get; init; }
    public bool IsBarClosed { get; init; }
    public bool IsRealtime { get; init; }
    public int? EvaluationOffset { get; init; }
    public AlertStateSnapshotRef? StateSnapshot { get; init; }

    /// <summary>Host inject — Pine <c>ta.change(time("15"))</c> edge; không tự giả lập trong Core.</summary>
    public bool M15CloseEdgeInjected { get; init; }

    public bool EventRawDetectionAvailable { get; init; }
    public bool ActiveZoneCollectionAvailable { get; init; }
    public bool RealtimeFilterStateAvailable { get; init; }

    // ---- Loop 4 payload injected from DATA CHART / host ----

    /// <summary>Pivot entries for f_detect_events_raw — populated when EventRawDetectionAvailable=true.</summary>
    public IReadOnlyList<PivotEntry>? PivotEntries { get; init; }

    /// <summary>Per-session dedup sets for EVENT A/B/C (buy/sell) — shared across bars, mutated in-place.</summary>
    public EventDedupSets? EventDedup { get; init; }

    /// <summary>true = M5 bar just closed; false = not M5-TF or no close.</summary>
    public bool IsM5JustClosed { get; init; }

    /// <summary>true = M15 bar just closed.</summary>
    public bool IsM15JustClosed { get; init; }

    /// <summary>Zone state for TOUCH / REAL evaluation — populated when ActiveZoneCollectionAvailable=true.</summary>
    public ZoneState? ZoneState { get; init; }

    /// <summary>
    /// Backtest HTF (H1/H4/…): giá probe LTF (M5 open/high/low) dùng cho Touch thay HTF close.
    /// Host inject — xem <see cref="TouchRealEvaluators"/>.
    /// </summary>
    public double? TouchLtfOpenPrice { get; init; }

    /// <summary>Debug label for <see cref="TouchLtfOpenPrice"/> — e.g. ltfOpenM5, ltfHighM5, ltfLowM5.</summary>
    public string? TouchLtfProbeKind { get; init; }

    /// <summary>Realtime filter state for CAN BUY/SELL REAL — populated when RealtimeFilterStateAvailable=true.</summary>
    public RealtimeFilterState? RealtimeFilter { get; init; }

    /// <summary>Pine <c>debugAlertTracking</c> — per-pivot FIRE/SKIP lines to log.</summary>
    public bool AlertTrackingDebug { get; init; }

    /// <summary>Session cache on shell — avoids duplicate raw detection per bar.</summary>
    public EventDetectionSessionCache? EventDetectionCache { get; init; }

    /// <summary>Log Real band vs zone overlap/buffer detail in canBuyReal/canSellReal reason text + host Print.</summary>
    public bool RealZoneDebug { get; init; }
}
