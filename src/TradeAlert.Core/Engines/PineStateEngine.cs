using System;
using System.Collections.Generic;
using TradeAlert.Core.Drawing;
using TradeAlert.Core.Models;
using TradeAlert.Core.Models.Alerts;
using TradeAlert.Core.Models.KeyLevel;
using TradeAlert.Core.Series;

namespace TradeAlert.Core.Engines;

/// <summary>
/// Orchestrator — ghép PivotDetector + PivotTransitionEngine + RealZoneEngine + OBEngine
/// thành 1 hệ trạng thái Pine. Mỗi bar:
///   1) Snapshot prev flags
///   2) Pivot transition tick (process_break R1)
///   3) Pivot detector — push pivot mới nếu A đủ điều kiện
///   4) RealZone Tick
///   5) OB Engine Tick (lifecycle)
///
/// Sau Tick, host gọi <see cref="BuildPivotEntries"/>, <see cref="GetRealtimeFilterState"/>,
/// <see cref="GetZoneState"/> để build AlertEvaluationContext.
/// </summary>
public sealed class PineStateEngine
{
    public PivotStateStore        Pivots          { get; } = new();
    public ObPoolStore            ObPool          { get; } = new();
    public LtfRingBufferStore     LtfBuffer       { get; }
    public PivotDetector          PivotDetector   { get; } = new();
    public PivotTransitionEngine  Transitions     { get; } = new();
    public RealZoneEngine         RealZone        { get; } = new();
    public MicroSwingEngine       MicroSwing      { get; } = new();
    public PullbackFilterEngine   Filter          { get; } = new();
    readonly SwingPeakMissTracker _peakMissTracker = new();

    public double TickSize { get; set; } = 0.00001;  // default 5-digit forex

    // === Pine-calibrated parameters (Pine line 80 / 116-117 / 115 / 20) ===
    /// <summary>dojiBodyRatioMax_structure = 0.10 (hardcoded Pine line 80). NOT to be confused with
    /// A4 dojiBodyMaxRatio=0.25 which is for the DojiForce rule only.</summary>
    public double DojiBodyRatioMaxStructure { get; set; } = 0.10;
    /// <summary>keylevelAvgBodyLen = 10 (Pine "Average body length" setting, default 10).</summary>
    public double KeylevelAvgBodyLen { get; set; } = 10.0;
    /// <summary>keylevelMinBodyMult = 1.5 (Pine "Min body multiplier (Algo)", default 1.5).</summary>
    public double KeylevelMinBodyMult { get; set; } = 1.5;
    /// <summary>keylevelAtrMult = 0.2 (Pine "ATR multiplier (Algo)") — used in ATR-fallback clear-body check.</summary>
    public double KeylevelAtrMult { get; set; } = 0.2;
    /// <summary>keylevelAtrLen = 14 (Pine "Độ dài ATR") — ATR period for clear-body fallback.</summary>
    public int    KeylevelAtrLen  { get; set; } = 14;
    /// <summary>Pine <c>maxKeylevelKeep</c> default 2.</summary>
    public int    MaxKeylevelKeep { get; set; } = 2;
    /// <summary>Pine <c>enableKeylevel</c>.</summary>
    public bool   EnableKeylevel { get; set; } = true;
    /// <summary>Pine <c>keylevelLookback</c> default 2.</summary>
    public int    KeylevelLookback { get; set; } = 2;
    /// <summary>Pine <c>keylevelUseAtrRule</c>.</summary>
    public bool   KeylevelUseAtrRule { get; set; } = true;
    /// <summary>Pine <c>breakR3MaxK</c> default 3.</summary>
    public int    BreakR3MaxK { get; set; } = 3;
    /// <summary>Pine <c>useGapMerge</c>.</summary>
    public bool   UseGapMerge { get; set; } = true;
    /// <summary>Pine <c>gapFilterAtr</c> default 0.5.</summary>
    public double GapFilterAtr { get; set; } = 0.5;
    /// <summary>Pine <c>strongAtrMult</c> default 0.6 (tier / filter).</summary>
    public double StrongAtrMult { get; set; } = 0.6;
    /// <summary>Pine <c>mediumAtrMult</c> default 0.3.</summary>
    public double MediumAtrMult { get; set; } = 0.3;
    /// <summary>Pine <c>maxDojiBodyRatio</c> OB gate default 0.15.</summary>
    public double MaxDojiBodyRatio { get; set; } = 0.15;
    /// <summary>Pine <c>inpShowKeyBorder</c>.</summary>
    public bool   ShowKeyBorder { get; set; } = true;
    /// <summary>Pine <c>inpKeyBorderWidth</c>.</summary>
    public int    KeyBorderWidth { get; set; } = 1;

    /// <summary>
    /// When true (default) use PullbackFilterEngine (useNewPullbackFilter=true).
    /// Set to false to use PivotDetector.TryPush bypass — useful for unit tests
    /// that test single-bar pivot logic without waiting for filter confirmation.
    /// </summary>
    public bool UsePullbackFilter { get; set; } = true;

    // Pine Filters group (lines 40–56)
    public bool   UseWickNoiseFilter  { get; set; } = true;
    public int    WickAvgLen          { get; set; } = 10;
    public bool   UseLtfWickConfirm   { get; set; } = true;
    public double LtfConfirmRatio     { get; set; } = 0.5;
    public bool   UseMicroSwingRule   { get; set; } = true;

    // Visual / OB (Pine defaults — opacity reduced for clearer boxes)
    public bool   ShowObBox              { get; set; } = true;
    public bool   HideObLoseZin          { get; set; } = true;
    public bool   HideObZin              { get; set; } = false;
    public int    ObScanBars             { get; set; } = 50;
    public int    MaxObs                 { get; set; } = 20;
    public double MinObBodyRatio         { get; set; } = 0.35;
    public int    LookbackBars           { get; set; } = 200;

    /// <summary>Pine <c>lockSwingCount</c> (Visual Display, default 50).</summary>
    public int  LockSwingCount     { get; set; } = MainPromotionEngine.DefaultLockSwingCount;
    public bool ShowLockedSwings   { get; set; }
    public bool ShowFakeSwings     { get; set; }
    public bool ShowActiveSwings   { get; set; }
    /// <summary>Debug label at push showing confirm rule (A1/A2/A3/A4/MICRO/BYPASS).</summary>
    public bool ShowSwingPushDebug { get; set; }
    /// <summary>Debug labels on last 10 bars — why each was not chosen as swing HIGH (đỉnh).</summary>
    public bool ShowSwingPeakMissLabels { get; set; }

    /// <summary>Debug labels at wick-noise bars showing LTF H/L confirm slice.</summary>
    public bool ShowWickNoiseDebugLabels { get; set; }
    /// <summary>Debug labels at OB bars when rectangle is missing — shows reason.</summary>
    public bool ShowObBoxMissingDebug { get; set; }
    public int    KeyOpacityActivePercent { get; set; } = PineColors.DefaultKeyOpacityActivePercent;
    public int    KeyOpacityBrokenPercent { get; set; } = PineColors.DefaultKeyOpacityBrokenPercent;

    /// <summary>Pine <c>useOBConfirm</c> — LTF confirm trước khi giữ OB ZIN.</summary>
    public bool   UseObConfirm          { get; set; } = true;
    /// <summary>Pine <c>LTF_BUFFER_SIZE</c> default 50.</summary>
    public int    LtfBufferSize         { get; set; } = 50;
    /// <summary>HTF có mapping LTF (M5→M2, …) — bắt buộc để OB LTF confirm chạy.</summary>
    public bool   ObLtfMappingResolved  { get; set; }
    /// <summary>Host inject: lấy LTF snapshot cho HTF bar đóng (lazy backfill khi ring miss).</summary>
    public Func<int, WickNeutralizeEngine.ILtfBarBundle?>? LtfSnapshotFetcher { get; set; }

