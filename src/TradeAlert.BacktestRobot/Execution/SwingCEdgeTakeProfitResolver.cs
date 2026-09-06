using System;
using System.Collections.Generic;
using System.Globalization;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.KeyLevel;
using TradeAlert.Core.Series;
// SwingCTpMode is in the same namespace (TradeAlert.BacktestRobot.Execution)

namespace TradeAlert.BacktestRobot.Execution;

public sealed class SwingCEdgeResult
{
    public int PivotIndex { get; init; }
    public int PivotBar { get; init; }
    public int PivotType { get; init; }
    public double TakeProfit { get; init; }
    public double EdgeTop { get; init; }
    public double EdgeBottom { get; init; }
    public string EdgeKind { get; init; } = "";
    /// <summary>True when the edge zone came from an OrderBlock (used by near-D buffer formula).</summary>
    public bool IsOb { get; init; }
    /// <summary>TF token where the zone was resolved (e.g. "15", "60", "240", "1D"). Empty when N/A.</summary>
    public string TfToken { get; init; } = "";
}

/// <summary>
/// Resolves initial TP from the lower/upper edge of confirmed swing C after setup swing B.
/// Valid swing C always carries a KeyBox on M15. TP C is the representative edge nearer to entry among:
/// KeyBottom/Top (KLV C), OB ZIN on the M15 C pivot, and OB ZIN on the corresponding M5 swing C pivot.
/// Zone geometry (EdgeTop/EdgeBottom) stays on the KeyBox span for gates and logging.
///
/// Mode <see cref="SwingCTpMode.NextKeyLevelAfterC"/>:
///   Instead of C, target the next KeyLevel (H1/H4/M15 — pivots with KeyBox, no M5 OB)
///   that clears C in the trade direction. Falls back to C if no qualifying D is found.
/// </summary>
public sealed class SwingCEdgeCrossTfContext
{
    public PineStateEngine M5State { get; init; } = null!;
    public SeriesBuffer? M5Buffer { get; init; }
    public SeriesBuffer? ChartBuffer { get; init; }
    public string ChartTfToken { get; init; } = "15";

    public static SwingCEdgeCrossTfContext? FromMapperConfig(in TradeMapperConfig cfg)
    {
        if (!string.Equals(cfg.ChartTfToken, "15", StringComparison.Ordinal)
            || cfg.M5FallbackState is null)
            return null;

        return new SwingCEdgeCrossTfContext
        {
            M5State = cfg.M5FallbackState,
            M5Buffer = cfg.M5FallbackBuffer,
            ChartBuffer = cfg.ChartSeriesBuffer,
            ChartTfToken = cfg.ChartTfToken,
        };
    }
}

public static class SwingCEdgeTakeProfitResolver
{
    public static SwingCEdgeResult? TryResolve(
        PineStateEngine state,
        bool isBuy,
        int swingBBar,
        double entry,
        out string reason,
        SwingCTpMode mode = SwingCTpMode.UpdateOnCConfirm,
        SwingCEdgeCrossTfContext? crossTf = null)
    {
        var pivots = state.Pivots;
        var wantType = isBuy ? SwingBResolver.TypeHigh : SwingBResolver.TypeLow;
        var cIdx = FindNearestConfirmedPivotAfter(pivots, swingBBar, wantType);
        if (cIdx < 0)
        {
            reason = $"no confirmed swing C after B@{swingBBar}";
            return null;
        }

        var cSnap  = pivots.GetSnapshot(cIdx);
        var cEdges = ResolveCEdges(state, pivots, cIdx, cSnap.Type, cSnap.Price, entry, isBuy, crossTf);

        // ── NextKeyLevelAfterC mode: try to target D (next H1/H4/M15 KeyLevel beyond C) ──
        if (mode == SwingCTpMode.NextKeyLevelAfterC)
        {
            var dResult = TryFindDKeyLevel(state, pivots, cSnap.BarIndex, cSnap.Price, isBuy, entry);
            if (dResult is not null)
            {
                reason = "ok(D)";
                return dResult;
            }
            // Fallback: D not found → use C as usual (log suffix signals fallback)
            reason = "ok(C-fallback:no-D)";
        }
        else if (mode != SwingCTpMode.SplitTpAtCAndD)
        {
            reason = "ok";
        }
        else
        {
            reason = "ok(C)";
        }

        var tp = isBuy ? cEdges.LowerEdge : cEdges.UpperEdge;
        var sideOk = isBuy ? tp > entry : tp < entry;
        if (!sideOk)
        {
            reason = isBuy
                ? $"C TP {tp:0.#####} not above entry {entry:0.#####}"
                : $"C TP {tp:0.#####} not below entry {entry:0.#####}";
            return null;
        }

        return new SwingCEdgeResult
        {
            PivotIndex = cIdx,
            PivotBar   = cSnap.BarIndex,
            PivotType  = cSnap.Type,
            TakeProfit = tp,
            EdgeTop    = cEdges.EdgeTop,
            EdgeBottom = cEdges.EdgeBottom,
            EdgeKind   = cEdges.EdgeKind,
            IsOb       = cEdges.TpFromOb,
        };
    }

