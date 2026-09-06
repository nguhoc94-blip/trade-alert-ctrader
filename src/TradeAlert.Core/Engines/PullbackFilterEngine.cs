using System;
using System.Collections.Generic;
using TradeAlert.Core.Models.KeyLevel;

namespace TradeAlert.Core.Engines;

/// <summary>
/// Port Pine pullback-filter engine (useNewPullbackFilter=true):
///   Step 1 — push bar A as pending H/L candidate each bar
///   Step 2 — scan B bar; evaluate A1/A2/A3/A4 rules for each candidate
///   Step 3 — build batch of confirmed pivots (extreme-pick + alternation gate)
///   Step 4 — expire stale candidates
///
/// Returns a list of <see cref="PivotBatch"/> to push.  Caller is responsible
/// for registering each pushed pivot with <see cref="PivotDetector.RegisterRescuePush"/>
/// and populating KeyBox / OB.
/// </summary>
public sealed class PullbackFilterEngine
{
    readonly PendingSwingQueue _penH = new();
    readonly PendingSwingQueue _penL = new();

    /// <summary>Pending HIGH candidates (Pine penHCand_*).</summary>
    public PendingSwingQueue PenH => _penH;

    /// <summary>Pending LOW candidates (Pine penLCand_*).</summary>
    public PendingSwingQueue PenL => _penL;

    /// <summary>Optional rolling debug store for swing-peak miss labels (last 10 bars).</summary>
    public SwingPeakMissTracker? PeakMissTracker { get; set; }

    // ── Parameters (Pine defaults) ────────────────────────────────────────────
    /// <summary>Bx: Max bars A→B before expiring (Pine default 6).</summary>
    public int    BMaxLagBars        { get; set; } = 6;
    /// <summary>CDx: Max bars A→C/D before expiring (Pine default 7).</summary>
    public int    CdMaxLagBars       { get; set; } = 7;
    /// <summary>Strong body ATR multiplier (Pine default 0.6).</summary>
    public double StrongAtrMult      { get; set; } = 0.6;
    /// <summary>Medium body ATR multiplier (Pine default 0.3).</summary>
    public double MediumAtrMult      { get; set; } = 0.3;
    /// <summary>Multi-A: pick price extreme from full queue (Pine default true).</summary>
    public bool   UseExtremePickRule { get; set; } = true;
    /// <summary>A4: Doji Force B + strong C rule (Pine default true).</summary>
    public bool   UseDojiForceBC     { get; set; } = true;
    /// <summary>A4: doji body max ratio (Pine default 0.25).</summary>
    public double DojiBodyMaxRatio       { get; set; } = 0.25;
    /// <summary>A4: doji force wick min ratio (Pine default 0.55).</summary>
    public double DojiForceWickRatioMin  { get; set; } = 0.55;
    /// <summary>A4: doji force body zone max (Pine default 0.45).</summary>
    public double DojiForceBodyZoneMax   { get; set; } = 0.45;
    /// <summary>A4: wick dominance ratio (Pine default 1.5).</summary>
    public double DojiWickDomRatio       { get; set; } = 1.5;

    // ── Output record ─────────────────────────────────────────────────────────
    /// <param name="IsGap">Pine <c>_batchGap</c> / <c>f_push(isGapMerge)</c> — keylevel ref lookback=0 when true.</param>
    public readonly record struct PivotBatch(int Bar, double Price, int Type, string Reason, bool IsGap = false);

    /// <summary>
    /// Debug diagnostic for a single pending candidate resolved this bar.
    /// Status values: "EXP_NO_B", "EXP_NO_CD", "EXP_OVF".
    /// Confirmed/pushed candidates are tracked via <see cref="PivotBatch.Reason"/> instead.
    /// </summary>
    public readonly record struct SwingCandidateDiag(
        int    CandBar,
        double CandPrice,
        int    Type,       // 1=HIGH, -1=LOW
        string Status,     // EXP_NO_B | EXP_NO_CD | EXP_OVF | ...
        int    BBar,       // -1 if no B was ever found
        string Detail = "");