    public int LastEvaluatedBarIndex { get; private set; } = -1;
    public double LastBarClose { get; private set; }

    // ===== Diagnostic snapshot of LAST OnBar() invocation =====
    // Used by host's Diagnostic Panel to verify pivot-break preconditions.
    public double DiagLastAtrWilder14 { get; private set; } = double.NaN;
    public double DiagLastAtrKeylevel { get; private set; } = double.NaN;
    public double DiagLastAvgBody { get; private set; } = double.NaN;
    public double DiagLastRawBody1 { get; private set; } = double.NaN;
    public double DiagLastRawRange1 { get; private set; } = double.NaN;
    public bool DiagLastRawIsDoji1 { get; private set; }
    public bool DiagLastRawClearBody1 { get; private set; }
    public double DiagLastEffBody { get; private set; } = double.NaN;
    public bool DiagLastEffIsDoji { get; private set; }
    public bool DiagLastEffClearBody { get; private set; }
    public int DiagAtrBarsSeen14 { get; private set; }
    // Raw OHLC at lag 1 of the LAST OnBar — proves whether series buffer holds real prices or phantoms
    public double DiagLastOhlc1Open { get; private set; } = double.NaN;
    public double DiagLastOhlc1High { get; private set; } = double.NaN;
    public double DiagLastOhlc1Low { get; private set; } = double.NaN;
    public double DiagLastOhlc1Close { get; private set; } = double.NaN;

    /// <summary>HTF bar index of bar A (lag 1) on last <see cref="OnBar"/>.</summary>
    public int DiagBarAIndex { get; private set; } = -1;
    public WickNeutralizeEngine.WickNeutralizeDiag DiagLastWickBarA { get; private set; } = new();
    public int DiagLtfBarCount { get; private set; }
    public const int DiagLtfBarMax = 8;
    public double[] DiagLtfOpen { get; } = new double[DiagLtfBarMax];
    public double[] DiagLtfHigh { get; } = new double[DiagLtfBarMax];
    public double[] DiagLtfLow { get; } = new double[DiagLtfBarMax];
    public double[] DiagLtfClose { get; } = new double[DiagLtfBarMax];

    // Persistent ATR state — Wilder's RMA to match Pine ta.atr(len)
    double _atrWilder    = double.NaN;  // running Wilder RMA value
    int    _atrBarsSeen  = 0;           // bars fed into ATR so far
    double _atrSmaSum    = 0;           // accumulator for SMA seed phase

    // Gap 4.6: separate fixed ATR(14) for key-break check (Pine always uses len=14 there)
    const int KeyBreakAtrLen = 14;
    double _atrWilder14   = double.NaN;
    int    _atrBarsSeen14 = 0;
    double _atrSmaSum14   = 0;

    /// <summary>Snapshot of pivot flag at end of last bar — used for FlagPrev injection.</summary>
    readonly List<int> _flagPrevBar = new();

    /// <summary>Pine structureIdCounter — increments when MAIN BROKEN confirmed.</summary>
    int _structureIdCounter = 0;

    /// <summary>Pine <c>pivKeyExtPrev</c> — persisted across bars; held when <c>keyStopBar == bar_index</c>.</summary>
    readonly Dictionary<string, bool> _pivKeyExtPrev = new();

    /// <summary>Pine neutralizedHighPrev/Low at end of prior bar — read as [1] for A4 wick guard.</summary>
    double? _prevBarNeutralizedHigh;
    double? _prevBarNeutralizedLow;


    public PineStateEngine()
    {
        LtfBuffer = new LtfRingBufferStore(LtfBufferSize);
    }

