using System;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.KeyLevel;
using TradeAlert.Core.Series;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>How swing B was selected: ACTIVE, D_SWING, MAIN_C fallback (NG M15), or M5 zone anchored to M15 (R1/R2).</summary>
public enum SwingBSource
{
    Active = 0,
    MainCFallback = 1,
    M5AnchoredFallback = 2,
    DSwing = 3,
}

/// <summary>Resolved swing B (most recent ACTIVE/D_SWING, or MAIN_C fallback when enabled).</summary>
public sealed class SwingBResult
{
    public int PivotIndex { get; init; }
    public int PivotBar { get; init; }
    /// <summary>1 = HIGH swing, -1 = LOW swing.</summary>
    public int Type { get; init; }
    public double KeyTop { get; init; }
    public double KeyBottom { get; init; }
    /// <summary>Whether this B came from ACTIVE, D_SWING, MAIN_C fallback, or M5 anchor.</summary>
    public SwingBSource Source { get; init; } = SwingBSource.Active;
    /// <summary>M5→M15 anchor picked a MAIN_C pivot on M5 (R1/R2 fallback only).</summary>
    public bool M5AnchoredMainC { get; init; }
    /// <summary>M5→M15 anchor picked a D_SWING pivot on M5 (R1/R2 fallback only).</summary>
    public bool M5AnchoredDSwing { get; init; }
    /// <summary>Native structure-TF bar (M5 when anchored). Same as <see cref="PivotBar"/> on chart TF.</summary>
    public int StructurePivotBar { get; init; }

    public bool IsHigh => Type == 1;
    public bool IsLow => Type == -1;
    public double Width => Math.Abs(KeyTop - KeyBottom);
}

/// <summary>
/// Rule 1 — finds the most recent ACTIVE or D_SWING pivot (B) on the chart-TF engine and gates it
/// against the trade direction: BUY requires B = swing LOW, SELL requires B = swing HIGH.
/// B must own a keylevel box (needed for SL/entry geometry).
/// <para>
/// When <c>allowMainCFallback</c> is true (NG M15 — R5/R6), if no valid ACTIVE/D_SWING of the required
/// direction exists, the resolver makes a second pass and accepts the most recent MAIN_C pivot of that
/// direction instead. This is needed because NG events fire right when the structure flips and the live
/// swing of that side often no longer exists at the fire bar.
/// </para>
/// </summary>
public static class SwingBResolver
{
    public const int FlagActive = 1;
    /// <summary>MAIN_C state flag: pivot promoted from ACTIVE to MAIN_C by <see cref="Core.Engines.MainPromotionEngine"/>.</summary>
    public const int FlagMainC = 0;
    /// <summary>D_SWING state flag — promoted D leg with ACTIVE-style keylevel (Pine pivFlag=4).</summary>
    public const int FlagDSwing = 4;
    public const int TypeHigh = 1;
    public const int TypeLow = -1;

    public static SwingBResult? Resolve(PineStateEngine state, bool isBuy, out string reason) =>
        Resolve(state, isBuy, allowMainCFallback: false, out reason);

    public static SwingBResult? Resolve(
        PineStateEngine state,
        bool isBuy,
        bool allowMainCFallback,
        out string reason)
    {
        var pivots = state.Pivots;
        var requiredType = isBuy ? TypeLow : TypeHigh;

        var liveResult = ScanForLiveSwing(pivots, requiredType, out var liveReason);
        if (liveResult is not null)
        {
            reason = liveReason;
            return liveResult;
        }

        if (!allowMainCFallback)
        {
            reason = liveReason;
            return null;
        }

        var mainCResult = ScanForB(
            pivots, requiredType,
            flagPredicate: flag => flag == FlagMainC,
            source: SwingBSource.MainCFallback,
            out var mainCReason,
            requireMainRole: true,
            requireKeyExtending: true);
        if (mainCResult is not null)
        {
            reason = $"ok (MAIN_C fallback @bar={mainCResult.PivotBar})";
            return mainCResult;
        }

        reason = $"{liveReason}; no MAIN_C fallback ({mainCReason})";
        return null;
    }