    /// <summary>
    /// Resolves swing C and optional D for split-TP mode (two legs at plan time).
    /// D is resolved from <paramref name="dReferencePrice"/> (fire-bar HIGH for BUY / LOW for SELL)
    /// when supplied, or from the confirmed C pivot price when <paramref name="dReferencePrice"/> is NaN.
    /// Pass <paramref name="cachedDFire"/> to reuse D locked at fire time (defer-await-C path).
    /// </summary>
    public static bool TryResolveSplitCd(
        PineStateEngine state,
        bool isBuy,
        int swingBBar,
        double entry,
        out SwingCEdgeResult cResult,
        out SwingCEdgeResult? dResult,
        out string reason,
        IReadOnlyDictionary<string, PineStateEngine>? dZoneStates = null,
        SwingCEdgeCrossTfContext? crossTf = null,
        double dReferencePrice = double.NaN,
        SwingCEdgeResult? cachedDFire = null)
    {
        cResult = null!;
        dResult = null;

        var c = TryResolve(state, isBuy, swingBBar, entry, out reason, SwingCTpMode.SplitTpAtCAndD, crossTf);
        if (c is null)
            return false;

        cResult = c;
        var cSnap = state.Pivots.GetSnapshot(c.PivotIndex);

        if (cachedDFire is not null)
        {
            dResult = cachedDFire;
        }
        else
        {
            var refPrice = double.IsNaN(dReferencePrice) ? cSnap.Price : dReferencePrice;
            dResult = ResolveDFireZone(dZoneStates, state, cSnap.BarIndex, refPrice, isBuy, entry);
        }

        reason = dResult is not null ? "ok(split)" : "ok(split,no-D)";
        return true;
    }

    /// <summary>
    /// Resolve D zone at fire bar: reference = HIGH (BUY) or LOW (SELL) of the fire candle.
    /// </summary>
    public static SwingCEdgeResult? ResolveDFireZone(
        IReadOnlyDictionary<string, PineStateEngine>? dZoneStates,
        PineStateEngine? pivotState,
        int searchAfterBar,
        double referencePrice,
        bool isBuy,
        double entry)
    {
        if (dZoneStates is { Count: > 0 })
            return TryFindDZone(dZoneStates, referencePrice, isBuy, entry);

        if (pivotState is not null)
            return TryFindDKeyLevel(pivotState, pivotState.Pivots, searchAfterBar, referencePrice, isBuy, entry);

        return null;
    }

    /// <summary>
    /// Gồng lời: resolve D' for TP leg D on R3–R6 — scans M15/H1/H4 only (no M5).
    /// Same geometry rules as <see cref="ResolveDFireZone"/>; falls back to pivot-after-C KeyLevel when no zone states.
    /// Does not affect near-D cancel (caller keeps using D from full TF scan).
    /// </summary>
    public static SwingCEdgeResult? ResolveDPrimeFireZone(
        IReadOnlyDictionary<string, PineStateEngine>? dPrimeZoneStates,
        PineStateEngine? pivotState,
        int searchAfterBar,
        double referencePrice,
        bool isBuy,
        double entry)
    {
        if (dPrimeZoneStates is { Count: > 0 })
        {
            var found = TryFindDZone(dPrimeZoneStates, referencePrice, isBuy, entry);
            if (found is not null)
                return CopyWithEdgeKind(found, "DPrimeZone");
        }

        if (pivotState is not null)
        {
            var kb = TryFindDKeyLevel(pivotState, pivotState.Pivots, searchAfterBar, referencePrice, isBuy, entry);
            if (kb is not null)
                return CopyWithEdgeKind(kb, "DPrimeKeyBox");
        }

        return null;
    }