    public void OnBar(
        SeriesBuffer series,
        int barIndex,
        IDrawingCommandSink? drawingSink = null,
        WickNeutralizeEngine.ILtfBarBundle? ltfSnapshotForClosedHtfBar = null)
    {
        if (barIndex < 1) { LastEvaluatedBarIndex = barIndex; return; }
        series.SetCurrentEvaluationBarIndex(barIndex);

        // Pine barstate.isconfirmed parity: skip duplicate ticks on the same forming bar.
        // Confirmed passes (visual-replay bar close, attach history) still run full engine.
        if (barIndex == LastEvaluatedBarIndex
            && series.TryGetCurrentFlags(out var rtFlags)
            && !rtFlags.IsConfirmed)
        {
            OnBarFormingTick(series, barIndex, drawingSink);
            return;
        }

        // OHLC bar A = bar_index - 1 = offset [1]
        var ok = series.TryGetOhlcAtOffset(1, out var ohlc1);
        if (!ok) return;
        var ok0 = series.TryGetOhlcAtOffset(0, out var ohlc0);
        if (!ok0) return;
        LastBarClose = ohlc0.Close;

        // ----- 1) Snapshot prev flags (Pine pivFlagPrev updated after alert eval) -----
        PivotTransitionEngine.SnapshotPrevFlags(Pivots);

        // ----- 2) Compute eff flags for bar A (with gap merge, Pine useGapMerge=true) -----
        var avgBody  = ComputeAvgBody(series, (int)KeylevelAvgBodyLen);
        // Pine atr14[1] for wick confirm = ATR(14) as of previous bar (before this bar's TR feed).
        var atrForWickConfirm = _atrWilder14;
        // Pine: float _gapAtrVal = ta.atr(14) — ALWAYS len 14 (effTier, gap merge, wick ATR fallback).
        UpdateWilderAtr14(series);
        var gapAtrVal = _atrWilder14;
        // Pine: ta.atr(keylevelAtrLen) — separate series for keylevel clear-body / reference candle.
        double atrKeylevel;
        if (KeylevelAtrLen == KeyBreakAtrLen)
        {
            atrKeylevel = gapAtrVal;
            _atrWilder   = gapAtrVal;
            _atrBarsSeen = _atrBarsSeen14;
            _atrSmaSum   = _atrSmaSum14;
        }
        else
        {
            UpdateWilderAtr(series, KeylevelAtrLen);
            atrKeylevel = _atrWilder;
        }
        var minTick  = TickSize * 2;

        // Gap merge: if bar A (lag 1) opened with a gap vs bar X (lag 2), merge X+Y into XY.
        // Pine: gapFilterAtr = 0.5; gap if |open[1] − close[2]| >= 0.5 * ta.atr(14)
        var hasOhlc2  = series.TryGetOhlcAtOffset(2, out var ohlc2);
        var calcIsGap = UseGapMerge && hasOhlc2 && !double.IsNaN(gapAtrVal) && barIndex > 1
                        && Math.Abs(ohlc1.Open - ohlc2.Close) >= GapFilterAtr * gapAtrVal;
        var effOpen  = calcIsGap ? ohlc2.Open                        : ohlc1.Open;
        var effHigh  = calcIsGap ? Math.Max(ohlc1.High, ohlc2.High)  : ohlc1.High;
        var effLow   = calcIsGap ? Math.Min(ohlc1.Low,  ohlc2.Low)   : ohlc1.Low;
        var effClose = ohlc1.Close;   // always Y.close (Pine spec)
        var effBody  = Math.Abs(effClose - effOpen);
        var effBull  = effClose > effOpen;
        var effBear  = effClose < effOpen;
        var effRange = effHigh - effLow;

        // Structure doji: dojiBodyRatioMax_structure = 0.10 (Pine line 80 / 467)
        var effIsDoji    = effRange > minTick && (effRange == 0 ? true : effBody / effRange <= DojiBodyRatioMaxStructure);
        // effClearBody: Pine line 471-475. Defensive only on effBody>0 (see rawClearBody comment).
        var effClearBody = !effIsDoji && !double.IsNaN(avgBody) && effBody > 0
            && (effBody >= avgBody * KeylevelMinBodyMult
                || (KeylevelUseAtrRule && !double.IsNaN(gapAtrVal) && effBody >= atrKeylevel * KeylevelAtrMult));
        // effTier: Pine line 468 — ALWAYS ta.atr(14)*strong/medium mult (NOT keylevelAtrLen)
        var effTier = (double.IsNaN(gapAtrVal) || effIsDoji) ? 0
                      : effBody >= gapAtrVal * StrongAtrMult ? 2
                      : effBody >= gapAtrVal * MediumAtrMult ? 1
                      : 0;

        Func<int, (double, double, double, double)> ohlcByLag =
            lag => { series.TryGetOhlcAtOffset(lag, out var t); return (t.Open, t.High, t.Low, t.Close); };

        // Pine A4: neutralizedHighPrev[1] / neutralizedLowPrev[1] — wick state of bar B (lag 2).
        var bLag2UpperWickNeutralized = UseWickNoiseFilter && _prevBarNeutralizedHigh.HasValue;
        var bLag2LowerWickNeutralized = UseWickNoiseFilter && _prevBarNeutralizedLow.HasValue;

        WickNeutralizeEngine.ILtfBarBundle? LtfForOffset(int offset) =>
            offset == 1 ? ltfSnapshotForClosedHtfBar
            : offset >= 2 ? LtfBuffer.TryGetBundle(barIndex - offset)
            : null;

        // Wick neutralize bar A → pivCandHigh/Low (Pine cleanHigh1Raw/cleanLow1Raw).
        // Must happen BEFORE break-tick so highUsed/lowUsed are wick-cleaned (gap 2.1).
        var ltfBarA = LtfForOffset(1);
        var (bar1Clean, wickDiagBarA) = WickNeutralizeEngine.NeutralizeBarAtOffsetDetailed(
            UseWickNoiseFilter, WickAvgLen, UseLtfWickConfirm, LtfConfirmRatio,
            offset: 1, ohlcByLag, ltfBarA);
        DiagBarAIndex = barIndex >= 1 ? barIndex - 1 : -1;
        DiagLastWickBarA = wickDiagBarA;
        CaptureLtfBarDiag(ltfBarA);
        _prevBarNeutralizedHigh = bar1Clean.NeutralizedHigh;
        _prevBarNeutralizedLow  = bar1Clean.NeutralizedLow;
        var pivCandHigh = bar1Clean.CleanHigh;
        var pivCandLow  = bar1Clean.CleanLow;

        // Raw body/doji for break engine — Pine process_break uses ohlc1 raw (not eff-merged).
        // Gap 2.2/2.3: rawClearBody = f_has_clear_body_structure_at(1) with raw lag 1.
        var rawBody1  = Math.Abs(ohlc1.Close - ohlc1.Open);
        var rawRange1 = ohlc1.High - ohlc1.Low;
        var rawIsDoji1     = rawRange1 > minTick && rawBody1 / rawRange1 <= DojiBodyRatioMaxStructure;
        // Defensive: require rawBody > 0 so a phantom (H=L=O=C) bar can't pass via 0 >= 0*1.5.
        // Do NOT require avgBody > 0 — exotic pairs (e.g. EURNOK) have legit flat-bar streaks
        // where avgBody legitimately becomes 0; blocking via avgBody>0 would freeze transitions
        // on the next REAL bar after such a streak.
        var rawClearBody1  = !rawIsDoji1 && !double.IsNaN(avgBody) && rawBody1 > 0
            && (rawBody1 >= avgBody * KeylevelMinBodyMult
                || (KeylevelUseAtrRule && !double.IsNaN(gapAtrVal) && rawBody1 >= atrKeylevel * KeylevelAtrMult));

        // Diag snapshot (so host's Diagnostic Panel can verify pivot-break preconditions)
        DiagLastAtrWilder14   = gapAtrVal;
        DiagLastAtrKeylevel   = atrKeylevel;
        DiagLastAvgBody       = avgBody;
        DiagLastRawBody1      = rawBody1;
        DiagLastRawRange1     = rawRange1;
        DiagLastRawIsDoji1    = rawIsDoji1;
        DiagLastRawClearBody1 = rawClearBody1;
        DiagLastEffBody       = effBody;
        DiagLastEffIsDoji     = effIsDoji;
        DiagLastEffClearBody  = effClearBody;
        DiagAtrBarsSeen14     = _atrBarsSeen14;
        DiagLastOhlc1Open     = ohlc1.Open;
        DiagLastOhlc1High     = ohlc1.High;
        DiagLastOhlc1Low      = ohlc1.Low;
        DiagLastOhlc1Close    = ohlc1.Close;

        // ----- 2b) Trim pivot pool outside lookback window (Pine ~4428, before process_break) -----
        PivotTrimEngine.TrimOutsideLookback(barIndex, LookbackBars, Pivots, ObPool, drawingSink);

        // ----- 3) Process break (pending → broken or rollback) BEFORE new push -----
        // highUsed/lowUsed = Pine cleanHigh1Raw/cleanLow1Raw (wick-cleaned).
        // rawClearBody/rawIsDoji = Pine raw f_has_clear_body_structure_at(1).
        var breakResult = Transitions.Tick(
            barIndex, Pivots,
            ohlc1.Open, bar1Clean.CleanHigh, bar1Clean.CleanLow, ohlc1.Close,
            rawClearBody1, rawIsDoji1, IsM5Mode,
            breakR3MaxK: BreakR3MaxK,
            drawingSink, obPool: ObPool);

        if (breakResult.NeedStructIncrement)
            _structureIdCounter++;

        // Pine ~3560: f_sync_keybox_with_flag ngay khi ACTIVE/D_SWING → BROKEN (recreate nếu soft-hide)
        SyncKeyboxesOnBrokenFlagTransition(
            barIndex, series, avgBody, atrKeylevel, ohlcByLag, drawingSink);

        // ----- 3b) Notify RealZone for BROKEN transitions -----
        for (var i = 0; i < Pivots.Count; i++)
        {
            var fn = Pivots.GetFlag(i);
            var fp = Pivots.GetPrevFlag(i);
            if (fn == fp) continue;

            if (fn == 2 && fp != 2)
            {
                RealZone.OnPivotBroken(Pivots.GetTypeAt(i), Pivots.GetBreakClose(i));
                // Invalidate OB entries owned by this broken pivot
                for (var j = 0; j < ObPool.Count; j++)
                {
                    if (ObPool.GetRecord(j).Owner == i)
                        ObPool.SetState(j, 3);
                }
            }
        }

        // ----- 3c) MAIN C demote — D_swing broken done Path B -----
        // D_swing that just confirmed BROKEN and did NOT enter waiting-D state is immediately broken done.
        // Trigger demote of any MAIN C (flag=0) whose mainDIdx points to this D.
        {
            var labelOpts3c = BuildLabelOptions(barIndex);
            for (var i = 0; i < Pivots.Count; i++)
            {
                var fn = Pivots.GetFlag(i);
                var fp = Pivots.GetPrevFlag(i);
                if (fn != 2 || fp == fn) continue;                                         // just became BROKEN
                if (Pivots.GetFlagBeforePending(i) != 4) continue;                         // was D_SWING before pending
                if (Pivots.GetFirstDIdx(i) == PivotStateStore.FirstDWaiting) continue;     // still waiting → Path A, not done yet
                MainPromotionEngine.DemoteMainCsForBrokenDSwing(
                    i, Pivots, ObPool, drawingSink, IsH4Chart, labelOpts3c);
            }
        }

        var cleanSeries = new WickCleanSeriesAdapter(
            UseWickNoiseFilter, WickAvgLen, UseLtfWickConfirm, LtfConfirmRatio,
            LtfForOffset, ohlcByLag);

        // ----- 4) Pivot detection — PullbackFilter (default) or bypass mode -----
        IReadOnlyList<PullbackFilterEngine.PivotBatch> batch;
        MicroSwingEngine.MicroSwingResult microResult = MicroSwingEngine.MicroSwingResult.Empty;

        if (UsePullbackFilter)
        {
            var lastForStep3 = PivotDetector.LastPushedSwingType;

            // prevPivCand* kept for pending metadata; A4 guard uses neutralized*Prev[1] instead.
            var bar2Clean = WickNeutralizeEngine.NeutralizeBarAtOffset(
                UseWickNoiseFilter, WickAvgLen, UseLtfWickConfirm, LtfConfirmRatio,
                offset: 2, ohlcByLag, LtfForOffset(2));

            // Step 1: push pending A with prevCleanHigh/Low stored for A4 guard
            Filter.PushStep1(barIndex, effBull, effBear, effTier, calcIsGap,
                pivCandHigh, pivCandLow,
                prevPivCandHigh: bar2Clean.CleanHigh,
                prevPivCandLow:  bar2Clean.CleanLow);

            // Step 1.5: micro rescue detect (Pine before Step 2)
            if (UseMicroSwingRule && Pivots.Count > 0)
            {
                // Micro scan covers [nBar+1 .. barIndex-2] which can be ≫ ring cap (50).
                // EnsureLtfBarsForMicroScan (push-to-ring) is a no-op in steady state:
                // pushing 200 bars sequentially leaves only the last 50 in ring — same as
                // before. Instead: build a micro-specific adapter that reads from ring first,
                // then falls back to a direct LtfSnapshotFetcher call (no ring write, no
                // eviction). This gives full Pine parity for any N→A gap width.
                var cleanSeriesForMicro = UseLtfWickConfirm && LtfSnapshotFetcher is not null
                    ? (ICleanOhlcSeries)new WickCleanSeriesAdapter(
                        UseWickNoiseFilter, WickAvgLen, UseLtfWickConfirm, LtfConfirmRatio,
                        lag =>
                        {
                            if (lag == 1) return ltfSnapshotForClosedHtfBar;
                            if (lag < 2)  return null;
                            var htfBar = barIndex - lag;
                            if (htfBar < 0) return null;
                            return LtfBuffer.TryGetBundle(htfBar) ?? LtfSnapshotFetcher(htfBar);
                        },
                        ohlcByLag)
                    : cleanSeries;

                microResult = MicroSwing.Detect(
                    UseMicroSwingRule, barIndex, Pivots,
                    effClearBody, effIsDoji, effBull, effBear,
                    ohlc1.Close, lastForStep3, cleanSeriesForMicro, Filter.PenH, Filter.PenL);

                if (ShowSwingPeakMissLabels && microResult.ClearedPenH.Count > 0)
                {
                    foreach (var (b, p) in microResult.ClearedPenH)
                        _peakMissTracker.NoteMicroQueueClear(b, p, microResult.NBar, microResult.ABar, microResult.MBar);
                }
            }

            List<PullbackFilterEngine.SwingCandidateDiag>? candidateDiags = null;
            if (ShowSwingPushDebug)
                candidateDiags = new List<PullbackFilterEngine.SwingCandidateDiag>();

            Filter.PeakMissTracker = ShowSwingPeakMissLabels ? _peakMissTracker : null;

            batch = Filter.EvaluateAndBatch(
                barIndex, lastForStep3, effClose, effBull, effBear,
                effIsDoji, effClearBody, effTier, ohlcByLag,
                useWickNoiseFilter: UseWickNoiseFilter,
                bLag2UpperWickNeutralized: bLag2UpperWickNeutralized,
                bLag2LowerWickNeutralized: bLag2LowerWickNeutralized,
                diags: candidateDiags);

            if (candidateDiags != null && candidateDiags.Count > 0)
                SwingPushDebugLabelEngine.EmitCandidateDiags(candidateDiags, barIndex, drawingSink);
        }
        else
        {
            var bypassBatch = new List<PullbackFilterEngine.PivotBatch>();
            if (!effIsDoji && effClearBody && barIndex >= 1)
            {
                var bull1 = ohlc1.Close > ohlc1.Open;
                var bear1 = ohlc1.Close < ohlc1.Open;
                var last  = PivotDetector.LastPushedSwingType;
                if (bull1 && last != 1)
                    bypassBatch.Add(new PullbackFilterEngine.PivotBatch(barIndex - 1, pivCandHigh, 1, "BYPASS_H"));
                else if (bear1 && last != -1)
                    bypassBatch.Add(new PullbackFilterEngine.PivotBatch(barIndex - 1, pivCandLow, -1, "BYPASS_L"));
            }
            batch = bypassBatch;
        }

        var atrProxy = !double.IsNaN(atrKeylevel) && atrKeylevel > 0 ? atrKeylevel : TickSize * 100;

        // Pine MAIN: push micro M before batch; gate batch when ACTIVE BROKEN this bar
        var pushQueue = new List<PullbackFilterEngine.PivotBatch>();
        if (!breakResult.HasActiveBroken)
        {
            if (microResult.Detected)
                pushQueue.Add(new PullbackFilterEngine.PivotBatch(
                    microResult.MBar, microResult.MPrice, microResult.MTyp, "MICRO"));
            pushQueue.AddRange(batch);
        }

        foreach (var entry in pushQueue)
        {
            // Pine f_push ~3840: do not add pivots outside lookback window
            if (!PivotTrimEngine.IsWithinLookbackWindow(barIndex, entry.Bar, LookbackBars))
                continue;

            // Sync alternation counters (mirrors Pine highIdCounter++ / lowIdCounter++)
            PivotDetector.RegisterRescuePush(entry.Type, entry.Type);

            var poolIdx = Pivots.PushPivot(
                price:    entry.Price,
                barIndex: entry.Bar,
                type:     entry.Type,
                timeMs:   0,
                highId:   entry.Type ==  1 ? PivotDetector.HighIdCounter : 0,
                lowId:    entry.Type == -1 ? PivotDetector.LowIdCounter  : 0,
                pushReason: entry.Reason);

            RealZone.OnSwingPushed(entry.Type, entry.Bar);

            if (ShowSwingPushDebug)
                SwingPushDebugLabelEngine.EmitCreate(
                    Pivots, poolIdx, entry.Reason, barIndex, LookbackBars, drawingSink);

            var newSid = entry.Type == 1
                ? $"H{PivotDetector.HighIdCounter}"
                : $"L{PivotDetector.LowIdCounter}";

            // Emit initial ACTIVE label. SubType is 0 at push time (HH/LL check happens later
            // in f_push, so we always start with base H/L color).
            drawingSink?.Enqueue(new DrawingCommand
            {
                Kind        = DrawingCommandKind.AddLabel,
                LabelKeyStr = $"sw_{newSid}",
                BarIndex    = entry.Bar,
                Price       = entry.Price,
                IsHigh      = entry.Type == 1,
                Text        = $"ACTIVE {newSid}",
                ColorArgb   = entry.Type == 1 ? PineColors.HighActive : PineColors.LowActive,
            });

            // Emit connecting line to previous pivot (Pine: utils.f_add_line)
            // Color = #b8fbdb (mint green), fully opaque — Pine lineColor
            if (poolIdx > 0 && drawingSink != null)
            {
                var prevSnap = Pivots.GetSnapshot(poolIdx - 1);
                drawingSink.Enqueue(new DrawingCommand
                {
                    Kind     = DrawingCommandKind.AddLine,
                    LabelKeyStr = $"ln_{prevSnap.BarIndex}_{entry.Bar}",
                    X1       = prevSnap.BarIndex,
                    Y1       = prevSnap.Price,
                    X2       = entry.Bar,
                    Y2       = entry.Price,
                    IsHigh   = entry.Type == 1,
                    ColorArgb = PineColors.LineActive,
                });
            }

            // ----- 4b) Build KeyBox for new pivot -----
            var pivKind = entry.Type == 1 ? "H" : "L";

            // Pine f_push ~3968: isGapMerge → lookback=0 (chỉ nến pivot, tránh span qua gap).
            var effKeyLookback = KeyLevelEngine.EffectiveKeylevelLookback(
                entry.IsGap, UseGapMerge, KeylevelLookback);

            var refPick = KeyLevelEngine.FindReferenceCandleFromPivot(
                barIndex, entry.Bar, pivKind,
                lookback: effKeyLookback, avgBody, KeylevelMinBodyMult,
                useAtrRule: KeylevelUseAtrRule, atrLen: KeylevelAtrLen, atrMult: KeylevelAtrMult,
                ohlcByLag, atrValueWhenRuleEnabled: atrKeylevel);

            // Pine: HIGH keylevel = inpBoxHighColor=rgb(197,89,89) reddish (resistance)
            //       LOW  keylevel = inpBoxLowColor=rgb(72,189,78) greenish (support)
            // Fill at keyOpacityActive=90 (90% transparent → alpha=26)
            // Border = base color at ~83% opaque (transp=17 → alpha=212)
            var kbSpec = KeyLevelEngine.BuildKeylevelBox(
                barIndex, entry.Bar, pivKind, entry.Price,
                refPick.BestLag, refPick.BodyAbs,
                searchRangeBars: 200, atrLen: KeylevelAtrLen, atrValue: atrProxy,
                highColorArgb: KeyLevelVisual.ActiveColors(1, KeyOpacityActivePercent).FillArgb,
                lowColorArgb:  KeyLevelVisual.ActiveColors(-1, KeyOpacityActivePercent).FillArgb,
                opacity: 100 - KeyOpacityActivePercent,
                minTick: TickSize,
                ohlcByLag,
                lag =>
                {
                    if (!series.TryGetSnapshotAtOffset(lag, out var sn)) return DateTime.MinValue;
                    return sn.OpenChartTimeLocal;
                },
                drawingSink);

            if (kbSpec != null)
            {
                var kb = new KeyBoxRef { Spec = kbSpec };
                Pivots.SetKeyBox(poolIdx, kb);
                Pivots.SetHasKey(poolIdx, true);
                Pivots.SetKeyVisible(poolIdx, true);
                Pivots.SetKeyExtending(poolIdx, true);

                KeyLevelSyncEngine.SyncKeyboxWithFlag(
                    poolIdx, Pivots,
                    BuildKeyLevelContext(barIndex, series, avgBody, atrKeylevel, ohlcByLag),
                    drawingSink, ObPool);
            }

            // ----- 4e) HH/LL subType detection (Pine lines 3925–3957) -----
            MainPromotionEngine.DetectHhLl(Pivots, poolIdx, drawingSink);

            // ----- 4f) Try D-commit + MAIN C promotion (Pine f_push while-loop) -----
            var labelOpts = BuildLabelOptions(barIndex);
            var keyCtx = BuildKeyLevelContext(barIndex, series, avgBody, atrKeylevel, ohlcByLag);
            var (promoted, cIdx, dIdx) = MainPromotionEngine.TryCommitD(
                poolIdx, Pivots, ObPool, drawingSink, labelOpts, keyCtx,
                isH4Chart: IsH4Chart, lockSwingCount: LockSwingCount);
            if (promoted)
                SyncKeyboxesOnBrokenFlagTransition(
                    barIndex, series, avgBody, atrKeylevel, ohlcByLag, drawingSink);

            if (promoted && cIdx >= 0 && Pivots.GetFlag(cIdx) == 0)
            {
                ObStructuralEngine.FindAndDrawStructural(
                    cIdx, dIdx, Pivots, ObPool, barIndex,
                    MinObBodyRatio, MaxDojiBodyRatio, LookbackBars, ShowObBox, UseObConfirm, HideObLoseZin,
                    ohlcByLag,
                    lag =>
                    {
                        if (!series.TryGetSnapshotAtOffset(lag, out var sn)) return DateTime.MinValue;
                        return sn.OpenChartTimeLocal;
                    },
                    drawingSink,
                    obScanBars: ObScanBars);  // Gap 4.2: pass global ObScanBars
            }
        }

        // ----- 4g) Pivot OB scan (Pine f_find_and_draw_OB per ACTIVE/D_SWING) -----
        ObPivotFinderEngine.ScanAllActivePivots(
            Pivots, ObPool, barIndex, ObScanBars, MinObBodyRatio, LookbackBars, ShowObBox, UseObConfirm, HideObLoseZin,
            ohlcByLag,
            lag =>
            {
                if (!series.TryGetSnapshotAtOffset(lag, out var sn)) return DateTime.MinValue;
                return sn.OpenChartTimeLocal;
            },
            drawingSink);

        // ----- 4h) OB lifecycle + draw (Pine visibility loop) -----
        var checkBar = barIndex - 2;
        var useObLtfMode = UseObConfirm && ObLtfMappingResolved;
        for (var i = 0; i < ObPool.Count; i++)
        {
            var r = ObPool.GetRecord(i);
            // Gap 4.10: Pine skips state==3 immediately — no per-bar draw commands.
            // Box was already handled at transition time.
            if (r.State == 3)
                continue;

            var tick = OBEngine.Tick(
                barIndex, r.Bar, r.State, r.X, r.Type, r.FlagLtf, r.LastCheckedBar, r.Extending,
                ohlcByLag);
            var prevBox = r.Box;
            ObPool.SetState(i, tick.NewState);
            var nextLastChecked = barIndex - 2;
            if (nextLastChecked > r.LastCheckedBar)
                ObPool.SetLastCheckedBar(i, nextLastChecked);

            if (tick.BoxShouldBeCreated)
            {
                if (ShowObBox)
                {
                    var (spec, fail) = ObDrawEngine.TryBuildFromBar(
                        barIndex, r.Bar, r.Type, r.Source, r.Owner, r.Number, LookbackBars, ohlcByLag,
                        lag =>
                        {
                            if (!series.TryGetSnapshotAtOffset(lag, out var sn)) return DateTime.MinValue;
                            return sn.OpenChartTimeLocal;
                        });
                    if (spec != null)
                    {
                        ObPool.SetBox(i, spec);
                        ObPool.SetExtending(i, true);
                        ObDrawEngine.EmitCreate(spec, drawingSink);
                        ObPool.SetBoxMissingReason(i, ObBoxMissingReason.None);
                    }
                    else if (fail.HasValue)
                        ObPool.SetBoxMissingReason(i, fail.Value);
                }
                else
                    ObPool.SetBoxMissingReason(i, ObBoxMissingReason.ShowObBoxOff);
            }

            if (tick.NewState == 3 && ObPool.GetRecord(i).Box == null)
            {
                var rr = ObPool.GetRecord(i);
                ObPool.SetBoxMissingReason(i, rr.FlagLtf == 3
                    ? ObBoxMissingReason.LtfReject
                    : ObBoxMissingReason.HtfLose);
            }
            else if (tick.NewState == 2 && ObPool.GetRecord(i).Box != null)
                ObPool.SetBoxMissingReason(i, ObBoxMissingReason.None);
            else if (tick.NewState == 0)
                ObPool.SetBoxMissingReason(i, ObBoxMissingReason.Pending);

            if (tick.BoxShouldStop && prevBox != null)
            {
                ObPool.SetExtending(i, false);
                // Pine 4759-4762: touch tại checkBar, set_right(time[1]) = open của bar sau checkBar.
                ObDrawEngine.EmitStopExtend(prevBox, checkBar + 1, drawingSink);
            }
            else if (tick.ExtendingChanged)
                ObPool.SetExtending(i, false);

            if (tick.NewState == 3)
            {
                // Pine 4759-4765: HTF ZIN→LOSE — stop extend only; box stays. hideOBLoseZin chỉ ẩn label.
                ObLabelDrawEngine.SetLoseLabel(ObPool, i, HideObLoseZin, drawingSink);
            }

            RunObLtfConfirm(i, barIndex, checkBar, tick, useObLtfMode, drawingSink);

            var rVis = ObPool.GetRecord(i);
            if (rVis.State == 0 || rVis.State == 2)
                ObLabelDrawEngine.UpdateZinVisibility(rVis, HideObZin, drawingSink);
        }

        ObDrawEngine.TickExtendAll(ObPool, barIndex, drawingSink);

        ObOverlapEngine.LimitOverlappingObBoxes(ObPool, barIndex, ohlcByLag, drawingSink);

        if (ShowObBoxMissingDebug)
            ObBoxDebugLabelEngine.RefreshPool(ObPool, barIndex, LookbackBars, ohlcByLag, drawingSink);
        else
            ObBoxDebugLabelEngine.ClearPool(ObPool, drawingSink);

        // Gap 4.5: Pine trims at maxOBs + 10 (buffer keeps 10 extra before hard-trim).
        foreach (var ti in OBEngine.ComputeTrimIndices(ObPool, MaxObs + 10))
            ObPool.RemoveAt(ti);

        // ----- 5) RealZone Tick — try update buy/sell zones at K=[0] -----
        RealZone.Tick(
            barIndex:       barIndex,
            isM5:           IsM5Mode,
            tickSize:       TickSize,
            breakR3MaxK:    BreakR3MaxK,
            ohlcAtLag:      lag => { series.TryGetOhlcAtOffset(lag, out var t); return (t.Open, t.High, t.Low, t.Close); },
            hasClearBodyAt: lag =>
            {
                if (!series.TryGetOhlcAtOffset(lag, out var t)) return false;
                var b = Math.Abs(t.Close - t.Open);
                var r = t.High - t.Low;
                if (r == 0) return false;
                var doji = r > minTick && b / r <= DojiBodyRatioMaxStructure;
                // Pine effClearBody OR condition: avgBody*mult OR atr*atrMult
                return !doji && !double.IsNaN(avgBody)
                    && (b >= avgBody * KeylevelMinBodyMult
                        || (KeylevelUseAtrRule && !double.IsNaN(atrKeylevel) && b >= atrKeylevel * KeylevelAtrMult));
            },
            isDojiAt: lag =>
            {
                if (!series.TryGetOhlcAtOffset(lag, out var t)) return true;
                var b = Math.Abs(t.Close - t.Open);
                var r = t.High - t.Low;
                if (r == 0) return true;
                return r > minTick && b / r <= DojiBodyRatioMaxStructure;
            });

        // Key-break: closed bar only (skip live forming ticks; IsNew → OHLC offset [1]).
        // Gap 4.6: key-break check uses fixed ATR(14), not KeylevelAtrLen.
        var keyBreak = KeyLevelDrawEngine.ResolveKeyBreakCheck(series, barIndex);
        KeyLevelDrawEngine.Tick(
            BuildKeyLevelContext(barIndex, series, avgBody, gapAtrVal, ohlcByLag),
            Pivots,
            in keyBreak,
            drawingSink, ObPool);

        // Fix A.1: Pine lines 5277-5289 — eff* expand mỗi bar theo bar high/low (không chỉ pivot break).
        RealZone.OnRealtimeBarPrice(ohlc0.High, ohlc0.Low);

        if (ltfSnapshotForClosedHtfBar != null && barIndex >= 1)
            LtfBuffer.PushSnapshot(barIndex - 1, ltfSnapshotForClosedHtfBar);

        if (drawingSink != null)
            PivotLabelEngine.RefreshAll(Pivots, drawingSink, BuildLabelOptions(barIndex));

        if (ShowSwingPeakMissLabels && drawingSink != null && UsePullbackFilter)
        {
            _peakMissTracker.RefreshLivePenH(Filter.PenH, barIndex, Filter.BMaxLagBars, Filter.CdMaxLagBars);
            _peakMissTracker.Prune(barIndex);
            SwingPeakMissDebugLabelEngine.EmitWindow(
                _peakMissTracker, Pivots, barIndex, LookbackBars, drawingSink);
        }

        if (ShowWickNoiseDebugLabels && drawingSink != null && UseWickNoiseFilter && DiagBarAIndex >= 0)
        {
            WickNoiseDebugLabelEngine.EmitWindow(
                barIndex,
                DiagBarAIndex,
                DiagLastOhlc1High,
                DiagLastOhlc1Low,
                DiagLastWickBarA,
                DiagLtfBarCount,
                DiagLtfHigh,
                DiagLtfLow,
                DiagLtfOpen,
                DiagLtfClose,
                drawingSink);
        }

        LastEvaluatedBarIndex = barIndex;
    }