    // =========================================================================
    // Step 1 — push bar A (call before micro-swing Step 1.5).
    // =========================================================================
    public void PushStep1(
        int    barIndex,
        bool   effBull,
        bool   effBear,
        int    effTier,
        bool   calcIsGap,
        double pivCandHigh,
        double pivCandLow,
        double prevPivCandHigh = double.NaN,  // Gap 1.1: Pine highUsed[1] = cleanHigh of bar before A
        double prevPivCandLow  = double.NaN)  // Gap 1.1: Pine lowUsed[1]  = cleanLow  of bar before A
    {
        var aBar = barIndex - 1;

        if (!double.IsNaN(pivCandHigh) && !QueueAlreadyHasBar(_penH, aBar))
        {
            int initBBar  = effBear && effTier >= 1 ? aBar : -1;
            int initBTier = effBear && effTier >= 1 ? effTier : 0;
            _penH.Push(new PendingSwingEntry
            {
                Bar = aBar, Price = pivCandHigh, BBar = initBBar, BTier = initBTier,
                IsGap = calcIsGap, PrevCleanHigh = prevPivCandHigh
            });
            PeakMissTracker?.NoteQueued(aBar, pivCandHigh, initBBar);
        }
        else if (PeakMissTracker != null && aBar >= 0)
        {
            PeakMissTracker.NoteNoCandidate(aBar, double.NaN, "WICK");
        }
        if (!double.IsNaN(pivCandLow) && !QueueAlreadyHasBar(_penL, aBar))
        {
            int initBBar  = effBull && effTier >= 1 ? aBar : -1;
            int initBTier = effBull && effTier >= 1 ? effTier : 0;
            _penL.Push(new PendingSwingEntry
            {
                Bar = aBar, Price = pivCandLow, BBar = initBBar, BTier = initBTier,
                IsGap = calcIsGap, PrevCleanLow = prevPivCandLow
            });
        }
    }

    static bool QueueAlreadyHasBar(PendingSwingQueue queue, int aBar) =>
        queue.Count > 0 && queue[queue.Count - 1].Bar == aBar;

