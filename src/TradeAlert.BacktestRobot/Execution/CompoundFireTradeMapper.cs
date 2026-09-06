using System;
using System.Collections.Generic;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.Alerts;
using TradeAlert.Core.Series;
using TradeAlert.BacktestRobot.Execution.Kl;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>Symbol + risk inputs needed to size and price a trade (no cAlgo types).</summary>
public readonly struct TradeMapperConfig
{
    public string SymbolName { get; init; }
    public double TickSize { get; init; }
    public double PipSize { get; init; }
    public double SpreadPips { get; init; }
    /// <summary><see cref="SpreadTelemetrySources.Override"/> or <see cref="SpreadTelemetrySources.Symbol"/>.</summary>
    public string SpreadSource { get; init; } = SpreadTelemetrySources.Symbol;

    public double AccountBalanceFtmo { get; init; }
    public double RiskPercent { get; init; }
    public int RoundPrecision { get; init; }

    /// <summary>Reward:Risk ratio (Rule 4, default 2.0).</summary>
    public double RewardRisk { get; init; }
    /// <summary>SL width multiplier of keylevel thickness (Rule 2, default 2).</summary>
    public double SlWidthMult { get; init; }

    /// <summary>
    /// When true, SL = entry ± (keylevel width × mult). When false (default), SL is anchored to the
    /// swing-X keylevel edge (KeyTop for BUY / KeyBottom for SELL) and does not shift with entry.
    /// </summary>
    public bool SlFromEntry { get; init; }

    public KlAssetType AssetType { get; init; }
    public int CustomContractSize { get; init; }
    public bool IsCryptoSymbol { get; init; }
    public string QuoteCurrency { get; init; }
    public string BaseAssetName { get; init; }
    /// <summary>USD per 1 unit of quote currency (0 = let KlEntryLot resolve USD quotes).</summary>
    public double ConvUsdPerQuote { get; init; }

    public string LabelPrefix { get; init; }
    /// <summary>Chart TF token for swing-B resolution (e.g. "15").</summary>
    public string ChartTfToken { get; init; } = "15";

    /// <summary>M5 shell state for R1/R2 swing structure on M15 chart.</summary>
    public PineStateEngine? M5FallbackState { get; init; }

    /// <summary>M5 series buffer paired with <see cref="M5FallbackState"/> for bar-time anchoring.</summary>
    public SeriesBuffer? M5FallbackBuffer { get; init; }

    /// <summary>Legacy flag — R1/R2 always use M5 structure on M15 chart when M5 state is available.</summary>
    public bool EnableM5SwingBFallback { get; init; }

    public bool UseSwingCEdgeTakeProfit { get; init; }
    public bool SwingCEdgeTpFallbackToRR { get; init; } = true;
    public HashSet<int> SwingCEdgeTpRuleSlots { get; init; } = new();
    public SwingCTpMode SwingCTpMode { get; init; } = SwingCTpMode.UpdateOnCConfirm;

    /// <summary>
    /// When true, skip/cancel when swing-C edge RR &lt; base RewardRisk (no RR floor bump).
    /// SplitTpAtCAndD: no orders at all. NoTpUntilC: cancel pending/open when C confirms.
    /// </summary>
    public bool SkipIfSwingCRrBelowBase { get; init; }

    /// <summary>
    /// SplitTpAtCAndD only: when the swing-C edge RR &lt; base RewardRisk, move the entry into the
    /// keylevel so that C-RR == base (SL stays anchored to swing-X). If base RR is unreachable within
    /// the keylevel band, or the moved entry fails the min-SL floor, no orders are placed at all.
    /// Takes priority over <see cref="SkipIfSwingCRrBelowBase"/>.
    /// </summary>
    public bool MoveEntryIfSwingCRrBelowBase { get; init; }

    /// <summary>
    /// SplitTpAtCAndD: min incremental RR (D vs C) before D leg TP falls back to C. 0 = off. Default 0.5.
    /// Near-D cancel gate uses the original D zone — unaffected by this fallback.
    /// </summary>
    public double SplitDLegMinIncrementalRr { get; init; } = 0.5;

    /// <summary>
    /// Controls whether spread is applied to order levels (entry/SL/TP).
    /// false (default): levels are placed as-is at bid prices — no spread shift, no level adjustment.
    /// true: ForwardSpreadOnBidLevels is applied, then TP is recalculated to preserve cfgRR exactly
    ///       (RR mode only; SwingC/D edge TP is a fixed price level and is not recalculated).
    /// </summary>
    public bool RecalcTpAfterSpread { get; init; } = false;

    public AtrSlWidthAdjustConfig AtrSlWidthAdjust { get; init; } = new();

    /// <summary>
    /// When true, the computed SL distance is clamped to a minimum based on spread and (for Forex) an
    /// absolute pip floor, so that very tight key-levels do not produce sub-spread stop-losses.
    /// </summary>
    public bool EnableMinSlConstraint { get; init; }

    /// <summary>Multiplier of SpreadPips used as the minimum SL. Default 10.0 → minSL = spread × 10 pips.</summary>
    public double MinSlSpreadMultiple { get; init; } = 10.0;

    /// <summary>Absolute minimum SL in pips for Forex symbols. Default 5 pips. Ignored for XAUUSD/Crypto/Custom.</summary>
    public double MinSlPipsForex { get; init; } = 5.0;

    /// <summary>
    /// Controls behaviour when the computed SL is below the minimum:<br/>
    /// <c>true</c>  — <b>Backtest mode</b>: clamp SL up to the floor so the trade still executes.<br/>
    /// <c>false</c> — <b>Live mode</b>: reject the trade entirely (return null) without touching SL.
    /// </summary>
    public bool SlConstraintBacktestMode { get; init; } = true;

    /// <summary>
    /// Multi-TF states for D zone lookup in <see cref="SwingCTpMode.SplitTpAtCAndD"/> mode.
    /// Keys: M5 ("5"), M15 ("15"), H1 ("60"), H4 ("240"). Used for near-D cancel and default TP D.
    /// When null or empty, D resolution falls back to the pivot-after-C heuristic.
    /// </summary>
    public IReadOnlyDictionary<string, PineStateEngine>? DZoneStates { get; init; }

    /// <summary>
    /// Gồng lời: M15/H1/H4 only — optional higher-TF TP D for R3–R6 when <see cref="EnableGongLoiTpD"/> is true.
    /// Never used for near-D cancel. When D' is not found, TP D falls back to <see cref="DZoneStates"/> D.
    /// </summary>
    public IReadOnlyDictionary<string, PineStateEngine>? DPrimeZoneStates { get; init; }

    /// <summary>
    /// Gồng lời: R3–R6 (slot 2–5) prefer D' (M15/H1/H4) for TP leg D; D zone unchanged for cancel.
    /// </summary>
    public bool EnableGongLoiTpD { get; init; }

    /// <summary>
    /// Optional Daily ("1D") states used ONLY as the near-D pending-cancel gate fallback when no
    /// D zone is available in <see cref="DZoneStates"/> (M5/M15/H1/H4). Never used to set the D leg TP.
    /// Keys are TF tokens (typically "1D"). Null/empty → no Daily fallback.
    /// </summary>
    public IReadOnlyDictionary<string, PineStateEngine>? NearDZoneFallbackStates { get; init; }

    /// <summary>
    /// When true, skip trade planning if the swing-C zone overlaps a same-color zone on any TF in
    /// <see cref="SwingCZoneObstacleStates"/>. Applies to both SplitTpAtCAndD and single-leg SwingC modes.
    /// Default false; set to true via bot parameter <c>EnableSwingCZoneObstacleGate</c>.
    /// </summary>
    public bool EnableSwingCZoneObstacleGate { get; init; }

    /// <summary>
    /// TF states for the swing-C zone obstacle gate.
    /// Recommended keys: M5 ("5"), M15 ("15"), H1 ("60"), H4 ("240").
    /// Null/empty → gate is effectively disabled even when <see cref="EnableSwingCZoneObstacleGate"/> is true.
    /// </summary>
    public IReadOnlyDictionary<string, PineStateEngine>? SwingCZoneObstacleStates { get; init; }

    /// <summary>
    /// Optional SeriesBuffer for the chart TF, used by the swing-C zone obstacle gate's cross-TF
    /// self-identity guard (resolving the C pivot's open time). Null disables cross-TF time containment;
    /// same-TF bar-index matching still works.
    /// </summary>
    public SeriesBuffer? ChartSeriesBuffer { get; init; }

    /// <summary>
    /// Optional fallback TF states (typically Daily "1440") scanned by the swing-C zone obstacle gate
    /// ONLY when no obstacle is found in <see cref="SwingCZoneObstacleStates"/> (M5/M15/H1/H4).
    /// Null/empty → no Daily fallback.
    /// </summary>
    public IReadOnlyDictionary<string, PineStateEngine>? SwingCZoneObstacleFallbackStates { get; init; }

    /// <summary>
    /// Per-TF SeriesBuffers for the swing-C zone obstacle gate. Keys must match the tokens in
    /// <see cref="SwingCZoneObstacleStates"/> (e.g. "5", "15", "60", "240").
    /// Required for cross-TF self-identity detection: without these, zones on TFs other than the
    /// chart TF (especially M5) cannot compare open times and the C pivot's own zone may be
    /// wrongly counted as an obstacle.
    /// </summary>
    public IReadOnlyDictionary<string, SeriesBuffer>? SwingCZoneObstacleTfBuffers { get; init; }

    /// <summary>
    /// H4 SL mode: thay base SL bằng High/Low của H4 bar đã đóng trước/tại <see cref="SlEvalTime"/>,
    /// guard theo p25/p75 của break-depth stats. Ngoài band → fallback mean. Resolver fail → giữ base SL.
    /// </summary>
    public bool EnableH4SlMode { get; init; }

    /// <summary>H4 ("240") series buffer dùng quét High/Low cho H4 SL mode (R3–R6).</summary>
    public SeriesBuffer? H4SeriesBuffer { get; init; }

    /// <summary>H1 ("60") series buffer dùng quét High/Low thay H4 khi <see cref="H4SlR1R2UseH1"/> bật (R1/R2).</summary>
    public SeriesBuffer? H1SeriesBuffer { get; init; }

    /// <summary>
    /// Khi true, R1/R2 (slot 0/1) dùng H1 High/Low thay vì H4 để resolve SL.
    /// R3–R6 (slot ≥ 2) vẫn dùng H4 như bình thường.
    /// </summary>
    public bool H4SlR1R2UseH1 { get; init; }

    /// <summary>
    /// Break-depth stats (đơn vị pip) cho đúng side của lệnh hiện tại — stats thật khi pool đủ mẫu,
    /// hoặc band fallback [min,max] khi pool side chưa đủ mẫu. Null → H4 mode không chạy.
    /// </summary>
    public BreakDepthStats? H4BreakDepthStats { get; init; }

    /// <summary>Thời điểm bot thực sự được phép biết dữ liệu (chống leak H4); set đúng theo từng call-site.</summary>
    public System.DateTime SlEvalTime { get; init; }

    /// <summary>Số H4 bar tối đa quét ngược về quá khứ trong H4 SL mode (default 50).</summary>
    public int H4SlMaxLookbackBars { get; init; }

    /// <summary>Trần dưới band guard (dist pip tối thiểu để dùng giá H4).</summary>
    public H4SlBandStatRef H4SlBandLowStat { get; init; } = H4SlBandStatRef.P25;

    /// <summary>Trần trên band guard (dist pip tối đa để dùng giá H4).</summary>
    public H4SlBandStatRef H4SlBandHighStat { get; init; } = H4SlBandStatRef.P75;

    /// <summary>
    /// Chiến lược fallback khi dist HTF bar vượt trần trên: mean/p25/p75/pmin/pmax hoặc SlH1 (tìm H1 bar trong band).
    /// </summary>
    public H4SlHiFallbackRef H4SlHiFallbackStat { get; init; } = H4SlHiFallbackRef.Mean;

    /// <summary>
    /// Khi <see cref="H4SlHiFallbackStat"/> == <c>SlH1</c> và không tìm được H1 bar trong band:
    /// stat dự phòng cuối cùng (mean/p25/p75/pmin/pmax). Mặc định Mean.
    /// </summary>
    public H4SlBandStatRef H4SlHiFallbackSlH1NoBarStat { get; init; } = H4SlBandStatRef.Mean;

    /// <summary>
    /// H4 SL RR guard 2 bước: khi RR sau H4 SL &lt; base RR →
    ///   Pass 1: chỉnh entry trong keylevel (<c>[H4RR-ADJ]</c>) → vào lệnh;
    ///   Pass 2 (nếu pass 1 thất bại): rollback entry về mép KL ban đầu, SL = mean depth →
    ///     - RR đủ ngay → <c>[H4RR-MEAN-OK]</c>
    ///     - Chỉnh entry với mean SL → <c>[H4RR-MEAN-ADJ]</c>
    ///     - Vẫn không đủ → <c>[H4RR-SKIP]</c> bỏ lệnh.
    /// Áp dụng cho SwingC TP (split và single-leg); RR-based TP luôn = base nên không ảnh hưởng.
    /// </summary>
    public bool H4SlRrGuard { get; init; }
}