    /// <summary>
    /// Lightweight path for duplicate realtime ticks on the same <paramref name="barIndex"/>.
    /// Updates forming-bar price tracking and chart labels without re-running push/FIFO.
    /// </summary>
    void OnBarFormingTick(SeriesBuffer series, int barIndex, IDrawingCommandSink? drawingSink)
    {
        if (series.TryGetOhlcAtOffset(0, out var ohlc0))
        {
            LastBarClose = ohlc0.Close;
            RealZone.OnRealtimeBarPrice(ohlc0.High, ohlc0.Low);
        }

        if (drawingSink != null)
            PivotLabelEngine.RefreshAll(Pivots, drawingSink, BuildLabelOptions(barIndex));
    }

    PivotLabelRenderOptions BuildLabelOptions(int barIndex) => new()
    {
        ShowLockedSwings = ShowLockedSwings,
        ShowFakeSwings   = ShowFakeSwings,
        ShowActiveSwings = ShowActiveSwings,
        BarIndex         = barIndex,
        LookbackBars     = LookbackBars,
        ObPool           = ObPool,
        DeleteObsOnLockLabelRefresh = !IsH4Chart,
    };

    KeyLevelTickContext BuildKeyLevelContext(
        int barIndex,
        SeriesBuffer series,
        double avgBody,
        double atrValue,
        Func<int, (double, double, double, double)> ohlcByLag) => new()
    {
        BarIndex                = barIndex,
        AvgBody                 = avgBody,
        AtrValue                = atrValue,
        TickSize                = TickSize,
        MaxKeylevelKeep         = MaxKeylevelKeep,
        EnableKeylevel          = EnableKeylevel,
        KeylevelLookback        = KeylevelLookback,
        KeylevelUseAtrRule      = KeylevelUseAtrRule,
        KeylevelMinBodyMult     = KeylevelMinBodyMult,
        KeylevelAtrLen          = KeylevelAtrLen,
        KeylevelAtrMult         = KeylevelAtrMult,
        SearchRangeBars         = LookbackBars,
        KeyOpacityActivePercent = KeyOpacityActivePercent,
        KeyOpacityBrokenPercent = KeyOpacityBrokenPercent,
        OhlcAtLag               = lag =>
        {
            var t = ohlcByLag(lag);
            return (t.Item1, t.Item2, t.Item3, t.Item4);
        },
        ChartTimeAtLag          = lag =>
        {
            if (!series.TryGetSnapshotAtOffset(lag, out var sn)) return DateTime.MinValue;
            return sn.OpenChartTimeLocal;
        },
    };

