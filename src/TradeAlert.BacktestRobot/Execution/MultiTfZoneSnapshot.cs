using System;
using System.Collections.Generic;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.KeyLevel;
using TradeAlert.Core.Series;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>One zone candidate from synchronized shell state.</summary>
public sealed class ZoneCandidate
{
    public string TfToken { get; init; } = "";
    public ZoneSourceKind Source { get; init; }
    public ZoneEffectiveColor EffectiveColor { get; init; }
    public BrokenSwingType? BrokenSwing { get; init; }
    public int? PivotIndex { get; init; }
    public int? PivotBar { get; init; }
    public double Low { get; init; }
    public double High { get; init; }
    /// <summary>Open time of the bar that holds this pivot (chart-local, Unspecified). Null when Series buffer
    /// has no snapshot for that bar (e.g. some unit-test scaffolds) — cross-TF identity check then degrades to
    /// the legacy bar_index match only.</summary>
    public DateTime? PivotOpenTime { get; init; }
    /// <summary>Duration of one bar on this zone's TF. <see cref="TimeSpan.Zero"/> when TF token is unknown.</summary>
    public TimeSpan TfPeriod { get; init; }
}

/// <summary>
/// Collect KeyLevel / BrokenKeyLevel / OB zones from a PineStateEngine shell.
/// Active KL mirrors ZoneCollector active branch; broken KL uses swing type at keylevel.
/// </summary>
public static class MultiTfZoneSnapshot
{
    static readonly HashSet<int> NonBrokenFlags = new() { 1, 3, 4, 7, 5, 50 };
    static readonly HashSet<int> BrokenFlags = new() { -3, 2, -2, 6 };