/// <summary>D zone locked at fire bar (HIGH for BUY / LOW for SELL) plus near-D cancel geometry and optional D' for TP.</summary>
public readonly record struct DFireAtFireCache(
    SwingCEdgeResult? DFire,
    double ReferencePrice,
    SwingCEdgeResult? NearDCancelZone,
    SwingCEdgeResult? DPrimeFire = null);

/// <summary>
/// Builds a <see cref="TradePlan"/> from a <see cref="CompoundFireEvent"/> using Rules 1-5:
/// gate swing B, SL = keylevel edge ± width×mult, entry = keylevel/OB edge, TP = RR, spread-adjust
/// (Bid chart) and size lot via KlEntryLot FTMO branch. Pure (cAlgo-free) for unit testing.
/// </summary>
public static class CompoundFireTradeMapper
{
    const double SplitLegRiskFraction = 0.5;

    public static TradePlan? TryBuild(
        in CompoundFireEvent ev,
        PineStateEngine state,
        in TradeMapperConfig cfg,
        out string reason,
        Func<string, SeriesBuffer?>? resolveAtrSeries = null)
    {
        var plans = TryBuildPlans(in ev, state, in cfg, out reason, resolveAtrSeries);
        return plans.Count > 0 ? plans[0] : null;
    }

    public static IReadOnlyList<TradePlan> TryBuildPlans(
        in CompoundFireEvent ev,
        PineStateEngine state,
        in TradeMapperConfig cfg,
        out string reason,
        Func<string, SeriesBuffer?>? resolveAtrSeries = null,
        SwingBResult? pinnedB = null,
        SwingCEdgeResult? injectedSplitC = null,
        SwingCEdgeResult? injectedSplitD = null,
        bool rolloverMode = false,
        double fireBarHigh = double.NaN,
        double fireBarLow = double.NaN,
        SwingCEdgeResult? cachedDFireAtFire = null,
        SwingCEdgeResult? cachedDPrimeAtFire = null)
    {
        var results = new List<TradePlan>();
        reason = "";

        if (ev.Direction != SignalDirection.Buy && ev.Direction != SignalDirection.Sell)
        {
            reason = $"direction {ev.Direction} not tradable";
            return results;
        }

        var isBuy = ev.Direction == SignalDirection.Buy;

        SwingBResult b;
        if (pinnedB is not null)
        {
            b = pinnedB;
            reason = "ok (pinned B at fire time)";
        }
        else
        {
            var resolved = ResolveSwingB(state, isBuy, ev.SlotIndex, in cfg, out reason);
            if (resolved is null)
                return results;
            b = resolved;
        }

        var usesM5SwingC = R1R2M5Structure.UsesM5SwingC(ev.SlotIndex, in cfg);
        var crossTf = SwingCEdgeCrossTfContext.FromMapperConfig(in cfg);
        var swingState = usesM5SwingC ? cfg.M5FallbackState! : state;
        var swingBBar = usesM5SwingC
            ? R1R2M5Structure.ResolveM5SwingBBarForC(in b, in cfg)
            : b.PivotBar;
        var swingCCrossTf = usesM5SwingC ? null : crossTf;
        var dZoneStates = cfg.DZoneStates;

        var kT = b.KeyTop;
        var kB = b.KeyBottom;
        var w = kT - kB;
        if (w <= 0)
        {
            reason = "degenerate keylevel width";
            return results;
        }

        var swingCCrossTfForEntry = crossTf;
        var useSwingCEntryPick = cfg.UseSwingCEdgeTakeProfit && cfg.SwingCEdgeTpRuleSlots.Contains(ev.SlotIndex);
        double? cRefForEntry = null;
        if (useSwingCEntryPick)
        {
            var provisionalEntry = isBuy ? kT : kB;
            if (SwingCEdgeTakeProfitResolver.TryResolveC(
                    swingState, isBuy, swingBBar, provisionalEntry, out _, swingCCrossTf) is { } cPreview)
                cRefForEntry = cPreview.TakeProfit;
        }

        var entryState = state;
        var obFallbackState = b.Source == SwingBSource.M5AnchoredFallback && cfg.M5FallbackState is not null
            ? cfg.M5FallbackState
            : null;
        var entryPick = KeylevelObReader.ResolveEntry(
            entryState, in b, isBuy, cRefForEntry, obFallbackState, swingCCrossTfForEntry);
        var entry = entryPick.Entry;
        var usedOb = entryPick.FromOb;
        var nearBLow = entryPick.NearBZoneLow;
        var nearBHigh = entryPick.NearBZoneHigh;

        var pip = KlEntryLotCalculator.ResolvePipSize(cfg.SymbolName, cfg.TickSize, cfg.PipSize);

        var atrCfg = cfg.AtrSlWidthAdjust;
        AtrSlWidthAdjustResult slAdjust;
        double slMult;

        if (atrCfg.UseAtrAdjustedSlWidthMultiplier)
        {
            var atrTf = string.IsNullOrWhiteSpace(atrCfg.AtrAdjustTf) ? cfg.ChartTfToken : atrCfg.AtrAdjustTf.Trim();
            var atrSeries = resolveAtrSeries?.Invoke(atrTf);

            slAdjust = AtrSlWidthMultiplierCalculator.Resolve(in atrCfg, atrSeries);
            if (!slAdjust.Success)
            {
                reason = slAdjust.SkipReason ?? "ATR adjustment unavailable";
                return results;
            }

            slMult = slAdjust.DynamicSlWidthMult;
        }
        else
        {
            slMult = cfg.SlWidthMult <= 0 ? 2.0 : cfg.SlWidthMult;
            slAdjust = new AtrSlWidthAdjustResult
            {
                Success = true,
                DynamicSlWidthMult = slMult,
                DynamicSlWidthMultRaw = slMult,
                AtrRatio = 1.0,
                AtrTf = cfg.ChartTfToken,
            };
        }

        var slWidth = w * slMult;
        double sl;
        double riskDistance;
        if (cfg.SlFromEntry)
        {
            riskDistance = slWidth;
            sl = isBuy ? entry - riskDistance : entry + riskDistance;
        }
        else
        {
            // SL anchored to swing-X keylevel edge; entry may shift (OB / swing-C RR move) without moving SL.
            var slAnchor = isBuy ? kT : kB;
            sl = isBuy ? slAnchor - slWidth : slAnchor + slWidth;
            riskDistance = Math.Abs(entry - sl);
        }

        var baseMultForLog = atrCfg.UseAtrAdjustedSlWidthMultiplier
            ? (atrCfg.BaseSlWidthMult <= 0 ? 2.0 : atrCfg.BaseSlWidthMult)
            : slMult;

        bool slFloorApplied = false;
        var slFloorNote = "";

        // H4 SL mode: ghi đè base SL bằng High/Low HTF bar (H4 hoặc H1 với R1/R2) đã đóng trước/tại SlEvalTime,
        // lùi thêm 1 chiều rộng keylevel swing-B, guard p25/p75. TRƯỚC min-SL constraint.
        if (cfg.EnableH4SlMode
            && cfg.H4BreakDepthStats is { } h4Stats
            && pip > 0)
        {
            // R1/R2 (slot 0, 1): dùng H1 nếu bật; còn lại và fallback dùng H4.
            var useH1ForSlot = cfg.H4SlR1R2UseH1
                               && (ev.SlotIndex == 0 || ev.SlotIndex == 1)
                               && cfg.H1SeriesBuffer is { };
            var htfBuf    = useH1ForSlot ? cfg.H1SeriesBuffer! : cfg.H4SeriesBuffer;
            var htfPeriod = useH1ForSlot ? TimeSpan.FromHours(1) : TimeSpan.FromHours(4);

            // SlH1 hi-fallback: dùng H1 buffer (đã được load trong ApplyH4SlConfig khi cần).
            var h1ForHiFallback = cfg.H4SlHiFallbackStat == H4SlHiFallbackRef.SlH1
                ? cfg.H1SeriesBuffer
                : null;

            if (htfBuf is not null
                && H4SlResolver.TryResolve(
                    isBuy, sl, entry, htfBuf, cfg.SlEvalTime,
                    htfPeriod, in h4Stats, pip,
                    cfg.H4SlMaxLookbackBars, cfg.H4SlBandLowStat, cfg.H4SlBandHighStat,
                    w,
                    cfg.H4SlHiFallbackStat, h1ForHiFallback,
                    cfg.H4SlHiFallbackSlH1NoBarStat,
                    out var h4Sl, out var h4Reason))
            {
                sl = h4Sl;
                riskDistance = Math.Abs(entry - sl);
                slFloorNote += $" [H4SL={h4Reason}]";
            }
            else if (pip > 0)
            {
                // no-h4-bar: áp dụng cùng chiến lược hi-fallback thay vì im lặng giữ base SL.
                H4SlResolver.ApplyNoBarFallback(
                    isBuy, entry, in h4Stats, pip,
                    cfg.H4SlBandLowStat, cfg.H4SlBandHighStat,
                    cfg.H4SlHiFallbackStat, h1ForHiFallback,
                    cfg.H4SlHiFallbackSlH1NoBarStat,
                    cfg.SlEvalTime, w,
                    out var noBarSl, out var noBarReason);
                sl = noBarSl;
                riskDistance = Math.Abs(entry - sl);
                slFloorNote += $" [H4SL={noBarReason}]";
            }
        }

        var slPipsBeforeFloor = pip > 0 ? riskDistance / pip : 0;
        var minSlFloorPips = 0.0;
        if (cfg.EnableMinSlConstraint && pip > 0)
        {
            var slPipsRaw = slPipsBeforeFloor;
            var spreadFloorPips = cfg.SpreadPips * (cfg.MinSlSpreadMultiple > 0 ? cfg.MinSlSpreadMultiple : 10.0);
            var isForex = cfg.AssetType == KlAssetType.Forex;
            minSlFloorPips = isForex
                ? Math.Max(spreadFloorPips, cfg.MinSlPipsForex > 0 ? cfg.MinSlPipsForex : 5.0)
                : spreadFloorPips;

            if (minSlFloorPips > 0 && slPipsRaw < minSlFloorPips)
            {
                if (cfg.SlConstraintBacktestMode)
                {
                    riskDistance = minSlFloorPips * pip;
                    sl = isBuy ? entry - riskDistance : entry + riskDistance;
                    slFloorApplied = true;
                    slFloorNote = $" [slFloor={minSlFloorPips:0.##}p was {slPipsRaw:0.##}p]";
                }
                else
                {
                    reason = $"SL too tight: {slPipsRaw:0.##}p < min {minSlFloorPips:0.##}p (spread×{cfg.MinSlSpreadMultiple:0.#} or {cfg.MinSlPipsForex:0.#}p abs)";
                    return results;
                }
            }
        }

        var rr = cfg.RewardRisk <= 0 ? 2.0 : cfg.RewardRisk;
        var risk = Math.Abs(entry - sl);
        if (risk <= 0)
        {
            reason = "zero risk distance";
            return results;
        }

        var useSwingC = !slFloorApplied && useSwingCEntryPick;

        // Split mode with SL floor applied: the SL is no longer structure-anchored, so
        // zone-based TP legs lose their meaning. Skip rather than silently downgrade to RR.
        if (slFloorApplied
            && cfg.SwingCTpMode == SwingCTpMode.SplitTpAtCAndD
            && useSwingCEntryPick
            && !rolloverMode)
        {
            reason = $"split C/D skipped: min-SL floor applied ({slFloorNote.Trim()}) — SL no longer structure-anchored";
            return results;
        }

        // Rollover re-entry forces the split branch even if the slot/floor checks would otherwise skip it.
        var doSplit = cfg.SwingCTpMode == SwingCTpMode.SplitTpAtCAndD
                      && (useSwingC || (rolloverMode && injectedSplitC is not null));

        if (doSplit)
        {
            SwingCEdgeResult cResult;
            SwingCEdgeResult? dResult;
            string splitReason;
            SwingCEdgeResult? nearDCancelZone;

            if (rolloverMode && injectedSplitC is not null)
            {
                // Post-fill C-rollover: C and D are supplied explicitly (new M15 obstacle = C, old C = D).
                // The swing-C obstacle gate and the near-D cancel gate are intentionally NOT applied —
                // this is an adjustment of an existing trade to a freshly-formed obstacle, not a new entry.
                cResult = injectedSplitC;
                dResult = injectedSplitD;
                splitReason = "ok(rollover)";
                nearDCancelZone = null;
            }
            else
            {
                if (!SwingCEdgeTakeProfitResolver.TryResolveSplitCd(
                        swingState, isBuy, swingBBar, entry, out var cResolved, out var dResolved, out var resolveReason,
                        dZoneStates, swingCCrossTf,
                        dReferencePrice: isBuy
                            ? (double.IsNaN(fireBarHigh) ? double.NaN : fireBarHigh)
                            : (double.IsNaN(fireBarLow) ? double.NaN : fireBarLow),
                        cachedDFire: cachedDFireAtFire))
                {
                    if (cfg.SwingCEdgeTpFallbackToRR)
                    {
                        var rrTp = isBuy ? entry + rr * risk : entry - rr * risk;
                        var single = CreatePlan(
                            in ev, in cfg, b, isBuy, kT, kB, w, entry, sl, pip, rr, risk, riskDistance,
                            usedOb, slFloorApplied, slFloorNote, in slAdjust, slMult, baseMultForLog,
                            minSlFloorPips, slPipsBeforeFloor, atrCfg.AtrAdjustmentFactor,
                            rrTp, TakeProfitSource.RewardRisk, null, "", cfg.RiskPercent, $"RR={rr}",
                            out var singleReason,
                            nearBCancelZoneLow: nearBLow,
                            nearBCancelZoneHigh: nearBHigh);
                        if (single is null)
                        {
                            reason = singleReason ?? resolveReason;
                            return results;
                        }

                        results.Add(single);
                        reason = "ok";
                        return results;
                    }

                    reason = $"swing-C TP: {resolveReason}";
                    return results;
                }

                cResult = usesM5SwingC
                    ? R1R2M5Structure.AnchorEdgePivotToChart(cResolved, in cfg)
                    : cResolved;
                dResult = dResolved;
                splitReason = resolveReason;

                // Near-D cancel gate uses D locked at fire reference (HIGH/LOW), not post-C price.
                var cPivotSnap = swingState.Pivots.GetSnapshot(cResult.PivotIndex);
                var nearDRef = isBuy
                    ? (double.IsNaN(fireBarHigh) ? cPivotSnap.Price : fireBarHigh)
                    : (double.IsNaN(fireBarLow) ? cPivotSnap.Price : fireBarLow);
                nearDCancelZone = SwingCEdgeTakeProfitResolver.ResolveNearDCancelZone(
                    dResult, nearDRef, isBuy, entry, cfg.NearDZoneFallbackStates);

                // Gate: skip if swing-C zone overlaps a same-color zone (resistance on resistance / support on support).
                if (cfg.EnableSwingCZoneObstacleGate
                    && cfg.SwingCZoneObstacleStates is { Count: > 0 } cObstacleStates)
                {
                    var cObs = SwingCZoneObstacleGate.Evaluate(
                        cResult, isBuy, cfg.ChartTfToken, cObstacleStates, pip, cfg.ChartSeriesBuffer,
                        // Daily fallback only when no D zone found in M5/M15/H1/H4 — meaning the zone landscape
                        // after C is "sparse" on shorter TFs. When dResult exists, the system already has a
                        // confirmed zone beyond C; Daily is redundant.
                        fallbackStates: dResult is null ? cfg.SwingCZoneObstacleFallbackStates : null,
                        tfBuffers: cfg.SwingCZoneObstacleTfBuffers);
                    if (cObs.HasObstacle)
                    {
                        reason = $"swing-C zone obstacle: C [{cResult.EdgeBottom:0.#####}..{cResult.EdgeTop:0.#####}] " +
                                 $"overlaps {cObs.ObstacleTf} {cObs.ObstacleSource} [{cObs.ObstacleLow:0.#####}..{cObs.ObstacleHigh:0.#####}] " +
                                 $"{cObs.OverlapPips:0.#}p selfSkipped={cObs.SelfIdentitySkipped}" +
                                 (cObs.IsFallback ? " (fallback-daily)" : "");
                        return results;
                    }
                }
            }

            // Gate / move / floor on the C edge. Entry-move (split only) takes priority over skip:
            // move entry to make C-RR == base; if unreachable within the keylevel → no orders at all.
            var cEdgeTp = cResult.TakeProfit;
            double tpC;
            if (SwingCEdgeTakeProfitResolver.IsEdgeRrBelowBase(cEdgeTp, entry, sl, isBuy, rr))
            {
                // H4 SL RR guard (2-pass): takes priority over MoveEntryIfSwingCRrBelowBase when active.
                if (cfg.EnableH4SlMode && cfg.H4SlRrGuard)
                {
                    if (SwingCEdgeTakeProfitResolver.TryComputeBaseRrEntry(
                            cEdgeTp, sl, isBuy, rr, entry, kT, kB, out var movedEntry, out _))
                    {
                        // Pass 1 OK: entry moved inside keylevel to achieve base RR.
                        entry = movedEntry;
                        risk = Math.Abs(entry - sl);
                        riskDistance = risk;
                        slPipsBeforeFloor = pip > 0 ? risk / pip : 0;
                        if (cfg.EnableMinSlConstraint && pip > 0 && minSlFloorPips > 0
                            && slPipsBeforeFloor < minSlFloorPips)
                        {
                            reason = $"[H4RR-ADJ] entry moved but SL too tight: {slPipsBeforeFloor:0.##}p < min {minSlFloorPips:0.##}p";
                            return results;
                        }
                        slFloorNote += " [H4RR-ADJ]";
                        tpC = cEdgeTp;
                    }
                    else if (cfg.H4BreakDepthStats is { Mean: > 0 } gStatsS && pip > 0)
                    {
                        // Pass 1 failed → Pass 2: rollback entry to KL edge, use SL = mean.
                        if (!TryH4RrMeanFallback(
                                cEdgeTp, isBuy, kT, kB, in gStatsS, pip, rr,
                                out entry, out sl, out var p2Tag))
                        {
                            var rrFail = SwingCEdgeTakeProfitResolver.ComputeEdgeRr(cEdgeTp, entry, sl, isBuy);
                            reason = $"H4SL RR guard [{p2Tag}] rrEdge={rrFail:0.##} < base {rr:0.##}";
                            return results;
                        }
                        risk = Math.Abs(entry - sl);
                        riskDistance = risk;
                        slPipsBeforeFloor = pip > 0 ? risk / pip : 0;
                        if (cfg.EnableMinSlConstraint && pip > 0 && minSlFloorPips > 0
                            && slPipsBeforeFloor < minSlFloorPips)
                        {
                            reason = $"H4SL RR guard [{p2Tag}] but SL too tight: {slPipsBeforeFloor:0.##}p < min {minSlFloorPips:0.##}p";
                            return results;
                        }
                        slFloorNote += $" [{p2Tag}]";
                        tpC = cEdgeTp;
                    }
                    else
                    {
                        var rrFail2 = SwingCEdgeTakeProfitResolver.ComputeEdgeRr(cEdgeTp, entry, sl, isBuy);
                        reason = $"H4SL RR guard [H4RR-SKIP] no mean stats rrEdge={rrFail2:0.##} < base {rr:0.##}";
                        return results;
                    }
                }
                else if (cfg.MoveEntryIfSwingCRrBelowBase)
                {
                    if (!SwingCEdgeTakeProfitResolver.TryComputeBaseRrEntry(
                            cEdgeTp, sl, isBuy, rr, entry, kT, kB, out var movedEntry, out var moveReason))
                    {
                        reason = $"swing-C RR below base; entry move not possible: {moveReason}";
                        return results;
                    }

                    entry = movedEntry;
                    risk = Math.Abs(entry - sl);
                    riskDistance = risk;
                    slPipsBeforeFloor = pip > 0 ? risk / pip : 0;

                    // After moving, the stop distance shrinks: reject if it no longer clears the
                    // min-SL floor (other gates are enforced later by the bot on the moved plan).
                    if (cfg.EnableMinSlConstraint && pip > 0 && minSlFloorPips > 0
                        && slPipsBeforeFloor < minSlFloorPips)
                    {
                        reason = $"entry moved but SL too tight: {slPipsBeforeFloor:0.##}p < min {minSlFloorPips:0.##}p";
                        return results;
                    }

                    tpC = cEdgeTp;
                }
                else if (cfg.SkipIfSwingCRrBelowBase)
                {
                    var rrEdge = SwingCEdgeTakeProfitResolver.ComputeEdgeRr(cEdgeTp, entry, sl, isBuy);
                    reason = $"swing-C RR {rrEdge:0.##} < base {rr:0.##} (edge={cEdgeTp:0.#####})";
                    return results;
                }
                else
                {
                    // Split mode: pushing TP C past the zone boundary defeats zone-based TP.
                    // Skip instead of applying ApplyMinRrFloor (which is for single-leg mode only).
                    var rrEdgeSplit = SwingCEdgeTakeProfitResolver.ComputeEdgeRr(cEdgeTp, entry, sl, isBuy);
                    reason = $"split leg C RR {rrEdgeSplit:0.##} < base {rr:0.##} (edge={cEdgeTp:0.#####}); enable MoveEntry or Skip param to handle this";
                    return results;
                }
            }
            else
            {
                tpC = cEdgeTp;
            }

            var planC = CreatePlan(
                in ev, in cfg, b, isBuy, kT, kB, w, entry, sl, pip, rr, risk, riskDistance,
                usedOb, slFloorApplied, slFloorNote, in slAdjust, slMult, baseMultForLog,
                minSlFloorPips, slPipsBeforeFloor, atrCfg.AtrAdjustmentFactor,
                tpC, TakeProfitSource.SwingCEdge, cResult, "C",
                cfg.RiskPercent * SplitLegRiskFraction, "SwingC",
                out var cFail,
                nearDCancelZone: nearDCancelZone,
                tpCLegPrice: tpC,
                nearBCancelZoneLow: nearBLow,
                nearBCancelZoneHigh: nearBHigh);
            if (planC is null)
            {
                reason = cFail ?? "split leg C invalid";
                return results;
            }

            results.Add(planC);

            double tpD;
            TakeProfitSource dSource;
            SwingCEdgeResult? dMeta;
            string dNote;
            var dForTp = SelectDMetaForTp(dResult, cachedDPrimeAtFire, ev.SlotIndex, in cfg,
                rolloverMode, swingState, cResult, isBuy, entry,
                isBuy ? (double.IsNaN(fireBarHigh) ? double.NaN : fireBarHigh)
                      : (double.IsNaN(fireBarLow) ? double.NaN : fireBarLow));
            if (dForTp is not null)
            {
                var dEdgeRaw = dForTp.TakeProfit;
                // nearDCancelZone (resolved above from dResult) is unchanged — gate still uses real D zone.
                if (cfg.SplitDLegMinIncrementalRr > 0
                    && SwingCEdgeTakeProfitResolver.ShouldFallbackDLegTpToC(
                        tpC, dEdgeRaw, entry, sl, isBuy, cfg.SplitDLegMinIncrementalRr))
                {
                    tpD = tpC;
                    dSource = TakeProfitSource.SwingCEdge;
                    dMeta = dForTp;
                    dNote = "SwingC-fallback(D-near-C)";
                }
                else if (SwingCEdgeTakeProfitResolver.IsEdgeRrBelowBase(dEdgeRaw, entry, sl, isBuy, rr))
                {
                    // D zone edge does not reach base RR — fall back to TP C rather than
                    // pushing TP D past the zone boundary via ApplyMinRrFloor.
                    tpD = tpC;
                    dSource = TakeProfitSource.SwingCEdge;
                    dMeta = dForTp;
                    dNote = "SwingC-fallback(D-rr-below-base)";
                }
                else
                {
                    tpD = dEdgeRaw;
                    dSource = TakeProfitSource.SwingCEdge;
                    dMeta = dForTp;
                    dNote = IsDPrimeMeta(dForTp) ? "SwingD-prime" : "SwingD";
                }
            }
            else
            {
                tpD = tpC;
                dSource = TakeProfitSource.SwingCEdge;
                dMeta = cResult;
                dNote = "SwingC-fallback(no-D)";
            }

            var planD = CreatePlan(
                in ev, in cfg, b, isBuy, kT, kB, w, entry, sl, pip, rr, risk, riskDistance,
                usedOb, slFloorApplied, slFloorNote, in slAdjust, slMult, baseMultForLog,
                minSlFloorPips, slPipsBeforeFloor, atrCfg.AtrAdjustmentFactor,
                tpD, dSource, dMeta, "D",
                cfg.RiskPercent * SplitLegRiskFraction, dNote,
                out var dFail,
                nearDCancelZone: nearDCancelZone,
                tpCLegPrice: tpC,
                nearBCancelZoneLow: nearBLow,
                nearBCancelZoneHigh: nearBHigh,
                swingCEntryPivotBar: cResult.PivotBar);
            if (planD is null)
            {
                reason = dFail ?? "split leg D invalid";
                return results;
            }

            results.Add(planD);
            reason = splitReason;
            return results;
        }

        var tpSource = TakeProfitSource.RewardRisk;
        SwingCEdgeResult? swingC = null;
        double tp;

        if (useSwingC)
        {
            if (cfg.SkipIfSwingCRrBelowBase
                && SwingCEdgeTakeProfitResolver.TryResolveC(
                    swingState, isBuy, swingBBar, entry, out _, swingCCrossTf) is { } cGate
                && SwingCEdgeTakeProfitResolver.IsEdgeRrBelowBase(cGate.TakeProfit, entry, sl, isBuy, rr))
            {
                var rrEdge = SwingCEdgeTakeProfitResolver.ComputeEdgeRr(cGate.TakeProfit, entry, sl, isBuy);
                reason = $"swing-C RR {rrEdge:0.##} < base {rr:0.##} (edge={cGate.TakeProfit:0.#####})";
                return results;
            }

            swingC = SwingCEdgeTakeProfitResolver.TryResolve(
                swingState, isBuy, swingBBar, entry, out var cReason, cfg.SwingCTpMode, swingCCrossTf);
            if (swingC is not null && usesM5SwingC)
                swingC = R1R2M5Structure.AnchorEdgePivotToChart(swingC, in cfg);
            if (swingC is not null)
            {
                // Gate: skip if swing-C zone overlaps a same-color zone.
                if (cfg.EnableSwingCZoneObstacleGate
                    && cfg.SwingCZoneObstacleStates is { Count: > 0 } cObsStates)
                {
                    var cObs = SwingCZoneObstacleGate.Evaluate(
                        swingC, isBuy, cfg.ChartTfToken, cObsStates, pip, cfg.ChartSeriesBuffer,
                        // Non-split mode has no D concept → no Daily fallback.
                        fallbackStates: null,
                        tfBuffers: cfg.SwingCZoneObstacleTfBuffers);
                    if (cObs.HasObstacle)
                    {
                        reason = $"swing-C zone obstacle: C [{swingC.EdgeBottom:0.#####}..{swingC.EdgeTop:0.#####}] " +
                                 $"overlaps {cObs.ObstacleTf} {cObs.ObstacleSource} [{cObs.ObstacleLow:0.#####}..{cObs.ObstacleHigh:0.#####}] " +
                                 $"{cObs.OverlapPips:0.#}p selfSkipped={cObs.SelfIdentitySkipped}" +
                                 (cObs.IsFallback ? " (fallback-daily)" : "");
                        return results;
                    }
                }

                var skipFloor = cfg.SkipIfSwingCRrBelowBase
                                && cfg.SwingCTpMode != SwingCTpMode.NextKeyLevelAfterC;
                if (!SwingCEdgeTakeProfitResolver.TryResolveSwingEdgeTp(
                        swingC.TakeProfit, entry, sl, isBuy, rr, skipFloor,
                        out tp, out var tpReject))
                {
                    // H4 SL RR guard (non-split): 2-pass fallback when skipFloor rejected the TP.
                    if (cfg.EnableH4SlMode && cfg.H4SlRrGuard && skipFloor)
                    {
                        if (SwingCEdgeTakeProfitResolver.TryComputeBaseRrEntry(
                                swingC.TakeProfit, sl, isBuy, rr, entry, kT, kB, out var movedEntryNs, out _))
                        {
                            // Pass 1 OK: adjust entry.
                            entry = movedEntryNs;
                            risk = Math.Abs(entry - sl);
                            riskDistance = risk;
                            slPipsBeforeFloor = pip > 0 ? risk / pip : 0;
                            if (cfg.EnableMinSlConstraint && pip > 0 && minSlFloorPips > 0
                                && slPipsBeforeFloor < minSlFloorPips)
                            {
                                reason = $"[H4RR-ADJ] non-split entry moved but SL too tight: {slPipsBeforeFloor:0.##}p < min {minSlFloorPips:0.##}p";
                                return results;
                            }
                            slFloorNote += " [H4RR-ADJ]";
                            tp = swingC.TakeProfit;
                            tpSource = TakeProfitSource.SwingCEdge;
                        }
                        else if (cfg.H4BreakDepthStats is { Mean: > 0 } gStatsNs && pip > 0)
                        {
                            // Pass 1 failed → Pass 2: rollback + mean SL.
                            if (!TryH4RrMeanFallback(
                                    swingC.TakeProfit, isBuy, kT, kB, in gStatsNs, pip, rr,
                                    out entry, out sl, out var p2TagNs))
                            {
                                var rrFail = SwingCEdgeTakeProfitResolver.ComputeEdgeRr(swingC.TakeProfit, entry, sl, isBuy);
                                reason = $"H4SL RR guard non-split [{p2TagNs}] rrEdge={rrFail:0.##} < base {rr:0.##}";
                                return results;
                            }
                            risk = Math.Abs(entry - sl);
                            riskDistance = risk;
                            slPipsBeforeFloor = pip > 0 ? risk / pip : 0;
                            if (cfg.EnableMinSlConstraint && pip > 0 && minSlFloorPips > 0
                                && slPipsBeforeFloor < minSlFloorPips)
                            {
                                reason = $"H4SL RR guard non-split [{p2TagNs}] but SL too tight: {slPipsBeforeFloor:0.##}p < min {minSlFloorPips:0.##}p";
                                return results;
                            }
                            slFloorNote += $" [{p2TagNs}]";
                            tp = swingC.TakeProfit;
                            tpSource = TakeProfitSource.SwingCEdge;
                        }
                        else
                        {
                            var rrFail2 = SwingCEdgeTakeProfitResolver.ComputeEdgeRr(swingC.TakeProfit, entry, sl, isBuy);
                            reason = $"H4SL RR guard non-split [H4RR-SKIP] no mean stats rrEdge={rrFail2:0.##} < base {rr:0.##}";
                            return results;
                        }
                    }
                    else
                    {
                        reason = tpReject!;
                        return results;
                    }
                }
                else
                {
                    tpSource = TakeProfitSource.SwingCEdge;
                }
            }
            else if (cfg.SwingCEdgeTpFallbackToRR)
            {
                tp = isBuy ? entry + rr * risk : entry - rr * risk;
            }
            else
            {
                reason = $"swing-C TP: {cReason}";
                return results;
            }
        }
        else
        {
            tp = isBuy ? entry + rr * risk : entry - rr * risk;
        }

        var plan = CreatePlan(
            in ev, in cfg, b, isBuy, kT, kB, w, entry, sl, pip, rr, risk, riskDistance,
            usedOb, slFloorApplied, slFloorNote, in slAdjust, slMult, baseMultForLog,
            minSlFloorPips, slPipsBeforeFloor, atrCfg.AtrAdjustmentFactor,
            tp, tpSource, swingC, "",
            cfg.RiskPercent,
            tpSource == TakeProfitSource.SwingCEdge ? "SwingC" : $"RR={rr}",
            out var failReason,
            nearBCancelZoneLow: nearBLow,
            nearBCancelZoneHigh: nearBHigh);
        if (plan is null)
        {
            reason = failReason ?? "plan invalid";
            return results;
        }

        results.Add(plan);
        reason = "ok";
        return results;
    }