    // =========================================================================
    // Steps 2–4 — evaluate + batch + expire (call after micro-swing Step 1.5).
    // =========================================================================
    public List<PivotBatch> EvaluateAndBatch(
        int    barIndex,
        int    lastPushedSwingType,
        double effClose,
        bool   effBull,
        bool   effBear,
        bool   effIsDoji,
        bool   effClearBody,
        int    effTier,
        Func<int, (double open, double high, double low, double close)> ohlcAtLag,
        bool   useWickNoiseFilter = false,
        bool   bLag2UpperWickNeutralized = false,
        bool   bLag2LowerWickNeutralized = false,
        List<SwingCandidateDiag>? diags = null)
    {
        // ── Step 2: scan B + evaluate A1/A2/A3/A4 ─────────────────────────────
        var hConfirmedIdx = new List<int>();
        var hConfirmedRsn = new List<string>();
        EvaluateHighCandidates(barIndex, effClose, effBull, effBear, effClearBody,
            effIsDoji, effTier, ohlcAtLag, hConfirmedIdx, hConfirmedRsn,
            useWickNoiseFilter, bLag2UpperWickNeutralized);

        var lConfirmedIdx = new List<int>();
        var lConfirmedRsn = new List<string>();
        EvaluateLowCandidates(barIndex, effClose, effBull, effBear, effClearBody,
            effIsDoji, effTier, ohlcAtLag, lConfirmedIdx, lConfirmedRsn,
            useWickNoiseFilter, bLag2LowerWickNeutralized);

        // ── Step 3: batch queue ────────────────────────────────────────────────
        var batch = new List<PivotBatch>();

        if (hConfirmedIdx.Count > 0 && lastPushedSwingType != 1)
        {
            int hWinIdx = AddBatch(batch, _penH, hConfirmedIdx, hConfirmedRsn, 1, extreme: UseExtremePickRule);

            int hWBar = WinBar(_penH, hWinIdx, hConfirmedIdx, type: 1);
            RecordHighBatchPeakMiss(hWinIdx, hConfirmedIdx, hConfirmedRsn, hWBar);

            if (diags != null)
            {
                var hConfirmedSet = new HashSet<int>(hConfirmedIdx);
                for (int qi = 0; qi < _penH.Count; qi++)
                {
                    var e = _penH[qi];
                    if (qi == hWinIdx) continue;
                    if (hConfirmedSet.TryGetValue(qi, out _))
                    {
                        int ci = hConfirmedIdx.IndexOf(qi);
                        diags.Add(new SwingCandidateDiag(
                            e.Bar, e.Price, 1,
                            $"CONF_NOT_PICKED:{hConfirmedRsn[ci]}",
                            e.BBar));
                    }
                    else
                    {
                        diags.Add(new SwingCandidateDiag(
                            e.Bar, e.Price, 1, "CLEARED_UNCONF", e.BBar,
                            $"HIGH batch win #{hWBar}"));
                    }
                }
                for (int qi = 0; qi < _penL.Count; qi++)
                {
                    var e = _penL[qi];
                    diags.Add(new SwingCandidateDiag(
                        e.Bar, e.Price, -1, "CLEARED_OPP_BATCH", e.BBar,
                        $"HIGH batch win #{hWBar}"));
                }
            }
            // Pine: clear CẢ 2 mảng sau khi HIGH confirm
            _penH.Clear();
            _penL.Clear();
        }
        else if (hConfirmedIdx.Count > 0 && lastPushedSwingType == 1)
        {
            RecordHighAltBlocked(hConfirmedIdx);
        }

        // Pine runs LOW as a separate if-block; guard _penL.Count in case HIGH already cleared it.
        if (lConfirmedIdx.Count > 0 && lastPushedSwingType != -1 && _penL.Count > 0)
        {
            int lWinIdx = AddBatch(batch, _penL, lConfirmedIdx, lConfirmedRsn, -1, extreme: UseExtremePickRule);

            int lWBar = WinBar(_penL, lWinIdx, lConfirmedIdx, type: -1);
            RecordLowBatchClearsHighCandidates(lWBar);

            if (diags != null)
            {
                var lConfirmedSet = new HashSet<int>(lConfirmedIdx);
                for (int qi = 0; qi < _penL.Count; qi++)
                {
                    var e = _penL[qi];
                    if (qi == lWinIdx) continue;
                    if (lConfirmedSet.TryGetValue(qi, out _))
                    {
                        int ci = lConfirmedIdx.IndexOf(qi);
                        diags.Add(new SwingCandidateDiag(
                            e.Bar, e.Price, -1,
                            $"CONF_NOT_PICKED:{lConfirmedRsn[ci]}",
                            e.BBar));
                    }
                    else
                    {
                        diags.Add(new SwingCandidateDiag(
                            e.Bar, e.Price, -1, "CLEARED_UNCONF", e.BBar,
                            $"LOW batch win #{lWBar}"));
                    }
                }
                for (int qi = 0; qi < _penH.Count; qi++)
                {
                    var e = _penH[qi];
                    diags.Add(new SwingCandidateDiag(
                        e.Bar, e.Price, 1, "CLEARED_OPP_BATCH", e.BBar,
                        $"LOW batch win #{lWBar}"));
                }
            }
            // Pine: clear CẢ 2 mảng sau khi LOW confirm
            _penL.Clear();
            _penH.Clear();
        }

        // ── Step 4: expire stale candidates, collecting diags if debug enabled ─
        ExpireQueue(_penH, barIndex, 1, diags);
        ExpireQueue(_penL, barIndex, -1, diags);

        return batch;
    }

    /// <summary>Full filter tick (Step 1 + micro hook + Steps 2–4) — convenience wrapper.</summary>
    public List<PivotBatch> Tick(
        int    barIndex,
        int    lastPushedSwingType,
        double effClose,
        bool   effBull,
        bool   effBear,
        bool   effIsDoji,
        bool   effClearBody,
        int    effTier,
        bool   calcIsGap,
        double pivCandHigh,
        double pivCandLow,
        Func<int, (double open, double high, double low, double close)> ohlcAtLag,
        double prevPivCandHigh = double.NaN,
        double prevPivCandLow  = double.NaN,
        List<SwingCandidateDiag>? diags = null,
        bool useWickNoiseFilter = false,
        bool bLag2UpperWickNeutralized = false,
        bool bLag2LowerWickNeutralized = false)
    {
        PushStep1(barIndex, effBull, effBear, effTier, calcIsGap, pivCandHigh, pivCandLow,
            prevPivCandHigh, prevPivCandLow);
        return EvaluateAndBatch(barIndex, lastPushedSwingType, effClose, effBull, effBear,
            effIsDoji, effClearBody, effTier, ohlcAtLag,
            useWickNoiseFilter, bLag2UpperWickNeutralized, bLag2LowerWickNeutralized, diags);
    }

