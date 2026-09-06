using System;
using System.Collections.Generic;
using TradeAlert.Core.Models.KeyLevel;

namespace TradeAlert.Core.Engines;

/// <summary>
/// OB lifecycle transitions — port của pine plot lines 1960-2050 (f_add_OB initial scan)
/// và lines 4659-4826 (visibility/update loop per bar).
///
/// State machine:
///   0 = Pending (chưa xét)
///   2 = ZIN (confirmed, valid)
///   3 = LOSE (invalid — chạm lần 2 hoặc không fullPierce)
///
/// UI (box drawing, label) hoàn toàn do cTrader host xử lý.
/// Core chỉ tính trạng thái + extending flag.
/// </summary>
public sealed class OBEngine
{
    // -----------------------------------------------------------------------
    // Pure candle + identity snapshot helpers (from Loop 3 — unchanged)
    // -----------------------------------------------------------------------

    public bool IsValidObCandle(
        int lag,
        double minBodyRatio,
        double maxDojiBodyRatio,
        Func<int, (double open, double high, double low, double close)> ohlcAtLag) =>
        KeyLevelEngine.IsValidObCandle(lag, minBodyRatio, maxDojiBodyRatio, ohlcAtLag);

    public bool TryCaptureObIdentity(ObPoolStore pool, int index, out ObIdentitySnapshot snap)
    {
        if (pool.Count == 0 || (uint)index >= (uint)pool.Count)
        {
            snap = default;
            return false;
        }

        var r = pool.GetRecord(index);
        snap = new ObIdentitySnapshot(
            index,
            r.PivotType,
            r.PivotHighId,
            r.PivotLowId,
            r.Number,
            r.Type,
            r.Bar);
        return true;
    }

    // -----------------------------------------------------------------------
    // Touch / fullPierce helpers (pure — port Pine lines 4666-4667)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Pine: typ==-1 → high&gt;obX and low&lt;obX; typ==1 → low&lt;obX and high&gt;obX
    /// </summary>
    public static bool IsTouch(int typ, double obX, double high, double low) =>
        typ == -1 ? (high > obX && low < obX)
                  : (low  < obX && high > obX);

    /// <summary>
    /// Pine fullPierce:
    ///   typ==-1 (green/support): close&gt;obX and high&gt;obX and low&lt;obX and open&lt;obX (bullish pierce)
    ///   typ== 1 (red/resist):    close&lt;obX and low&lt;obX and high&gt;obX and open&gt;obX (bearish pierce)
    /// </summary>
    public static bool IsFullPierce(
        int typ, double obX,
        double open, double high, double low, double close) =>
        typ == -1
            ? (close > obX && high > obX && low < obX && open < obX)
            : (close < obX && low  < obX && high > obX && open > obX);

    // -----------------------------------------------------------------------
    // Initial-scan state at OB creation (f_add_OB, lines 1960-2007)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Compute initial OB state when a new OB is pushed into the pool.
    /// Scans bars from (barOB+1) up to min(barIndex-2, barOB+obScanBars) — matches Pine's f_add_OB.
    /// Returns (state, count, lastProcessedBar) — mirrors Pine push logic.
    ///
    ///   state=0: still pending (no touch yet)
    ///   state=2: ZIN (fullPierce on first touch)
    ///   state=3: LOSE
    ///
    /// ohlcAtLag: offset from barIndex (lag 0 = current bar).
    /// </summary>
    public static (int state, int count, int lastProcessedBar) ComputeInitialObState(
        int  barIndex,
        int  barOB,
        int  typ,
        double xOB,
        int  obScanBars,
        Func<int, (double open, double high, double low, double close)> ohlcAtLag)
    {
        int state   = 0;
        int count   = 0;
        // Gap 4.7: Pine caps scan at barOB+obScanBars (not just barIndex-2)
        int maxScan = Math.Min(barIndex - 2, barOB + obScanBars);
        int totalBarsToScan = maxScan - barOB;
        int lastProcessedBar = barOB;

        if (barOB >= barIndex - 2)
        {
            // OB quá mới, chưa có bar nào có LTF
            return (state, count, lastProcessedBar);
        }

        for (var b = barOB + 1; b <= maxScan; b++)
        {
            var lag = barIndex - b;
            if (lag < 0 || lag > 180) continue;

            var (o, h, l, c) = ohlcAtLag(lag);
            var touch      = IsTouch(typ, xOB, h, l);
            var fullPierce = IsFullPierce(typ, xOB, o, h, l, c);

            if (!touch) continue;

            if (totalBarsToScan == 1)
                break; // chỉ 1 bar → để visibility loop xử lý sau

            if (fullPierce && state == 0)
            {
                state = 2; count = 1;
                lastProcessedBar = b;
                continue; // check lần 2
            }
            else if (state == 2)
            {
                state = 3; count = 2;
                lastProcessedBar = b;
                break;
            }
            else
            {
                state = 3; count = 1;
                lastProcessedBar = b;
                break;
            }
        }

        // Loop kết thúc mà không touch và đã quét hết
        if (lastProcessedBar == barOB && totalBarsToScan > 1)
            lastProcessedBar = maxScan;

        return (state, count, lastProcessedBar);
    }