    static TradePlan? CreatePlan(
        in CompoundFireEvent ev,
        in TradeMapperConfig cfg,
        SwingBResult b,
        bool isBuy,
        double kT,
        double kB,
        double w,
        double entry,
        double sl,
        double pip,
        double rr,
        double risk,
        double riskDistance,
        bool usedOb,
        bool slFloorApplied,
        string slFloorNote,
        in AtrSlWidthAdjustResult slAdjust,
        double slMult,
        double baseMultForLog,
        double minSlFloorPips,
        double slPipsBeforeFloor,
        double atrAdjustFactor,
        double tpBid,
        TakeProfitSource tpSource,
        SwingCEdgeResult? swingMeta,
        string tpLegTag,
        double riskPercentForLeg,
        string tpNote,
        out string? failReason,
        SwingCEdgeResult? nearDCancelZone = null,
        double tpCLegPrice = 0,
        double nearBCancelZoneLow = 0,
        double nearBCancelZoneHigh = 0,
        int swingCEntryPivotBar = 0)
    {
        failReason = null;
        var tp = tpBid;
        var entryForCheck = entry;
        var slForCheck = sl;

        var consistent = isBuy
            ? (slForCheck < entryForCheck && entryForCheck < tp)
            : (tp < entryForCheck && entryForCheck < slForCheck);
        if (!consistent)
        {
            failReason = $"inconsistent levels entry={entryForCheck} sl={slForCheck} tp={tp} leg={tpLegTag} (buy={isBuy})";
            return null;
        }

        var rawEntryBid = entry;
        var rawSlBid = sl;
        var rawTpBid = tp;
        double tp1 = tp, tp2 = 0;

        if (cfg.RecalcTpAfterSpread)
        {
            KlEntryLotCalculator.ForwardSpreadOnBidLevels(ref entry, ref sl, ref tp1, ref tp2, cfg.SpreadPips, pip, isBuy);
            tp = tp1;
            if (tpSource == TakeProfitSource.RewardRisk)
            {
                var riskAfterSpread = Math.Abs(entry - sl);
                tp = isBuy ? entry + rr * riskAfterSpread : entry - rr * riskAfterSpread;
            }
        }

        var entryShiftBySpread = entry - rawEntryBid;
        var slShiftBySpread = sl - rawSlBid;
        var tpShiftBySpread = tp - rawTpBid;

        var klInput = new KlEntryLotInput
        {
            AssetType = cfg.AssetType,
            CustomContractSize = cfg.CustomContractSize <= 0 ? 100_000 : cfg.CustomContractSize,
            AccountBalance = cfg.AccountBalanceFtmo,
            AccountBalanceFtmo = cfg.AccountBalanceFtmo,
            RiskPercent = riskPercentForLeg,
            ManualConvUsdPerQuote = cfg.ConvUsdPerQuote,
            EntryPrice = entry,
            StopPrice = sl,
            Tp1 = tp,
            Tp2 = 0,
            SpreadPips = cfg.SpreadPips,
            RoundPrecision = cfg.RoundPrecision <= 0 ? 1000 : cfg.RoundPrecision,
            SymbolName = cfg.SymbolName,
            TickSize = cfg.TickSize,
            PipSize = cfg.PipSize,
            IsCryptoSymbol = cfg.IsCryptoSymbol,
            QuoteCurrency = string.IsNullOrWhiteSpace(cfg.QuoteCurrency) ? "USD" : cfg.QuoteCurrency,
            BaseAssetName = cfg.BaseAssetName ?? "",
            AutoConvToUsd = cfg.ConvUsdPerQuote > 0 ? cfg.ConvUsdPerQuote : (double?)null,
        };

        var kl = KlEntryLotCalculator.Compute(in klInput);

        var slPips = Math.Abs(entry - sl) / pip;
        var tpPips = Math.Abs(entry - tp) / pip;

        var legSuffix = string.IsNullOrEmpty(tpLegTag) ? "" : $"|{tpLegTag}";
        var label = $"{cfg.LabelPrefix}R{ev.SlotIndex + 1}|B{b.PivotBar}{legSuffix}";
        var obNote = usedOb ? "OB" : "KL";
        var legNote = string.IsNullOrEmpty(tpLegTag) ? "" : $" leg={tpLegTag} riskPct={riskPercentForLeg:0.###}%";
        var usesM5SwingCPlan = R1R2M5Structure.UsesM5SwingC(ev.SlotIndex, in cfg);
        var swingBFromM5 = b.Source == SwingBSource.M5AnchoredFallback;
        var structureSwingBBar = swingBFromM5
            ? b.StructurePivotBar
            : usesM5SwingCPlan
                ? R1R2M5Structure.ResolveM5SwingBBarForC(in b, in cfg)
                : 0;
        return new TradePlan
        {
            SlotIndex = ev.SlotIndex,
            RuleName = ev.RuleName,
            Direction = ev.Direction,
            IsBuy = isBuy,
            EntryLimit = entry,
            StopLoss = sl,
            TakeProfit = tp,
            StopLossPips = slPips,
            TakeProfitPips = tpPips,
            LotFtmo = kl.RoundDisplayedFtmo is > 0 ? kl.RoundDisplayedFtmo : null,
            DedupKey = new TradeDedupKey(ev.SlotIndex, b.PivotBar, tpLegTag),
            SwingBPivotIndex = b.PivotIndex,
            SwingBPivotBar = b.PivotBar,
            SwingBTfToken = string.IsNullOrWhiteSpace(cfg.ChartTfToken) ? "15" : cfg.ChartTfToken,
            UsesM5SwingC = usesM5SwingCPlan,
            SwingBFromM5Anchor = swingBFromM5,
            StructureSwingBBar = structureSwingBBar,
            SwingBKeyLow = kB,
            SwingBKeyHigh = kT,
            SwingBKeyIsOb = usedOb,
            TpCLegPrice = tpCLegPrice,
            SwingCPivotIndex = swingMeta?.PivotIndex ?? 0,
            SwingCPivotBar = swingMeta?.PivotBar ?? 0,
            SwingCEntryPivotBar = swingCEntryPivotBar > 0 ? swingCEntryPivotBar : swingMeta?.PivotBar ?? 0,
            SwingCEdgeTop = swingMeta?.EdgeTop ?? 0,
            SwingCEdgeBottom = swingMeta?.EdgeBottom ?? 0,
            TakeProfitSource = tpSource,
            OriginalTakeProfit = tp,
            KeyWidth = w,
            DynamicSlWidthMult = slMult,
            SlAdjust = slAdjust,
            SlBaseMult = baseMultForLog,
            SlAdjustFactor = atrAdjustFactor,
            SlRiskDistance = riskDistance,
            SpreadPipsAtPlan = cfg.SpreadPips,
            SpreadSourceAtPlan = string.IsNullOrWhiteSpace(cfg.SpreadSource)
                ? SpreadTelemetrySources.Symbol
                : cfg.SpreadSource,
            MinSlFloorPips = minSlFloorPips,
            SlPipsBeforeFloor = slPipsBeforeFloor,
            SlFloorApplied = slFloorApplied,
            RawEntryBid = rawEntryBid,
            RawSlBid = rawSlBid,
            RawTpBid = rawTpBid,
            EntryShiftBySpread = entryShiftBySpread,
            SlShiftBySpread = slShiftBySpread,
            TpShiftBySpread = tpShiftBySpread,
            Label = label,
            TpLegTag = tpLegTag,
            NearDCancelZoneHigh = nearDCancelZone?.EdgeTop ?? 0,
            NearDCancelZoneLow = nearDCancelZone?.EdgeBottom ?? 0,
            NearDCancelZoneIsOb = nearDCancelZone?.IsOb ?? false,
            NearDCancelZoneTfToken = nearDCancelZone?.TfToken ?? "",
            NearBCancelZoneLow = nearBCancelZoneLow,
            NearBCancelZoneHigh = nearBCancelZoneHigh,
            NearBCancelZoneIsOb = usedOb,
            Reason = $"{(isBuy ? "BUY" : "SELL")} entry({obNote})={entry:0.#####} sl={sl:0.#####} tp={tp:0.#####} w={w:0.#####} {tpNote}{legNote} lotFtmo={kl.RoundDisplayedFtmo}{FormatSwingBSourceTag(b)}{slFloorNote}",
        };
    }

