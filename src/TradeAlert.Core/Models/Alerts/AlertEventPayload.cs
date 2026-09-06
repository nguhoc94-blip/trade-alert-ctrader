using System.Collections.Generic;

namespace TradeAlert.Core.Models.Alerts;

/// <summary>
/// Pivot state for one swing entry — mirrors Pine pivFlag/pivFlagPrev/pivType/pivHighId/pivLowId/
/// pivKeyExtending/pivKeyExtPrev/pivKeyStopBar arrays passed to f_detect_events_raw.
/// barIndex = current source bar index (used for Event C: <c>keyStopBar == barIndex</c> matches Pine <c>keyStopBar_pkl == bar_index</c>).
/// </summary>
public sealed class PivotEntry
{
    /// <summary>flagNow — Pine pivFlag[i] at this bar.</summary>
    public int FlagNow { get; init; }
    /// <summary>flagPrev — Pine pivFlagPrev[i] from previous bar.</summary>
    public int FlagPrev { get; init; }
    /// <summary>typ — Pine pivType[i]: 1=HIGH swing, -1=LOW swing.</summary>
    public int Type { get; init; }
    /// <summary>HighId / LowId as string prefix: "H{id}" or "L{id}".</summary>
    public string SwingIdStr { get; init; } = "";
    /// <summary>pivKeyExtending[i] at this bar.</summary>
    public bool KeyExtending { get; init; }
    /// <summary>pivKeyExtPrev[i] — from previous bar (before update).</summary>
    public bool KeyExtendingPrev { get; init; }
    /// <summary>pivKeyStopBar[i] — bar index when key level stopped extending; -1 if never.</summary>
    public int KeyStopBar { get; init; } = -1;
    /// <summary>True if a key box exists for this pivot (non-null kb).</summary>
    public bool HasKeyBox { get; init; }
    /// <summary>pivMainRole[i]: 1 = MAIN_C impulse role (flag 0).</summary>
    public int MainRole { get; init; }
}

/// <summary>
/// Zone boundaries for f_check_touch_zones / f_collect_active_zones.
/// Each list may be empty (no active zone).
/// int ZoneTouchResult: 0=none, 1=green only, -1=red only, 2=both.
/// </summary>
public sealed class ZoneState
{
    public IReadOnlyList<(double Low, double High)> GreenKeyZones { get; init; } = Array.Empty<(double, double)>();
    public IReadOnlyList<(double Low, double High)> RedKeyZones   { get; init; } = Array.Empty<(double, double)>();
    public IReadOnlyList<(double Low, double High)> GreenObZones  { get; init; } = Array.Empty<(double, double)>();
    public IReadOnlyList<(double Low, double High)> RedObZones    { get; init; } = Array.Empty<(double, double)>();

    /// <summary>Pre-computed touch result for close[0] vs all zones (from CSV zone_touch_result column).
    /// 0=none, 1=green only, -1=red only, 2=both.</summary>
    public int ZoneTouchResult { get; init; }

    /// <summary>Actual bar close price used for touch evaluation.</summary>
    public double BarClose { get; init; }

}

/// <summary>
/// Realtime filter state — mirrors canBuyReal / canSellReal computation.
/// lastPushedSwingType: 1=HIGH pushed, -1=LOW pushed, 0=init.
/// </summary>
public sealed class RealtimeFilterState
{
    public int    LastPushedSwingType { get; init; }
    public double? RealBuyTop   { get; init; }
    public double? RealBuyBot   { get; init; }
    public double? RealSellTop  { get; init; }
    public double? RealSellBot  { get; init; }
    public double? EffBuyBreakBot  { get; init; }
    public double? EffSellBreakTop { get; init; }
    public double  BarHigh { get; init; }
    public double  BarLow  { get; init; }
    /// <summary>ZoneState shared from the same bar (for f_check_touch_zones on active range).</summary>
    public ZoneState? Zones { get; init; }
}