    static SwingCEdgeResult CopyWithEdgeKind(SwingCEdgeResult src, string edgeKind) => new()
    {
        PivotIndex = src.PivotIndex,
        PivotBar = src.PivotBar,
        PivotType = src.PivotType,
        TakeProfit = src.TakeProfit,
        EdgeTop = src.EdgeTop,
        EdgeBottom = src.EdgeBottom,
        EdgeKind = edgeKind,
        IsOb = src.IsOb,
        TfToken = src.TfToken,
    };

    /// <summary>
    /// Resolve the "near-D" cancel-gate zone:
    ///   1. Use <paramref name="dResult"/> when it carries a real zone (EdgeTop &gt; EdgeBottom).
    ///   2. Else, scan <paramref name="fallbackStates"/> (typically Daily) for a direction-correct zone
    ///      beyond <paramref name="referencePrice"/> (fire-bar HIGH/LOW at plan time).
    ///   3. Else, return null (gate skipped).
    /// </summary>
    public static SwingCEdgeResult? ResolveNearDCancelZone(
        SwingCEdgeResult? dResult,
        double referencePrice,
        bool isBuy,
        double entry,
        IReadOnlyDictionary<string, PineStateEngine>? fallbackStates)
    {
        if (dResult is not null && dResult.EdgeTop > dResult.EdgeBottom)
            return dResult;

        if (fallbackStates is null || fallbackStates.Count == 0)
            return null;

        return TryFindDZone(fallbackStates, referencePrice, isBuy, entry);
    }

    /// <summary>
    /// D zone scan config: active KeyLevels + OBs, no broken levels.
    /// </summary>
    static readonly EntrySlZoneGateConfig s_dZoneScanCfg = new()
    {
        IncludeKeyLevels = true,
        IncludeOrderBlocks = true,
        IncludeBrokenKeyLevels = false,
    };