    /// <summary>Collect zone candidates. Pass <paramref name="buffer"/> when bar-open-time lookup is needed
    /// (cross-TF identity in <see cref="PostBPushObstacleCancelRule"/>); pass <c>null</c> for callers that only
    /// need price geometry (<see cref="EntrySlZoneGate"/>, unit tests).</summary>
    public static IReadOnlyList<ZoneCandidate> Collect(
        PineStateEngine state,
        string tfToken,
        in EntrySlZoneGateConfig cfg,
        SeriesBuffer? buffer = null)
    {
        var list = new List<ZoneCandidate>();
        var pivots = state.Pivots;
        var obPool = state.ObPool;
        var tfPeriod = TfPeriodTokens.Parse(tfToken);

        if (cfg.IncludeKeyLevels || cfg.IncludeBrokenKeyLevels)
        {
            for (var i = 0; i < pivots.Count; i++)
            {
                if (!pivots.GetHasKey(i))
                    continue;

                var kb = pivots.GetKeyBox(i);
                if (kb is null)
                    continue;

                if (!pivots.GetKeyExtending(i))
                    continue;

                var typ = pivots.GetTypeAt(i);
                var flag = pivots.GetFlag(i);
                var mainRole = pivots.GetMainRole(i);
                var pivotBar = pivots.GetSnapshot(i).BarIndex;
                var pivotOpenTime = ResolveOpenTime(buffer, pivotBar);
                var top = kb.Spec.Top;
                var bot = kb.Spec.Bottom;
                var lo = System.Math.Min(top, bot);
                var hi = System.Math.Max(top, bot);

                var isBroken = BrokenFlags.Contains(flag);

                if (isBroken)
                {
                    if (!cfg.IncludeBrokenKeyLevels)
                        continue;

                    // Broken keylevel colour mirrors Pine ZoneCollector: after breaking, LOW→Red (resistance), HIGH→Green (support).
                    if (typ == -1)
                    {
                        list.Add(new ZoneCandidate
                        {
                            TfToken = tfToken,
                            Source = ZoneSourceKind.BrokenKeyLevel,
                            EffectiveColor = ZoneEffectiveColor.Red,
                            BrokenSwing = BrokenSwingType.SwingLow,
                            PivotIndex = i,
                            PivotBar = pivotBar,
                            PivotOpenTime = pivotOpenTime,
                            TfPeriod = tfPeriod,
                            Low = lo,
                            High = hi,
                        });
                    }
                    else if (typ == 1)
                    {
                        list.Add(new ZoneCandidate
                        {
                            TfToken = tfToken,
                            Source = ZoneSourceKind.BrokenKeyLevel,
                            EffectiveColor = ZoneEffectiveColor.Green,
                            BrokenSwing = BrokenSwingType.SwingHigh,
                            PivotIndex = i,
                            PivotBar = pivotBar,
                            PivotOpenTime = pivotOpenTime,
                            TfPeriod = tfPeriod,
                            Low = lo,
                            High = hi,
                        });
                    }

                    continue;
                }

                if (!cfg.IncludeKeyLevels)
                    continue;

                var nonBrokenMatch = NonBrokenFlags.Contains(flag)
                                     || (flag == 0 && mainRole == 1);

                if (!nonBrokenMatch)
                    continue;

                if (typ == -1)
                {
                    list.Add(new ZoneCandidate
                    {
                        TfToken = tfToken,
                        Source = ZoneSourceKind.KeyLevel,
                        EffectiveColor = ZoneEffectiveColor.Green,
                        PivotIndex = i,
                        PivotBar = pivotBar,
                        PivotOpenTime = pivotOpenTime,
                        TfPeriod = tfPeriod,
                        Low = lo,
                        High = hi,
                    });
                }
                else if (typ == 1)
                {
                    list.Add(new ZoneCandidate
                    {
                        TfToken = tfToken,
                        Source = ZoneSourceKind.KeyLevel,
                        EffectiveColor = ZoneEffectiveColor.Red,
                        PivotIndex = i,
                        PivotBar = pivotBar,
                        PivotOpenTime = pivotOpenTime,
                        TfPeriod = tfPeriod,
                        Low = lo,
                        High = hi,
                    });
                }
            }
        }

        if (cfg.IncludeOrderBlocks)
        {
            for (var i = 0; i < obPool.Count; i++)
            {
                var r = obPool.GetRecord(i);
                if (r.State != 2)
                    continue;
                if (!r.Extending)
                    continue;
                if (r.Box is null)
                    continue;

                var top = r.Box.Top;
                var bot = r.Box.Bottom;
                var lo = System.Math.Min(top, bot);
                var hi = System.Math.Max(top, bot);

                if (r.Type == -1)
                {
                    list.Add(new ZoneCandidate
                    {
                        TfToken = tfToken,
                        Source = ZoneSourceKind.OrderBlock,
                        EffectiveColor = ZoneEffectiveColor.Green,
                        TfPeriod = tfPeriod,
                        Low = lo,
                        High = hi,
                    });
                }
                else if (r.Type == 1)
                {
                    list.Add(new ZoneCandidate
                    {
                        TfToken = tfToken,
                        Source = ZoneSourceKind.OrderBlock,
                        EffectiveColor = ZoneEffectiveColor.Red,
                        TfPeriod = tfPeriod,
                        Low = lo,
                        High = hi,
                    });
                }
            }
        }

        return list;
    }

    /// <summary>Resolve bar open-time via <see cref="SeriesBuffer.TryGetSnapshotAtOffset"/>; returns
    /// <c>null</c> when buffer is unavailable or the bar is not in buffer (e.g. unit-test scaffolds with no
    /// Series feed).</summary>
    static DateTime? ResolveOpenTime(SeriesBuffer? buffer, int pivotBar)
    {
        if (buffer is null) return null;
        var current = buffer.CurrentEvaluationBarIndex;
        var offset = current - pivotBar;
        if (offset < 0) return null;
        return buffer.TryGetSnapshotAtOffset(offset, out var snap)
            ? snap.OpenChartTimeLocal
            : (DateTime?)null;
    }
}

/// <summary>TF token → bar duration. Unknown tokens map to <see cref="TimeSpan.Zero"/>.</summary>
public static class TfPeriodTokens
{
    public static TimeSpan Parse(string tfToken) => tfToken switch
    {
        "1"   => TimeSpan.FromMinutes(1),
        "5"   => TimeSpan.FromMinutes(5),
        "15"  => TimeSpan.FromMinutes(15),
        "30"  => TimeSpan.FromMinutes(30),
        "60"  => TimeSpan.FromMinutes(60),
        "240" => TimeSpan.FromHours(4),
        "1440" => TimeSpan.FromDays(1),
        "D"   => TimeSpan.FromDays(1),
        "1D"  => TimeSpan.FromDays(1),
        _     => TimeSpan.Zero,
    };
}