    /// <summary>Pine <c>f_sync_keybox_with_flag</c> khi flag vừa chuyển sang BROKEN (2) / BROKEN_FAKE (6).</summary>
    void SyncKeyboxesOnBrokenFlagTransition(
        int barIndex,
        SeriesBuffer series,
        double avgBody,
        double atrValue,
        Func<int, (double, double, double, double)> ohlcByLag,
        IDrawingCommandSink? drawingSink)
    {
        if (drawingSink == null) return;

        var ctx = BuildKeyLevelContext(barIndex, series, avgBody, atrValue, ohlcByLag);
        for (var i = 0; i < Pivots.Count; i++)
        {
            var fn = Pivots.GetFlag(i);
            var fp = Pivots.GetPrevFlag(i);
            if ((fn == 2 || fn == 6) && fn != fp)
                KeyLevelSyncEngine.SyncKeyboxWithFlag(i, Pivots, ctx, drawingSink, ObPool);
        }
    }

    void RunObLtfConfirm(
        int obIdx,
        int barIndex,
        int checkBar,
        OBEngine.ObTickResult tick,
        bool useObLtfMode,
        IDrawingCommandSink? drawingSink)
    {
        var r = ObPool.GetRecord(obIdx);
        if (r.State != 2) return;

        if (!UseObConfirm)
        {
            if (r.FlagLtf == 0)
            {
                ObPool.SetFlagLtf(obIdx, 2);
                ObPool.SetLtfConfirmKind(obIdx, ObLtfConfirmKind.Standard);
            }
            return;
        }

        if (!useObLtfMode)
        {
            if (r.FlagLtf == 0)
            {
                ObPool.SetFlagLtf(obIdx, 2);
                ObPool.SetLtfConfirmKind(obIdx, ObLtfConfirmKind.Standard);
            }
            return;
        }

        EnsureLtfBarsForObScan(r.Bar, barIndex, ObScanBars);

        if (tick.BoxShouldBeCreated && tick.NewState == 2)
        {
            var res = ObLtfConfirmEngine.ConfirmCheckBar(checkBar, r.X, r.Type, LtfBuffer);
            ObLtfConfirmEngine.ApplyOutcome(obIdx, res, ObPool, HideObLoseZin, drawingSink,
                new ObLtfConfirmEngine.ObLtfConfirmApplyContext(checkBar, ObLtfConfirmPath.CheckBar, LtfBuffer.FlatCount));
            return;
        }

        if (r.FlagLtf == 0)
        {
            var res = ObLtfConfirmEngine.CheckFullRange(barIndex, r.Bar, r.X, r.Type, ObScanBars, LtfBuffer);
            ObLtfConfirmEngine.ApplyOutcome(obIdx, res, ObPool, HideObLoseZin, drawingSink,
                new ObLtfConfirmEngine.ObLtfConfirmApplyContext(barIndex, ObLtfConfirmPath.FullRange, LtfBuffer.FlatCount));
        }
    }