    /// <summary>
    /// R1/R2 on M15: when chart-TF ACTIVE/D_SWING B is missing, resolve B on M5 (ACTIVE/D_SWING, then MAIN_C fallback)
    /// and anchor its price zone to the M15 bar that contains the M5 pivot open time.
    /// </summary>
    public static SwingBResult? TryResolveM5AnchoredFallback(
        PineStateEngine m5State,
        bool isBuy,
        SeriesBuffer? m5Buffer,
        SeriesBuffer? chartBuffer,
        string chartTfToken,
        out string reason)
    {
        var m5B = Resolve(m5State, isBuy, allowMainCFallback: true, out var m5Reason);
        if (m5B is null)
        {
            reason = $"no M15 B; M5 fallback failed ({m5Reason})";
            return null;
        }

        if (!TryAnchorM5PivotBarToChart(m5B.PivotBar, m5Buffer, chartBuffer, chartTfToken, out var chartBar))
        {
            reason = $"no M15 B; M5 B@{m5B.PivotBar} could not anchor to {chartTfToken}";
            return null;
        }

        var kindTag = m5B.Source switch
        {
            SwingBSource.MainCFallback => " MAIN_C",
            SwingBSource.DSwing => " D_SWING",
            _ => "",
        };
        reason = $"ok (M5{kindTag} B@{m5B.PivotBar} anchored to {chartTfToken} bar={chartBar})";
        return new SwingBResult
        {
            PivotIndex = m5B.PivotIndex,
            PivotBar = chartBar,
            StructurePivotBar = m5B.PivotBar,
            Type = m5B.Type,
            KeyTop = m5B.KeyTop,
            KeyBottom = m5B.KeyBottom,
            Source = SwingBSource.M5AnchoredFallback,
            M5AnchoredMainC = m5B.Source == SwingBSource.MainCFallback,
            M5AnchoredDSwing = m5B.Source == SwingBSource.DSwing,
        };
    }

    /// <summary>Find chart-TF bar index whose open-time window contains the M5 pivot bar open time.</summary>
    public static bool TryAnchorM5PivotBarToChart(
        int m5PivotBar,
        SeriesBuffer? m5Buffer,
        SeriesBuffer? chartBuffer,
        string chartTfToken,
        out int chartBar)
    {
        chartBar = -1;
        if (m5Buffer is null || chartBuffer is null)
            return false;

        var m5Offset = m5Buffer.CurrentEvaluationBarIndex - m5PivotBar;
        if (m5Offset < 0 || !m5Buffer.TryGetSnapshotAtOffset(m5Offset, out var m5Snap))
            return false;

        var pivotTime = m5Snap.OpenChartTimeLocal;
        var chartPeriod = TfPeriodTokens.Parse(chartTfToken);
        if (chartPeriod <= TimeSpan.Zero)
            return false;

        var current = chartBuffer.CurrentEvaluationBarIndex;
        for (var offset = 0; offset <= 5000; offset++)
        {
            if (!chartBuffer.TryGetSnapshotAtOffset(offset, out var chartSnap))
                break;

            var barIdx = current - offset;
            var start = chartSnap.OpenChartTimeLocal;
            var end = start + chartPeriod;
            if (start <= pivotTime && pivotTime < end)
            {
                chartBar = barIdx;
                return true;
            }

            if (end <= pivotTime)
                break;
        }

        return false;
    }

    /// <summary>Latest M5 bar index whose open time falls inside the chart-TF bar window.</summary>
    public static bool TryFindLatestM5BarInChartBar(
        int chartBar,
        SeriesBuffer chartBuffer,
        SeriesBuffer m5Buffer,
        string chartTfToken,
        out int m5Bar)
    {
        m5Bar = -1;
        if (!TryResolvePivotOpenTime(chartBuffer, chartBar, out var chartStart))
            return false;

        var chartPeriod = TfPeriodTokens.Parse(chartTfToken);
        if (chartPeriod <= TimeSpan.Zero)
            return false;

        var chartEnd = chartStart + chartPeriod;
        var current = m5Buffer.CurrentEvaluationBarIndex;
        var found = false;

        for (var offset = 0; offset <= 5000; offset++)
        {
            if (!m5Buffer.TryGetSnapshotAtOffset(offset, out var m5Snap))
                break;

            var barIdx = current - offset;
            var m5Start = m5Snap.OpenChartTimeLocal;
            if (m5Start < chartStart)
                break;
            if (m5Start >= chartStart && m5Start < chartEnd)
            {
                m5Bar = barIdx;
                found = true;
            }
        }

        return found;
    }