    static string FormatSwingBSourceTag(SwingBResult b) => b.Source switch
    {
        SwingBSource.MainCFallback => " [B=MAIN_C]",
        SwingBSource.DSwing => " [B=D_SWING]",
        SwingBSource.M5AnchoredFallback when b.M5AnchoredMainC => " [B=M5→M15,MAIN_C]",
        SwingBSource.M5AnchoredFallback when b.M5AnchoredDSwing => " [B=M5→M15,D_SWING]",
        SwingBSource.M5AnchoredFallback => " [B=M5→M15]",
        _ => "",
    };

    /// <summary>R1/R2 on M15: swing B on chart TF first; M5 anchored fallback when enabled and M15 has no B.</summary>
    public static SwingBResult? ResolveSwingB(
        PineStateEngine chartState,
        bool isBuy,
        int slotIndex,
        in TradeMapperConfig cfg,
        out string reason)
    {
        var allowMainCFallback = IsNgM15Slot(slotIndex);
        var b = SwingBResolver.Resolve(chartState, isBuy, allowMainCFallback, out reason);
        if (b is not null)
            return b;

        if (!cfg.EnableM5SwingBFallback
            || !SwingBResolver.IsR1R2Slot(slotIndex)
            || !string.Equals(cfg.ChartTfToken, "15", StringComparison.Ordinal)
            || cfg.M5FallbackState is null)
            return null;

        return SwingBResolver.TryResolveM5AnchoredFallback(
            cfg.M5FallbackState, isBuy, cfg.M5FallbackBuffer, cfg.ChartSeriesBuffer, cfg.ChartTfToken, out reason);
    }