    /// <summary>Set chart TF M5 mode (caller wires from host TF token).</summary>
    public bool IsM5Mode { get; set; }

    /// <summary>
    /// Sau warmup/attach: quét lại OB ZIN đã auto-confirm bằng NoLtfData hoặc HasLtfNoTouch
    /// khi buffer LTF đã đầy hơn (Pine parity).
    /// </summary>
    public void ReconcileSuspiciousObLtfConfirm(int barIndex, IDrawingCommandSink? drawingSink)
    {
        if (!UseObConfirm || !ObLtfMappingResolved)
            return;

        for (var i = 0; i < ObPool.Count; i++)
        {
            var r = ObPool.GetRecord(i);
            if (r.State != 2 || r.FlagLtf != 2)
                continue;
            if (r.LtfConfirmKind is not (ObLtfConfirmKind.HasLtfNoTouch or ObLtfConfirmKind.NoLtfData))
                continue;

            EnsureLtfBarsForObScan(r.Bar, barIndex, ObScanBars);
            ObPool.SetFlagLtf(i, 0);
            var res = ObLtfConfirmEngine.CheckFullRange(barIndex, r.Bar, r.X, r.Type, ObScanBars, LtfBuffer);
            ObLtfConfirmEngine.ApplyOutcome(i, res, ObPool, HideObLoseZin, drawingSink,
                new ObLtfConfirmEngine.ObLtfConfirmApplyContext(barIndex, ObLtfConfirmPath.FullRange, LtfBuffer.FlatCount));

            var updated = ObPool.GetRecord(i);
            if (updated.State is 0 or 2)
                ObLabelDrawEngine.UpdateZinVisibility(updated, HideObZin, drawingSink);
        }
    }