    public void Reset()
    {
        _penH.Clear();
        _penL.Clear();
    }

    // =========================================================================
    // HIGH candidates (A = potential swing HIGH; B must be bearish strong/medium)
    // =========================================================================
    void EvaluateHighCandidates(
        int barIndex, double effClose,
        bool effBull, bool effBear, bool effClearBody, bool effIsDoji, int effTier,
        Func<int, (double open, double high, double low, double close)> ohlcAtLag,
        List<int> confirmedIdx, List<string> confirmedRsn,
        bool useWickNoiseFilter = false,
        bool bLag2UpperWickNeutralized = false)
    {
        for (int i = 0; i < _penH.Count; i++)
        {
            var e = _penH[i];

            // Scan B (if not yet found)
            if (e.BBar == -1 && effBear && effTier >= 1)
            {
                _penH.Set(i, new PendingSwingEntry
                {
                    Bar = e.Bar, Price = e.Price, BBar = barIndex - 1, BTier = effTier, IsGap = e.IsGap
                });
                e = _penH[i];
            }

            if (e.BBar == -1) continue;   // still no B

            bool   confirmed = false;
            string rsn       = "";

            // lagB = lag of B from current eval bar (bar_index-1);
            // B's OHLC = ohlcAtLag(lagB + 1) because running bar is barIndex (lag 0).
            int lagB = (barIndex - 1) - e.BBar;

            // ── A1: B strong, C immediately follows B ─────────────────────────
            if (!confirmed && e.BTier == 2 && (barIndex - 1) == e.BBar + 1)
            {
                double thr  = OneThirdBearish(lagB + 1, ohlcAtLag);
                bool bBodyOk = BBodyLowOk(lagB, ohlcAtLag);
                if (effClose < thr && bBodyOk) { confirmed = true; rsn = "A1"; }
            }

            // ── A2: B medium, D = 2 bars after B ─────────────────────────────
            if (!confirmed && e.BTier == 1 && (barIndex - 1) == e.BBar + 2)
            {
                double thr  = OneThirdBearish(lagB + 1, ohlcAtLag);
                bool bBodyOk = BBodyLowOk(lagB, ohlcAtLag);
                var (_, _, _, closeC) = ohlcAtLag(2);  // C = lag 2 from barIndex
                if (closeC < thr && effClose < thr && bBodyOk) { confirmed = true; rsn = "A2"; }
            }

            // ── A3: B medium/strong, C after B, within CDx window ────────────
            // Pine: C bearish + effClearBody + non-doji (gap-merged XY body when calcIsGap).
            if (!confirmed && e.BTier >= 1
                && (barIndex - 1) > e.BBar
                && (barIndex - 1) - e.Bar <= CdMaxLagBars)
            {
                if (effBear && effClearBody && !effIsDoji)
                {
                    var (_, _, bLow, _) = ohlcAtLag(lagB + 1);
                    if (effClose < bLow) { confirmed = true; rsn = "A3"; }
                }
            }

            // ── A4: doji sell-force at lag 2, C = calc bar strong bear ────────
            // Pine: block if B (lag 2) upper wick was neutralized — neutralizedHighPrev[1]
            if (UseDojiForceBC && !confirmed
                && barIndex - 2 >= e.Bar && (barIndex - 2) - e.Bar <= BMaxLagBars
                && (barIndex - 1) - e.Bar <= CdMaxLagBars)
            {
                bool bIsWickBarH = useWickNoiseFilter && bLag2UpperWickNeutralized;
                var (dO, dH, dL, dC) = ohlcAtLag(2);
                if (!bIsWickBarH && IsDojiSellForce(dO, dH, dL, dC) && effBear && effTier == 2)
                {
                    confirmed = true;
                    rsn = "A4_DOJI_SELL";
                }
            }

            if (confirmed) { confirmedIdx.Add(i); confirmedRsn.Add(rsn); }
        }
    }