    /// <summary>0-based slots for R5 (BUY NG M15) and R6 (SELL NG M15) — they may fall back to MAIN_C
    /// when no ACTIVE swing of the required direction exists at the fire bar.</summary>
    static bool IsNgM15Slot(int slotIndex) => slotIndex == 4 || slotIndex == 5;

    /// <summary>
    /// Scan D once at the fire bar (HIGH for BUY / LOW for SELL). Cached on deferred fires and
    /// passed back into <see cref="TryBuildPlans"/> when C confirms.
    /// </summary>
    public static DFireAtFireCache ResolveDFireAtFire(
        PineStateEngine state,
        in TradeMapperConfig cfg,
        in CompoundFireEvent ev,
        SwingBResult b,
        double fireBarHigh,
        double fireBarLow)
    {
        var isBuy = ev.Direction == SignalDirection.Buy;
        var usesM5SwingC = R1R2M5Structure.UsesM5SwingC(ev.SlotIndex, in cfg);
        var swingState = usesM5SwingC ? cfg.M5FallbackState! : state;
        var swingBBar = usesM5SwingC
            ? R1R2M5Structure.ResolveM5SwingBBarForC(in b, in cfg)
            : b.PivotBar;
        var refPrice = isBuy ? fireBarHigh : fireBarLow;
        var entry = isBuy ? b.KeyTop : b.KeyBottom;
        var dFire = SwingCEdgeTakeProfitResolver.ResolveDFireZone(
            cfg.DZoneStates, swingState, swingBBar, refPrice, isBuy, entry);
        var nearD = SwingCEdgeTakeProfitResolver.ResolveNearDCancelZone(
            dFire, refPrice, isBuy, entry, cfg.NearDZoneFallbackStates);
        SwingCEdgeResult? dPrime = null;
        if (cfg.EnableGongLoiTpD && UsesGongLoiTpD(ev.SlotIndex))
        {
            dPrime = SwingCEdgeTakeProfitResolver.ResolveDPrimeFireZone(
                cfg.DPrimeZoneStates, swingState, swingBBar, refPrice, isBuy, entry);
        }
        return new DFireAtFireCache(dFire, refPrice, nearD, dPrime);
    }