    static bool TryResolvePivotOpenTime(SeriesBuffer buffer, int pivotBar, out DateTime openTime)
    {
        openTime = default;
        var offset = buffer.CurrentEvaluationBarIndex - pivotBar;
        if (offset < 0 || !buffer.TryGetSnapshotAtOffset(offset, out var snap))
            return false;
        openTime = snap.OpenChartTimeLocal;
        return true;
    }

    /// <summary>0-based slots R1 and R2 (M5 compound rules on M15 chart).</summary>
    public static bool IsR1R2Slot(int slotIndex) => slotIndex == 0 || slotIndex == 1;

    /// <summary>Most recent pivot with flag ACTIVE or D_SWING that matches direction and has a keylevel.</summary>
    static SwingBResult? ScanForLiveSwing(PivotStateStore pivots, int requiredType, out string reason)
    {
        bool sawAny = false;
        string? lastReason = null;

        for (var i = pivots.Count - 1; i >= 0; i--)
        {
            var flag = pivots.GetFlag(i);
            if (flag != FlagActive && flag != FlagDSwing)
                continue;

            sawAny = true;
            var source = flag == FlagDSwing ? SwingBSource.DSwing : SwingBSource.Active;
            var result = BuildBOrReason(pivots, i, requiredType, source, requireKeyExtending: false, out var r);
            if (result is not null)
            {
                reason = r;
                return result;
            }

            lastReason ??= r;
        }

        reason = sawAny ? (lastReason ?? "no candidate") : "no active swing";
        return null;
    }

    static SwingBResult? ScanForB(
        PivotStateStore pivots,
        int requiredType,
        Func<int, bool> flagPredicate,
        SwingBSource source,
        out string reason,
        bool requireMainRole,
        bool requireKeyExtending)
    {
        bool sawAnyFlagMatch = false;
        string? lastReason = null;

        for (var i = pivots.Count - 1; i >= 0; i--)
        {
            if (!flagPredicate(pivots.GetFlag(i)))
                continue;
            if (requireMainRole && pivots.GetMainRole(i) != 1)
                continue;

            sawAnyFlagMatch = true;
            var result = BuildBOrReason(pivots, i, requiredType, source, requireKeyExtending, out var r);
            if (result is not null)
            {
                reason = r;
                return result;
            }
            lastReason ??= r;
        }

        reason = sawAnyFlagMatch ? (lastReason ?? "no candidate") : "no MAIN_C";
        return null;
    }

    static SwingBResult? BuildBOrReason(
        PivotStateStore pivots,
        int idx,
        int requiredType,
        SwingBSource source,
        bool requireKeyExtending,
        out string reason)
    {
        var snap = pivots.GetSnapshot(idx);

        if (snap.Type != requiredType)
        {
            reason = requiredType == TypeLow
                ? $"B@{snap.BarIndex} not swing LOW (type={snap.Type})"
                : $"B@{snap.BarIndex} not swing HIGH (type={snap.Type})";
            return null;
        }

        if (!pivots.GetHasKey(idx))
        {
            reason = $"B@{snap.BarIndex} has no keylevel";
            return null;
        }

        var spec = pivots.GetKeyBox(idx)?.Spec;
        if (spec is null)
        {
            reason = $"B@{snap.BarIndex} keybox spec null";
            return null;
        }

        if (requireKeyExtending && !pivots.GetKeyExtending(idx))
        {
            reason = $"B@{snap.BarIndex} keylevel not extending";
            return null;
        }

        var top = Math.Max(spec.Top, spec.Bottom);
        var bottom = Math.Min(spec.Top, spec.Bottom);
        if (top - bottom <= 0)
        {
            reason = $"B@{snap.BarIndex} degenerate keylevel width";
            return null;
        }

        reason = "ok";
        return new SwingBResult
        {
            PivotIndex = idx,
            PivotBar = snap.BarIndex,
            StructurePivotBar = snap.BarIndex,
            Type = snap.Type,
            KeyTop = top,
            KeyBottom = bottom,
            Source = source,
        };
    }
}