    // =========================================================================
    // LOW candidates (A = potential swing LOW; B must be bullish strong/medium)
    // =========================================================================
    void EvaluateLowCandidates(
        int barIndex, double effClose,
        bool effBull, bool effBear, bool effClearBody, bool effIsDoji, int effTier,
        Func<int, (double open, double high, double low, double close)> ohlcAtLag,
        List<int> confirmedIdx, List<string> confirmedRsn,
        bool useWickNoiseFilter = false,
        bool bLag2LowerWickNeutralized = false)
    {
        for (int i = 0; i < _penL.Count; i++)
        {
            var e = _penL[i];

            if (e.BBar == -1 && effBull && effTier >= 1)
            {
                _penL.Set(i, new PendingSwingEntry
                {
                    Bar = e.Bar, Price = e.Price, BBar = barIndex - 1, BTier = effTier, IsGap = e.IsGap
                });
                e = _penL[i];
            }

            if (e.BBar == -1) continue;

            bool   confirmed = false;
            string rsn       = "";

            int lagB = (barIndex - 1) - e.BBar;

            // ── A1: B strong, C immediately follows B ─────────────────────────
            if (!confirmed && e.BTier == 2 && (barIndex - 1) == e.BBar + 1)
            {
                double thr  = TwoThirdBullish(lagB + 1, ohlcAtLag);
                bool bBodyOk = BBodyHighOk(lagB, ohlcAtLag);
                if (effClose > thr && bBodyOk) { confirmed = true; rsn = "A1"; }
            }

            // ── A2: B medium, D = 2 bars after B ─────────────────────────────
            if (!confirmed && e.BTier == 1 && (barIndex - 1) == e.BBar + 2)
            {
                double thr  = TwoThirdBullish(lagB + 1, ohlcAtLag);
                bool bBodyOk = BBodyHighOk(lagB, ohlcAtLag);
                var (_, _, _, closeC) = ohlcAtLag(2);
                if (closeC > thr && effClose > thr && bBodyOk) { confirmed = true; rsn = "A2"; }
            }

            // ── A3: B medium/strong, C after B, within CDx window ────────────
            // Pine: C bullish + effClearBody + non-doji (gap-merged XY body when calcIsGap).
            if (!confirmed && e.BTier >= 1
                && (barIndex - 1) > e.BBar
                && (barIndex - 1) - e.Bar <= CdMaxLagBars)
            {
                if (effBull && effClearBody && !effIsDoji)
                {
                    var (_, bHigh, _, _) = ohlcAtLag(lagB + 1);
                    if (effClose > bHigh) { confirmed = true; rsn = "A3"; }
                }
            }

            // ── A4: doji buy-force at lag 2, C = calc bar strong bull ─────────
            // Pine: block if B (lag 2) lower wick was neutralized — neutralizedLowPrev[1]
            if (UseDojiForceBC && !confirmed
                && barIndex - 2 >= e.Bar && (barIndex - 2) - e.Bar <= BMaxLagBars
                && (barIndex - 1) - e.Bar <= CdMaxLagBars)
            {
                bool bIsWickBarL = useWickNoiseFilter && bLag2LowerWickNeutralized;
                var (dO, dH, dL, dC) = ohlcAtLag(2);
                if (!bIsWickBarL && IsDojiBuyForce(dO, dH, dL, dC) && effBull && effTier == 2)
                {
                    confirmed = true;
                    rsn = "A4_DOJI_BUY";
                }
            }

            if (confirmed) { confirmedIdx.Add(i); confirmedRsn.Add(rsn); }
        }
    }

    // ── Batch builder ─────────────────────────────────────────────────────────
    /// <summary>
    /// Builds the batch for one direction.
    /// Returns the queue index of the winner (extreme mode), or -1 (non-extreme: all confirmed pushed).
    /// </summary>
    static int AddBatch(
        List<PivotBatch> batch,
        PendingSwingQueue queue,
        List<int> confIdx,
        List<string> confRsn,
        int type,
        bool extreme)
    {
        if (queue.Count == 0) return -1;

        if (extreme)
        {
            int    winIdx   = 0;
            double winPrice = queue[0].Price;
            for (int ai = 1; ai < queue.Count; ai++)
            {
                bool better = type == 1 ? queue[ai].Price > winPrice : queue[ai].Price < winPrice;
                if (better) { winPrice = queue[ai].Price; winIdx = ai; }
            }
            string winRsn = "EXTREME";
            for (int ci = 0; ci < confIdx.Count; ci++)
                if (confIdx[ci] == winIdx) { winRsn = confRsn[ci]; break; }
            batch.Add(new PivotBatch(queue[winIdx].Bar, queue[winIdx].Price, type, winRsn, queue[winIdx].IsGap));
            return winIdx;
        }
        else
        {
            for (int ci = 0; ci < confIdx.Count; ci++)
            {
                int idx = confIdx[ci];
                batch.Add(new PivotBatch(queue[idx].Bar, queue[idx].Price, type, confRsn[ci], queue[idx].IsGap));
            }
            return -1;
        }
    }