    /// <summary>
    /// Bù snapshot LTF cho mọi HTF bar trong cửa sổ scan OB (ring chỉ giữ <see cref="LtfBufferSize"/> slot).
    /// </summary>
    public void EnsureLtfBarsForObScan(int barOb, int barIndex, int scanRange)
    {
        if (LtfSnapshotFetcher is null || !ObLtfMappingResolved)
            return;

        var effectiveScan = Math.Min(scanRange, LtfBuffer.Capacity);
        var maxScan = Math.Min(barIndex - 2, barOb + effectiveScan);
        for (var b = barOb + 1; b <= maxScan; b++)
        {
            if (LtfBuffer.HasBar(b))
                continue;

            var snap = LtfSnapshotFetcher(b);
            if (snap is { Count: > 0 })
                LtfBuffer.PushSnapshot(b, snap);
        }
    }

    /// <summary>Pine <c>timeframe.period != "240"</c> — skip OB delete on MAIN_OLD / D strip.</summary>
    public bool IsH4Chart { get; set; }

    /// <summary>SMA of candle body over exactly <paramref name="len"/> bars.
    /// Returns NaN if fewer than len bars are available — matching Pine ta.sma behavior.</summary>
    static double ComputeAvgBody(SeriesBuffer series, int len)
    {
        double sum = 0;
        int n = 0;
        for (var i = 0; i < len; i++)
        {
            if (!series.TryGetOhlcAtOffset(i, out var t)) break;
            sum += Math.Abs(t.Close - t.Open);
            n++;
        }
        return n < len ? double.NaN : sum / n;
    }

    /// <summary>
    /// Wilder's RMA ATR — exact match for Pine ta.atr(len) = ta.rma(ta.tr, len).
    /// Seed phase: accumulate SMA of True Range for the first len bars.
    /// Recursion: atr_t = atr_{t-1} + (tr_t − atr_{t-1}) / len  (Wilder smoothing).
    /// Returns NaN until len bars have been seen (same as Pine na guard).
    /// Call once per bar with the CURRENT bar's OHLC at offset 0.
    /// </summary>
    void UpdateWilderAtr(SeriesBuffer series, int len)
    {
        if (!series.TryGetOhlcAtOffset(0, out var cur)) return;
        double tr;
        if (series.TryGetOhlcAtOffset(1, out var prev))
            tr = Math.Max(cur.High - cur.Low,
                     Math.Max(Math.Abs(cur.High - prev.Close),
                              Math.Abs(cur.Low  - prev.Close)));
        else
            tr = cur.High - cur.Low;

        _atrBarsSeen++;
        if (_atrBarsSeen < len)
        {
            _atrSmaSum += tr;
            _atrWilder  = double.NaN;   // not yet seeded
        }
        else if (_atrBarsSeen == len)
        {
            _atrSmaSum += tr;
            _atrWilder  = _atrSmaSum / len;  // SMA seed
        }
        else
        {
            // Wilder recursion: alpha = 1/len
            _atrWilder = _atrWilder + (tr - _atrWilder) / len;
        }
    }