    /// <summary>R3–R6 slots (0-based 2–5) use gồng lời D' for TP when enabled.</summary>
    public static bool UsesGongLoiTpD(int slotIndex) => slotIndex is >= 2 and <= 5;

    static bool IsDPrimeMeta(SwingCEdgeResult meta) =>
        string.Equals(meta.EdgeKind, "DPrimeZone", StringComparison.Ordinal)
        || string.Equals(meta.EdgeKind, "DPrimeKeyBox", StringComparison.Ordinal);

    /// <summary>
    /// Pick zone metadata for TP leg D: D' on R3–R6 when found, else D. Near-D cancel always uses D separately.
    /// </summary>
    static SwingCEdgeResult? SelectDMetaForTp(
        SwingCEdgeResult? dResult,
        SwingCEdgeResult? cachedDPrime,
        int slotIndex,
        in TradeMapperConfig cfg,
        bool rolloverMode,
        PineStateEngine swingState,
        SwingCEdgeResult cResult,
        bool isBuy,
        double entry,
        double fireRefPrice)
    {
        if (dResult is null)
            return null;

        if (rolloverMode || !cfg.EnableGongLoiTpD || !UsesGongLoiTpD(slotIndex))
            return dResult;

        var dPrime = cachedDPrime;
        if (dPrime is null)
        {
            var cBar = cResult.PivotBar;
            var refPrice = double.IsNaN(fireRefPrice) ? cResult.TakeProfit : fireRefPrice;
            dPrime = SwingCEdgeTakeProfitResolver.ResolveDPrimeFireZone(
                cfg.DPrimeZoneStates, swingState, cBar, refPrice, isBuy, entry);
        }

        return dPrime ?? dResult;
    }