    void ExpireQueue(PendingSwingQueue queue, int barIndex,
                     int type, List<SwingCandidateDiag>? diags)
    {
        for (int i = queue.Count - 1; i >= 0; i--)
        {
            var e   = queue[i];
            int age = (barIndex - 1) - e.Bar;
            bool expire = (e.BBar == -1 && age > BMaxLagBars)
                       || (e.BBar != -1 && age > CdMaxLagBars);
            if (expire)
            {
                var expStatus = e.BBar == -1 ? "EXP_NO_B" : "EXP_NO_CD";
                var detail = e.BBar == -1
                    ? $"age={age}>Bx{BMaxLagBars} no B"
                    : $"age={age}>CDx{CdMaxLagBars} B@{e.BBar}";
                diags?.Add(new SwingCandidateDiag(
                    e.Bar, e.Price, type,
                    expStatus,
                    e.BBar,
                    detail));
                if (type == 1)
                    PeakMissTracker?.NoteExpired(e.Bar, e.Price, expStatus, e.BBar, detail);
                queue.RemoveAt(i);
            }
        }
        if (queue.Count > CdMaxLagBars)
        {
            var countBefore = queue.Count;
            if (type == 1)
                PeakMissTracker?.NoteFifoSnapshot(queue, CdMaxLagBars);

            for (var qi = 1; qi < countBefore; qi++)
            {
                var mem = queue[qi];
                var memDetail = $"q{qi + 1}/{countBefore} {countBefore}>{CdMaxLagBars}";
                diags?.Add(new SwingCandidateDiag(mem.Bar, mem.Price, type, "FIFO_MEM", mem.BBar, memDetail));
            }
        }

        while (queue.Count > CdMaxLagBars)
        {
            var countBefore = queue.Count;
            var e = queue[0];
            var detail = $"FIFO {countBefore}>{CdMaxLagBars} oldest q1/{countBefore}";
            diags?.Add(new SwingCandidateDiag(e.Bar, e.Price, type, "EXP_OVF", e.BBar, detail));
            if (type == 1)
                PeakMissTracker?.NoteExpired(e.Bar, e.Price, "EXP_OVF", e.BBar, detail);
            queue.RemoveAt(0);
        }
    }

    void RecordHighBatchPeakMiss(
        int winIdx,
        List<int> confIdx,
        List<string> confRsn,
        int winBar)
    {
        if (PeakMissTracker == null || _penH.Count == 0) return;

        var winEntry = winIdx >= 0 ? _penH[winIdx] : default;
        var winPrice = winIdx >= 0 ? winEntry.Price : double.NaN;
        var winConfirmed = winIdx >= 0 && confIdx.Contains(winIdx);
        string WinRule()
        {
            if (winIdx < 0) return "MULTI";
            for (var ci = 0; ci < confIdx.Count; ci++)
                if (confIdx[ci] == winIdx) return confRsn[ci];
            return "EXT";
        }

        var winRule = WinRule();
        PeakMissTracker.NotePicked(winBar, winPrice, winRule);

        var confSet = new HashSet<int>(confIdx);
        for (var qi = 0; qi < _penH.Count; qi++)
        {
            var e = _penH[qi];
            if (qi == winIdx) continue;
            if (confSet.Contains(qi))
            {
                var ci = confIdx.IndexOf(qi);
                PeakMissTracker.NoteExtremeLoss(e.Bar, e.Price, confRsn[ci], winBar, winPrice, winConfirmed);
            }
            else
            {
                PeakMissTracker.NoteUnconfirmed(e.Bar, e.Price, e.BBar, "HIGH batch");
            }
        }
    }

    void RecordHighAltBlocked(List<int> confIdx)
    {
        if (PeakMissTracker == null) return;
        foreach (var qi in confIdx)
        {
            var e = _penH[qi];
            PeakMissTracker.NoteAltBlocked(e.Bar, e.Price);
        }
    }

    void RecordLowBatchClearsHighCandidates(int lowWinBar)
    {
        if (PeakMissTracker == null) return;
        for (var qi = 0; qi < _penH.Count; qi++)
        {
            var e = _penH[qi];
            PeakMissTracker.NoteClearedOpp(e.Bar, e.Price, lowWinBar);
        }
    }