    // -----------------------------------------------------------------------
    // Per-bar visibility / update transition (lines 4659-4826)
    // -----------------------------------------------------------------------

    /// <summary>Result of one per-bar OB lifecycle tick.</summary>
    public readonly record struct ObTickResult(
        int  NewState,
        bool ExtendingChanged,   // true if extending flipped from T→F
        bool BoxShouldBeCreated, // true when state transitions 0→2 (host should draw box)
        bool BoxShouldStop,      // true when state transitions 2→3 (host stops extending)
        bool LtfConfirmNeeded);  // true when state==2 and flagLtf==0 (host checks LTF)

    /// <summary>
    /// Evaluate one OB entry against the current bar (barIndex) at checkBar=barIndex-2.
    /// Mirrors Pine visibility loop lines 4659-4769.
    ///
    /// Parameters mirror ObRecord fields:
    ///   state      = obStates[i]
    ///   obX        = obXs[i]
    ///   typ        = obTypes[i]
    ///   flagLtf    = obFlagLTF[i]
    ///   lastChecked = obLastCheckedBars[i]
    ///   extending  = obExtending[i]
    ///
    /// Returns ObTickResult; host applies state/extending mutations to ObRecord.
    /// LTF confirm logic runs in host (requires LTF buffer — outside Core).
    /// </summary>
    public static ObTickResult Tick(
        int    barIndex,
        int    obBar,
        int    state,
        double obX,
        int    typ,
        int    flagLtf,
        int    lastChecked,
        bool   extending,
        Func<int, (double open, double high, double low, double close)> ohlcAtLag)
    {
        int  newState       = state;
        bool extChanged     = false;
        bool boxCreate      = false;
        bool boxStop        = false;
        bool ltfNeeded      = false;

        var checkBar = barIndex - 2;
        // Gap 4.8: Pine also requires checkBar > obBar (avoids re-checking the OB bar itself)
        if (checkBar <= lastChecked || checkBar <= obBar) goto ltfCheck;

        {
            var lag = barIndex - checkBar;
            if (lag >= 0 && lag <= 180)
            {
                var (o, h, l, c) = ohlcAtLag(lag);
                var touchNow      = IsTouch(typ, obX, h, l);
                var fullPierceNow = IsFullPierce(typ, obX, o, h, l, c);

                if (touchNow)
                {
                    if (fullPierceNow && state == 0)
                    {
                        // 0 → 2 (ZIN)
                        newState   = 2;
                        boxCreate  = true;
                        extending  = true;
                        // LTF confirm check happens in host after this
                    }
                    else if (state == 2 || !fullPierceNow)
                    {
                        // 2 → 3 or 0 → 3
                        int oldState = newState;
                        newState = 3;
                        if (oldState == 2 && extending)
                        {
                            extChanged = true;  // stop extending
                            boxStop    = true;
                        }
                    }
                }
            }
        }

    ltfCheck:
        // LTF confirm check: only when state==2 and flagLtf==0
        if (newState == 2 && flagLtf == 0)
            ltfNeeded = true;

        return new ObTickResult(newState, extChanged, boxCreate, boxStop, ltfNeeded);
    }

    // -----------------------------------------------------------------------
    // Pool trim logic (lines 2270-2396) — trim to maxCount, zin-first
    // -----------------------------------------------------------------------

    /// <summary>
    /// Trim OB pool to maxCount entries, preferring ZIN (state==2) over non-ZIN.
    /// Returns indices to REMOVE (back-to-front, caller calls pool.RemoveAt in reverse).
    /// Mirrors Pine trim logic lines 2270-2396.
    /// </summary>
    public static List<int> ComputeTrimIndices(ObPoolStore pool, int maxCount)
    {
        var n = pool.Count;
        if (n <= maxCount) return new List<int>();

        // Separate zin vs non-zin, sorted by bar descending (newest first)
        var zin    = new List<(int Bar, int Idx)>();
        var nonZin = new List<(int Bar, int Idx)>();

        for (var i = 0; i < n; i++)
        {
            var r = pool.GetRecord(i);
            if (r.State == 2) zin.Add((r.Bar, i));
            else              nonZin.Add((r.Bar, i));
        }

        zin.Sort((a, b) => b.Bar.CompareTo(a.Bar));
        nonZin.Sort((a, b) => b.Bar.CompareTo(a.Bar));

        var keepSet = new HashSet<int>();
        foreach (var (_, idx) in zin)
        {
            if (keepSet.Count >= maxCount) break;
            keepSet.Add(idx);
        }
        foreach (var (_, idx) in nonZin)
        {
            if (keepSet.Count >= maxCount) break;
            keepSet.Add(idx);
        }

        var toRemove = new List<int>();
        for (var i = 0; i < n; i++)
            if (!keepSet.Contains(i)) toRemove.Add(i);

        toRemove.Sort((a, b) => b.CompareTo(a)); // reverse order for safe removal
        return toRemove;
    }

    /// <summary>No-op API shim retained for compatibility. Use <see cref="Tick"/> and <see cref="ComputeInitialObState"/>.</summary>
    [Obsolete("Use OBEngine.Tick() and ComputeInitialObState().")]
    public void ObLifecycleTransitionNotMapped() { }
}