    /// <summary>Gap 4.6 / Pine ta.atr(14): fixed len=14 for _gapAtrVal, effTier, gap merge, wick ATR, key-break.</summary>
    void UpdateWilderAtr14(SeriesBuffer series)
    {
        const int len = KeyBreakAtrLen;
        if (!series.TryGetOhlcAtOffset(0, out var cur)) return;
        double tr;
        if (series.TryGetOhlcAtOffset(1, out var prev))
            tr = Math.Max(cur.High - cur.Low,
                     Math.Max(Math.Abs(cur.High - prev.Close),
                              Math.Abs(cur.Low  - prev.Close)));
        else
            tr = cur.High - cur.Low;

        _atrBarsSeen14++;
        if (_atrBarsSeen14 < len)
        {
            _atrSmaSum14 += tr;
            _atrWilder14  = double.NaN;
        }
        else if (_atrBarsSeen14 == len)
        {
            _atrSmaSum14 += tr;
            _atrWilder14  = _atrSmaSum14 / len;
        }
        else
        {
            _atrWilder14 = _atrWilder14 + (tr - _atrWilder14) / len;
        }
    }

    // ------------------------------------------------------------------
    // Build payload for AlertEvaluationContext
    // ------------------------------------------------------------------

    public List<PivotEntry> BuildPivotEntries(int barIndex)
    {
        var list = new List<PivotEntry>(Pivots.Count);
        for (var i = 0; i < Pivots.Count; i++)
        {
            var typ      = Pivots.GetTypeAt(i);
            var hid      = Pivots.GetHighId(i);
            var lid      = Pivots.GetLowId(i);
            var sid      = typ == 1 ? $"H{hid}" : typ == -1 ? $"L{lid}" : "?";
            var kb       = Pivots.GetKeyBox(i);
            var keyExt   = Pivots.GetKeyExtending(i);
            var keyExtPr = _pivKeyExtPrev.TryGetValue(sid, out var p) && p;

            list.Add(new PivotEntry
            {
                FlagNow         = Pivots.GetFlag(i),
                FlagPrev        = Pivots.GetPrevFlag(i),
                Type            = typ,
                SwingIdStr      = sid,
                KeyExtending    = keyExt,
                KeyExtendingPrev= keyExtPr,
                KeyStopBar      = Pivots.GetKeyStopBar(i) != PivotStateStore.FirstDNa
                                      ? Pivots.GetKeyStopBar(i)
                                      : (Pivots.GetFlag(i) == 2 ? Pivots.GetConfirmBar(i) : -1),
                HasKeyBox       = kb is not null,
                MainRole        = Pivots.GetMainRole(i),
            });
        }
        return list;
    }

    /// <summary>
    /// Pine post-alert block: update <c>pivKeyExtPrev</c> except when key stopped on this bar
    /// (keep extPrev=true so next bar can detect Event C extPrev→!extNow).
    /// </summary>
    public void UpdateKeyExtPrevAfterAlertEval(int barIndex)
    {
        for (var i = 0; i < Pivots.Count; i++)
        {
            var typ = Pivots.GetTypeAt(i);
            if (typ != 1 && typ != -1) continue;
            var sid = typ == 1 ? $"H{Pivots.GetHighId(i)}" : $"L{Pivots.GetLowId(i)}";

            var stopBar = Pivots.GetKeyStopBar(i);
            if (stopBar != PivotStateStore.FirstDNa && stopBar == barIndex)
                continue;

            _pivKeyExtPrev[sid] = Pivots.GetKeyExtending(i);
        }
    }

    public RealtimeFilterState BuildRealtimeFilterState(double barHigh, double barLow, ZoneState? zones) =>
        new()
        {
            LastPushedSwingType = RealZone.LastPushedSwingType,
            RealBuyTop  = RealZone.RealBuyTop,
            RealBuyBot  = RealZone.RealBuyBot,
            RealSellTop = RealZone.RealSellTop,
            RealSellBot = RealZone.RealSellBot,
            EffBuyBreakBot  = RealZone.EffBuyBreakBot,
            EffSellBreakTop = RealZone.EffSellBreakTop,
            BarHigh = barHigh,
            BarLow  = barLow,
            Zones   = zones,
        };

    public ZoneState BuildZoneState(double barClose) =>
        ZoneCollector.Collect(ObPool, Pivots, barClose);

    public void ResetForSession()
    {
        PivotDetector.Reset();
        RealZone.Reset();
        Filter.Reset();
        Filter.PeakMissTracker = null;
        _peakMissTracker.Clear();
        _flagPrevBar.Clear();
        _pivKeyExtPrev.Clear();
        _atrWilder          = double.NaN;
        _atrBarsSeen        = 0;
        _atrSmaSum          = 0;
        _atrWilder14        = double.NaN;
        _atrBarsSeen14      = 0;
        _atrSmaSum14        = 0;
        _structureIdCounter = 0;
        LastEvaluatedBarIndex = -1;
        _prevBarNeutralizedHigh = null;
        _prevBarNeutralizedLow  = null;
    }

    void CaptureLtfBarDiag(WickNeutralizeEngine.ILtfBarBundle? ltf)
    {
        DiagLtfBarCount = 0;
        for (var i = 0; i < DiagLtfBarMax; i++)
        {
            DiagLtfOpen[i] = double.NaN;
            DiagLtfHigh[i] = double.NaN;
            DiagLtfLow[i] = double.NaN;
            DiagLtfClose[i] = double.NaN;
        }

        if (ltf is null || ltf.Count <= 0)
            return;

        var n = Math.Min(ltf.Count, DiagLtfBarMax);
        DiagLtfBarCount = n;
        for (var i = 0; i < n; i++)
        {
            DiagLtfOpen[i] = ltf.OpenAt(i);
            DiagLtfHigh[i] = ltf.HighAt(i);
            DiagLtfLow[i] = ltf.LowAt(i);
            DiagLtfClose[i] = ltf.CloseAt(i);
        }
    }

    /// <summary>Per-offset clean OHLC for micro-swing scan (Pine cleanHigh/cleanLow series).</summary>
    sealed class WickCleanSeriesAdapter : ICleanOhlcSeries
    {
        readonly bool _useWick;
        readonly int _wickAvgLen;
        readonly bool _useLtf;
        readonly double _ltfRatio;
        readonly Func<int, WickNeutralizeEngine.ILtfBarBundle?> _ltfForOffset;
        readonly Func<int, (double open, double high, double low, double close)> _ohlc;

        public WickCleanSeriesAdapter(
            bool useWick, int wickAvgLen, bool useLtf, double ltfRatio,
            Func<int, WickNeutralizeEngine.ILtfBarBundle?> ltfForOffset,
            Func<int, (double open, double high, double low, double close)> ohlc)
        {
            _useWick = useWick;
            _wickAvgLen = wickAvgLen;
            _useLtf = useLtf;
            _ltfRatio = ltfRatio;
            _ltfForOffset = ltfForOffset;
            _ohlc = ohlc;
        }

        // Pine f_micro_swing_detect: mv = cleanLow1Raw[off] / cleanHigh1Raw[off].
        // clean*1Raw at bar_index B = wick-clean of bar B-1; [off] => bar B-1-off = barRet.
        // NeutralizeBarAtOffset(lag): lag 1 = bar B-1, lag 2 = bar B-2 → use lag = off + 1.
        public double CleanLowAtOffset(int off)
        {
            var lag = off + 1;
            return WickNeutralizeEngine.NeutralizeBarAtOffset(
                _useWick, _wickAvgLen, _useLtf, _ltfRatio, lag, _ohlc, _ltfForOffset(lag)).CleanLow;
        }

        public double CleanHighAtOffset(int off)
        {
            var lag = off + 1;
            return WickNeutralizeEngine.NeutralizeBarAtOffset(
                _useWick, _wickAvgLen, _useLtf, _ltfRatio, lag, _ohlc, _ltfForOffset(lag)).CleanHigh;
        }
    }
}