    // ── Level helpers ─────────────────────────────────────────────────────────

    /// <summary>One-third level bearish: close[bLag] + (open[bLag] − close[bLag]) / 3.</summary>
    static double OneThirdBearish(int bLag, Func<int, (double, double, double, double)> ohlc)
    {
        var (o, _, _, c) = ohlc(bLag);
        return c + (o - c) / 3.0;
    }

    /// <summary>Two-thirds level bullish: open[bLag] + 2*(close[bLag] − open[bLag]) / 3.</summary>
    static double TwoThirdBullish(int bLag, Func<int, (double, double, double, double)> ohlc)
    {
        var (o, _, _, c) = ohlc(bLag);
        return o + 2.0 * (c - o) / 3.0;
    }

    /// <summary>B body bottom (bear): B.bodyBot &lt; prevB.bodyBot.</summary>
    static bool BBodyLowOk(int lagB, Func<int, (double, double, double, double)> ohlc)
    {
        var (bO, _, _, bC)   = ohlc(lagB + 1);
        var (b2O, _, _, b2C) = ohlc(lagB + 2);
        return Math.Min(bO, bC) < Math.Min(b2O, b2C);
    }

    /// <summary>B body top (bull): B.bodyTop &gt; prevB.bodyTop.</summary>
    static bool BBodyHighOk(int lagB, Func<int, (double, double, double, double)> ohlc)
    {
        var (bO, _, _, bC)   = ohlc(lagB + 1);
        var (b2O, _, _, b2C) = ohlc(lagB + 2);
        return Math.Max(bO, bC) > Math.Max(b2O, b2C);
    }

    // ── A4: doji force candle checks ──────────────────────────────────────────

    bool IsDojiSellForce(double open, double high, double low, double close)
    {
        double range = high - low;
        if (range <= 0) return false;
        double body      = Math.Abs(close - open);
        double upperWick = high - Math.Max(open, close);
        double lowerWick = Math.Min(open, close) - low;
        double bodyLowPos = (Math.Min(open, close) - low) / range;
        bool isDoji   = body / range <= DojiBodyMaxRatio;
        bool hasForce = upperWick / range >= DojiForceWickRatioMin;
        bool bodyZone = bodyLowPos <= DojiForceBodyZoneMax;
        bool wickDom  = DojiWickDomRatio <= 1.0 || lowerWick <= 0.0 || upperWick >= lowerWick * DojiWickDomRatio;
        return isDoji && hasForce && bodyZone && wickDom;
    }

    bool IsDojiBuyForce(double open, double high, double low, double close)
    {
        double range = high - low;
        if (range <= 0) return false;
        double body       = Math.Abs(close - open);
        double lowerWick  = Math.Min(open, close) - low;
        double upperWick  = high - Math.Max(open, close);
        double bodyHighPos = (high - Math.Max(open, close)) / range;
        bool isDoji   = body / range <= DojiBodyMaxRatio;
        bool hasForce = lowerWick / range >= DojiForceWickRatioMin;
        bool bodyZone = bodyHighPos <= DojiForceBodyZoneMax;
        bool wickDom  = DojiWickDomRatio <= 1.0 || upperWick <= 0.0 || lowerWick >= upperWick * DojiWickDomRatio;
        return isDoji && hasForce && bodyZone && wickDom;
    }

    /// <summary>
    /// Returns the bar index of the pushed swing winner.
    /// Extreme mode: winner is the entry at <paramref name="winIdx"/>.
    /// Non-extreme mode (winIdx == -1): uses the most extreme price among confirmed entries.
    /// </summary>
    static int WinBar(PendingSwingQueue queue, int winIdx, List<int> confIdx, int type)
    {
        if (winIdx >= 0)
            return queue[winIdx].Bar;

        // Non-extreme: find the most extreme price bar among all confirmed entries
        int bestIdx   = confIdx[0];
        double bestPr = queue[bestIdx].Price;
        for (int i = 1; i < confIdx.Count; i++)
        {
            int ci = confIdx[i];
            bool better = type == 1 ? queue[ci].Price > bestPr : queue[ci].Price < bestPr;
            if (better) { bestIdx = ci; bestPr = queue[ci].Price; }
        }
        return queue[bestIdx].Bar;
    }
}