    /// <summary>
    /// H4 SL RR guard — Pass 2: rollback <paramref name="entry"/> về mép keylevel ban đầu,
    /// dùng SL = mean depth của break-depth stats. Thử đủ RR:
    /// <list type="bullet">
    ///   <item>Không cần adjust entry → <c>[H4RR-MEAN-OK]</c></item>
    ///   <item>Cần adjust entry trong keylevel → <c>[H4RR-MEAN-ADJ]</c></item>
    ///   <item>Vẫn không đủ → <c>[H4RR-SKIP]</c>, trả <c>false</c></item>
    /// </list>
    /// </summary>
    static bool TryH4RrMeanFallback(
        double tpTarget,
        bool isBuy,
        double kT,
        double kB,
        in BreakDepthStats stats,
        double pip,
        double rr,
        out double entry,
        out double sl,
        out string tag)
    {
        entry = isBuy ? kT : kB;
        sl    = isBuy ? entry - stats.Mean * pip : entry + stats.Mean * pip;

        if (!SwingCEdgeTakeProfitResolver.IsEdgeRrBelowBase(tpTarget, entry, sl, isBuy, rr))
        {
            tag = "H4RR-MEAN-OK";
            return true;
        }

        if (SwingCEdgeTakeProfitResolver.TryComputeBaseRrEntry(
                tpTarget, sl, isBuy, rr, entry, kT, kB, out var movedEntry, out _))
        {
            entry = movedEntry;
            tag   = "H4RR-MEAN-ADJ";
            return true;
        }

        tag = "H4RR-SKIP";
        return false;
    }
}