    /// <summary>
    /// Find D by scanning existing zones across the provided TF states (expected: M5/M15/H1/H4).
    /// BUY  → nearest Red zone whose Low is above <paramref name="referencePrice"/>.
    /// SELL → nearest Green zone whose High is below <paramref name="referencePrice"/>.
    /// "Nearest" = closest price distance from the reference, ensuring TP is valid relative to entry.
    /// </summary>
    public static SwingCEdgeResult? TryFindDZone(
        IReadOnlyDictionary<string, PineStateEngine> dZoneStates,
        double referencePrice,
        bool isBuy,
        double entry)
    {
        var wantColor = isBuy ? ZoneEffectiveColor.Red : ZoneEffectiveColor.Green;
        double bestDist = double.MaxValue;
        double bestLow = 0, bestHigh = 0;
        var bestIsOb = false;
        string bestTf = "";
        var found = false;

        foreach (var (tfToken, tfState) in dZoneStates)
        {
            var zones = MultiTfZoneSnapshot.Collect(tfState, tfToken, s_dZoneScanCfg);
            foreach (var z in zones)
            {
                if (z.EffectiveColor != wantColor)
                    continue;

                if (isBuy)
                {
                    if (z.Low <= referencePrice) continue;
                    if (z.Low <= entry) continue;
                    var dist = z.Low - referencePrice;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestLow = z.Low;
                        bestHigh = z.High;
                        bestIsOb = z.Source == ZoneSourceKind.OrderBlock;
                        bestTf = tfToken;
                        found = true;
                    }
                }
                else
                {
                    if (z.High >= referencePrice) continue;
                    if (z.High >= entry) continue;
                    var dist = referencePrice - z.High;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestLow = z.Low;
                        bestHigh = z.High;
                        bestIsOb = z.Source == ZoneSourceKind.OrderBlock;
                        bestTf = tfToken;
                        found = true;
                    }
                }
            }
        }

        if (!found)
            return null;

        var dTp = isBuy ? bestLow : bestHigh;
        return new SwingCEdgeResult
        {
            PivotIndex = -1,
            PivotBar   = -1,
            PivotType  = isBuy ? SwingBResolver.TypeHigh : SwingBResolver.TypeLow,
            TakeProfit = dTp,
            EdgeTop    = bestHigh,
            EdgeBottom = bestLow,
            EdgeKind   = "DZone",
            IsOb       = bestIsOb,
            TfToken    = bestTf,
        };
    }

    /// <summary>
    /// Find D = nearest pivot AFTER C that has a KeyBox (H1/H4/M15 key level — no M5-only OB)
    /// and whose zone clears C in the trade direction:
    ///   BUY:  KeyBox lower edge > C price  (zone is entirely above C)
    ///   SELL: KeyBox upper edge &lt; C price (zone is entirely below C)
    /// Returns null when no qualifying D exists.
    /// Kept for backward compatibility / unit tests that do not supply dZoneStates.
    /// </summary>
    static SwingCEdgeResult? TryFindDKeyLevel(
        PineStateEngine state,
        PivotStateStore pivots,
        int cBar,
        double cPrice,
        bool isBuy,
        double entry)
    {
        var bestIdx = -1;
        var bestBar = int.MaxValue;

        for (var i = 0; i < pivots.Count; i++)
        {
            var snap = pivots.GetSnapshot(i);

            // D must appear AFTER C in bar time
            if (snap.BarIndex <= cBar)
                continue;

            // D must have a KeyBox (H1/H4/M15 keylevel) — skip M5-only OB pivots
            if (!pivots.GetHasKey(i))
                continue;
            var kb = pivots.GetKeyBox(i);
            if (kb?.Spec == null)
                continue;

            var zoneTop    = Math.Max(kb.Spec.Top, kb.Spec.Bottom);
            var zoneBottom = Math.Min(kb.Spec.Top, kb.Spec.Bottom);

            // Direction guard: the zone must clear C
            if (isBuy)
            {
                // BUY: zone bottom must be above C price (resistance above C)
                if (zoneBottom <= cPrice)
                    continue;
            }
            else
            {
                // SELL: zone top must be below C price (support below C)
                if (zoneTop >= cPrice)
                    continue;
            }

            // TP side guard: TP must be above entry (BUY) or below entry (SELL)
            var dTp = isBuy ? zoneBottom : zoneTop;
            if (isBuy  && dTp <= entry) continue;
            if (!isBuy && dTp >= entry) continue;

            // Pick the nearest D in bar time
            if (snap.BarIndex < bestBar)
            {
                bestBar = snap.BarIndex;
                bestIdx = i;
            }
        }

        if (bestIdx < 0)
            return null;

        var dSnap = pivots.GetSnapshot(bestIdx);
        var dKb   = pivots.GetKeyBox(bestIdx)!;
        var dTop    = Math.Max(dKb.Spec.Top, dKb.Spec.Bottom);
        var dBottom = Math.Min(dKb.Spec.Top, dKb.Spec.Bottom);
        var dTakeProfit = isBuy ? dBottom : dTop;

        return new SwingCEdgeResult
        {
            PivotIndex = bestIdx,
            PivotBar   = dSnap.BarIndex,
            PivotType  = dSnap.Type,
            TakeProfit = dTakeProfit,
            EdgeTop    = dTop,
            EdgeBottom = dBottom,
            EdgeKind   = "DKeyBox",
        };
    }

    /// <summary>Pine D_SWING pivot flag (promoted D leg).</summary>
    public const int FlagDSwing = SwingBResolver.FlagDSwing;

    static int FindNearestConfirmedPivotAfter(PivotStateStore pivots, int bBar, int wantType)
    {
        var bestIdx = -1;
        var bestBar = int.MaxValue;
        for (var i = 0; i < pivots.Count; i++)
        {
            var snap = pivots.GetSnapshot(i);
            if (snap.BarIndex <= bBar)
                continue;
            if (snap.Type != wantType)
                continue;
            if (!IsValidCCandidate(pivots, i))
                continue;
            if (snap.BarIndex < bestBar)
            {
                bestBar = snap.BarIndex;
                bestIdx = i;
            }
        }

        return bestIdx;
    }

    /// <summary>Entry/plan: swing C must be ACTIVE or D_SWING.</summary>
    public static bool IsValidCEntryCandidate(PivotStateStore pivots, int idx)
    {
        var flag = pivots.GetFlag(idx);
        return flag == SwingBResolver.FlagActive || flag == FlagDSwing;
    }

    /// <summary>Pending invalidation: C transitioned to BROKEN while waiting for D.</summary>
    public static bool IsBrokenWaitingD(PivotStateStore pivots, int idx) =>
        pivots.GetFlag(idx) == 2
        && pivots.GetFirstDIdx(idx) == PivotStateStore.FirstDWaiting;

    public static bool IsBrokenWaitingD(PineStateEngine state, int pivotBar)
    {
        var idx = SwingBrokenChecker.FindByBar(state, pivotBar);
        return idx >= 0 && IsBrokenWaitingD(state.Pivots, idx);
    }

    static bool IsValidCCandidate(PivotStateStore pivots, int idx) =>
        IsValidCEntryCandidate(pivots, idx);

    sealed class CEdgeGeometry
    {
        public double EdgeTop { get; init; }
        public double EdgeBottom { get; init; }
        public double LowerEdge { get; init; }
        public double UpperEdge { get; init; }
        public string EdgeKind { get; init; } = "";
        public bool TpFromOb { get; init; }
    }

    sealed class CEdgeCandidate
    {
        public double Edge { get; init; }
        public string Kind { get; init; } = "";
        public bool FromOb { get; init; }
    }

    static CEdgeGeometry ResolveCEdges(
        PineStateEngine state,
        PivotStateStore pivots,
        int cIdx,
        int pivotType,
        double pivotPrice,
        double entry,
        bool isBuy,
        SwingCEdgeCrossTfContext? crossTf)
    {
        double edgeTop;
        double edgeBottom;
        string edgeKind;
        var tpFromOb = false;
        double tpLower;
        double tpUpper;

        if (pivots.GetHasKey(cIdx) && pivots.GetKeyBox(cIdx)?.Spec is { } keySpec)
        {
            edgeTop = Math.Max(keySpec.Top, keySpec.Bottom);
            edgeBottom = Math.Min(keySpec.Top, keySpec.Bottom);

            var candidates = new List<CEdgeCandidate>();
            AddKeyCEdgeCandidates(isBuy, edgeBottom, edgeTop, candidates);

            if (TryGetPivotObZinBox(state, cIdx, out var m15ObTop, out var m15ObBottom))
                AddObZinCEdgeCandidates(isBuy, m15ObTop, m15ObBottom, "ObZin", candidates);

            if (crossTf is not null
                && TryFindCorrespondingM5CPivot(
                    crossTf.M5State, pivots, cIdx, crossTf.ChartBuffer, crossTf.M5Buffer,
                    crossTf.ChartTfToken, out var m5CIdx)
                && TryGetPivotObZinBox(crossTf.M5State, m5CIdx, out var m5ObTop, out var m5ObBottom))
            {
                AddObZinCEdgeCandidates(isBuy, m5ObTop, m5ObBottom, "M5ObZin", candidates);
            }

            var tpEdge = PickNearestCEdgeFromCandidates(isBuy, entry, candidates, edgeBottom, edgeTop,
                out edgeKind, out tpFromOb);
            tpLower = tpUpper = tpEdge;
        }
        else if (TryGetPivotObZinBox(state, cIdx, out var obTop, out var obBottom))
        {
            edgeTop = obTop;
            edgeBottom = obBottom;
            edgeKind = "ObZin";
            tpFromOb = true;
            tpLower = obBottom;
            tpUpper = obTop;
        }
        else
        {
            edgeTop = pivotPrice;
            edgeBottom = pivotPrice;
            edgeKind = pivotType == SwingBResolver.TypeHigh ? "PriceHigh" : "PriceLow";
            tpLower = tpUpper = pivotPrice;
        }

        return new CEdgeGeometry
        {
            EdgeTop = edgeTop,
            EdgeBottom = edgeBottom,
            UpperEdge = tpUpper,
            LowerEdge = tpLower,
            EdgeKind = edgeKind,
            TpFromOb = tpFromOb,
        };
    }

    static void AddKeyCEdgeCandidates(bool isBuy, double keyBottom, double keyTop, List<CEdgeCandidate> list)
    {
        if (isBuy)
            list.Add(new CEdgeCandidate { Edge = keyBottom, Kind = "KeyBox", FromOb = false });
        else
            list.Add(new CEdgeCandidate { Edge = keyTop, Kind = "KeyBox", FromOb = false });
    }

    static void AddObZinCEdgeCandidates(
        bool isBuy, double obTop, double obBottom, string kind, List<CEdgeCandidate> list)
    {
        if (isBuy)
            list.Add(new CEdgeCandidate { Edge = obBottom, Kind = kind, FromOb = true });
        else
            list.Add(new CEdgeCandidate { Edge = obTop, Kind = kind, FromOb = true });
    }

    /// <summary>
    /// Among KLV C, M15 OB ZIN on C pivot, and M5 corresponding C OB ZIN — pick the edge nearest entry.
    /// </summary>
    static double PickNearestCEdgeFromCandidates(
        bool isBuy,
        double entry,
        IReadOnlyList<CEdgeCandidate> candidates,
        double fallbackBottom,
        double fallbackTop,
        out string edgeKind,
        out bool tpFromOb)
    {
        edgeKind = "KeyBox";
        tpFromOb = false;
        var bestEdge = isBuy ? fallbackBottom : fallbackTop;
        var bestDist = double.MaxValue;
        var found = false;

        foreach (var c in candidates)
        {
            double dist;
            if (isBuy)
            {
                if (c.Edge <= entry)
                    continue;
                dist = c.Edge - entry;
            }
            else
            {
                if (c.Edge >= entry)
                    continue;
                dist = entry - c.Edge;
            }

            if (dist < bestDist)
            {
                bestDist = dist;
                bestEdge = c.Edge;
                edgeKind = c.Kind;
                tpFromOb = c.FromOb;
                found = true;
            }
        }

        return found ? bestEdge : bestEdge;
    }

    /// <summary>
    /// M5 swing C whose bar open time falls inside the M15 C bar window and matches type/price.
    /// </summary>
    static bool TryFindCorrespondingM5CPivot(
        PineStateEngine m5State,
        PivotStateStore m15Pivots,
        int m15CIdx,
        SeriesBuffer? m15Buffer,
        SeriesBuffer? m5Buffer,
        string chartTfToken,
        out int m5CIdx)
    {
        m5CIdx = -1;
        if (m5Buffer is null || m15Buffer is null)
            return false;

        var m15Snap = m15Pivots.GetSnapshot(m15CIdx);
        if (!TryResolvePivotOpenTime(m15Buffer, m15Snap.BarIndex, out var cStart))
            return false;

        var chartPeriod = TfPeriodTokens.Parse(chartTfToken);
        if (chartPeriod <= TimeSpan.Zero)
            return false;

        var cEnd = cStart + chartPeriod;
        var bestPriceDist = double.MaxValue;
        var found = false;

        for (var i = 0; i < m5State.Pivots.Count; i++)
        {
            if (m5State.Pivots.GetSnapshot(i).Type != m15Snap.Type)
                continue;
            if (!IsValidCCandidate(m5State.Pivots, i))
                continue;

            var m5Bar = m5State.Pivots.GetSnapshot(i).BarIndex;
            if (!TryResolvePivotOpenTime(m5Buffer, m5Bar, out var m5Start))
                continue;
            if (m5Start < cStart || m5Start >= cEnd)
                continue;

            var priceDist = Math.Abs(m5State.Pivots.GetSnapshot(i).Price - m15Snap.Price);
            if (!found || priceDist < bestPriceDist)
            {
                bestPriceDist = priceDist;
                m5CIdx = i;
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

    static bool TryGetPivotObZinBox(PineStateEngine state, int pivotIdx, out double top, out double bottom)
    {
        top = bottom = 0;
        var obPool = state.ObPool;
        for (var i = 0; i < obPool.Count; i++)
        {
            var r = obPool.GetRecord(i);
            if (r.Owner != pivotIdx || r.Box is null)
                continue;
            if (r.State != 2)
                continue;
            if (!r.Extending)
                continue;

            top = Math.Max(r.Box.Top, r.Box.Bottom);
            bottom = Math.Min(r.Box.Top, r.Box.Bottom);
            if (top >= bottom)
                return true;
        }

        return false;
    }

    public static string FormatPlanLog(
        in TradePlan plan,
        int ruleSlot,
        in SwingCEdgeResult swingC)
    {
        var dir = plan.IsBuy ? "BUY" : "SELL";
        var cType = swingC.PivotType == SwingBResolver.TypeHigh ? "HIGH" : "LOW";
        return $"[L6BT] TP SWING-C rule=R{ruleSlot + 1} dir={dir} " +
               $"entry={Fmt(plan.EntryLimit)} sl={Fmt(plan.StopLoss)} " +
               $"Cbar={swingC.PivotBar} Ctype={cType} " +
               $"CedgeTop={Fmt(swingC.EdgeTop)} CedgeBottom={Fmt(swingC.EdgeBottom)} " +
               $"initialTP={Fmt(plan.TakeProfit)} source=SwingCEdge";
    }

    static string Fmt(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

    /// <summary>
    /// When swing C/D edge TP yields RR below <paramref name="minRewardRisk"/>, extend TP to base RR.
    /// If edge RR is already &gt;= min, the structure edge is kept unchanged.
    /// </summary>
    public static double ApplyMinRrFloor(
        double edgeTp,
        double entry,
        double sl,
        bool isBuy,
        double minRewardRisk)
    {
        var minRr = minRewardRisk <= 0 ? 2.0 : minRewardRisk;
        var risk = Math.Abs(entry - sl);
        if (risk <= 0)
            return edgeTp;

        var reward = isBuy ? edgeTp - entry : entry - edgeTp;
        if (reward / risk >= minRr)
            return edgeTp;

        return isBuy ? entry + minRr * risk : entry - minRr * risk;
    }

    /// <summary>Reward:risk from entry to a structure edge TP.</summary>
    public static double ComputeEdgeRr(double edgeTp, double entry, double sl, bool isBuy)
    {
        var risk = Math.Abs(entry - sl);
        if (risk <= 0)
            return 0;
        var reward = isBuy ? edgeTp - entry : entry - edgeTp;
        return reward / risk;
    }

    /// <summary>
    /// Incremental RR from final C-leg TP to raw D edge: RR(D) − RR(C). Symmetric for buy/sell.
    /// </summary>
    public static double ComputeIncrementalRrAboveC(
        double tpC, double tpDRaw, double entry, double sl, bool isBuy) =>
        ComputeEdgeRr(tpDRaw, entry, sl, isBuy) - ComputeEdgeRr(tpC, entry, sl, isBuy);

    /// <summary>
    /// Split D leg: when incremental RR ≤ <paramref name="minIncrementalRr"/>, TP D should equal TP C.
    /// Evaluated on the raw D edge before <see cref="ApplyMinRrFloor"/>.
    /// </summary>
    public static bool ShouldFallbackDLegTpToC(
        double tpC,
        double tpDRaw,
        double entry,
        double sl,
        bool isBuy,
        double minIncrementalRr = 0.5)
    {
        if (minIncrementalRr <= 0)
            return false;
        return ComputeIncrementalRrAboveC(tpC, tpDRaw, entry, sl, isBuy) <= minIncrementalRr;
    }

    public static bool IsEdgeRrBelowBase(
        double edgeTp,
        double entry,
        double sl,
        bool isBuy,
        double minRewardRisk)
    {
        var minRr = minRewardRisk <= 0 ? 2.0 : minRewardRisk;
        return ComputeEdgeRr(edgeTp, entry, sl, isBuy) < minRr;
    }

    /// <summary>
    /// Resolve swing-C edge only (ignores D / NextKeyLevelAfterC targeting).
    /// </summary>
    public static SwingCEdgeResult? TryResolveC(
        PineStateEngine state,
        bool isBuy,
        int swingBBar,
        double entry,
        out string reason,
        SwingCEdgeCrossTfContext? crossTf = null) =>
        TryResolve(state, isBuy, swingBBar, entry, out reason, SwingCTpMode.UpdateOnCConfirm, crossTf);

    /// <summary>
    /// Applies RR floor unless <paramref name="skipIfBelowBase"/> rejects sub-min RR at the C edge.
    /// </summary>
    public static bool TryResolveSwingEdgeTp(
        double edgeTp,
        double entry,
        double sl,
        bool isBuy,
        double minRewardRisk,
        bool skipIfBelowBase,
        out double tp,
        out string? rejectReason)
    {
        rejectReason = null;
        tp = edgeTp;
        var minRr = minRewardRisk <= 0 ? 2.0 : minRewardRisk;

        if (skipIfBelowBase && IsEdgeRrBelowBase(edgeTp, entry, sl, isBuy, minRr))
        {
            var rrEdge = ComputeEdgeRr(edgeTp, entry, sl, isBuy);
            rejectReason =
                $"swing-C RR {rrEdge:0.##} < base {minRr:0.##} (edge={edgeTp:0.#####} entry={entry:0.#####} sl={sl:0.#####})";
            return false;
        }

        tp = skipIfBelowBase
            ? edgeTp
            : ApplyMinRrFloor(edgeTp, entry, sl, isBuy, minRr);
        return true;
    }

    /// <summary>
    /// Computes the entry price that makes the swing-C edge RR exactly equal to base RR by moving
    /// the entry into the keylevel interior while the SL stays anchored to the swing-X edge.
    /// <para>
    /// BUY: entry moves DOWN, allowed band [<paramref name="keyBottom"/>, <paramref name="originalEntry"/>].
    /// SELL: entry moves UP, allowed band [<paramref name="originalEntry"/>, <paramref name="keyTop"/>].
    /// </para>
    /// Returns false when the required entry falls outside the allowed band (RR base unreachable) or
    /// when the resulting risk would be non-positive.
    /// </summary>
    public static bool TryComputeBaseRrEntry(
        double swingCTp,
        double sl,
        bool isBuy,
        double baseRewardRisk,
        double originalEntry,
        double keyTop,
        double keyBottom,
        out double movedEntry,
        out string reason)
    {
        // Solving (TP - e)/(e - SL) = R for BUY  (and mirror for SELL) gives the same closed form:
        //   e = (TP + R*SL) / (1 + R)
        var rr = baseRewardRisk <= 0 ? 2.0 : baseRewardRisk;
        movedEntry = (swingCTp + rr * sl) / (1.0 + rr);
        const double eps = 1e-9;

        if (isBuy)
        {
            if (movedEntry > originalEntry + eps)
            {
                reason = $"moved entry {Fmt(movedEntry)} above original {Fmt(originalEntry)}";
                return false;
            }
            if (movedEntry < keyBottom - eps)
            {
                reason = $"moved entry {Fmt(movedEntry)} below keyBottom {Fmt(keyBottom)}";
                return false;
            }
            if (movedEntry - sl <= 0)
            {
                reason = "non-positive risk after move";
                return false;
            }
        }
        else
        {
            if (movedEntry < originalEntry - eps)
            {
                reason = $"moved entry {Fmt(movedEntry)} below original {Fmt(originalEntry)}";
                return false;
            }
            if (movedEntry > keyTop + eps)
            {
                reason = $"moved entry {Fmt(movedEntry)} above keyTop {Fmt(keyTop)}";
                return false;
            }
            if (sl - movedEntry <= 0)
            {
                reason = "non-positive risk after move";
                return false;
            }
        }

        reason = "ok";
        return true;
    }
}
