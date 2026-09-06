using System;
using TradeAlert.Core.Drawing;
using TradeAlert.Core.Models.KeyLevel;

namespace TradeAlert.Core.Engines;

/// <summary>
/// Port of Pine <c>f_push</c> D-commit while-loop (lines 3989–4318),
/// <c>f_handle_main_c_after_promote</c> (MAIN_OLD sweep, lines 2855–2941),
/// and <c>f_lock_swings_before</c> (LOCKED state, lines 2947–2975).
///
/// Call <see cref="TryCommitD"/> after each new pivot is pushed to <see cref="PivotStateStore"/>.
/// </summary>
public static class MainPromotionEngine
{
    /// <summary>
    /// D-commit tolerance: new pivot bar must be >= breakBar(B) - dTolerance.
    /// Pine default = 3.
    /// </summary>
    public const int DefaultDTolerance   = 3;

    /// <summary>
    /// Distance in pivot-array positions before the new D to lock old swings.
    /// Pine <c>lockSwingCount</c> parameter, default 50.
    /// </summary>
    public const int DefaultLockSwingCount = 50;

    // ──────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Try to commit the newly pushed pivot at <paramref name="newPivotIdx"/> as D_SWING,
    /// find C, promote C to MAIN_C or MAIN_BROKEN, then run REBOOT BD and MAIN_OLD sweep.
    ///
    /// Returns true if promotion succeeded (promotedCIdx is valid).
    /// </summary>
    public static (bool Success, int PromotedCIdx, int DIdx) TryCommitD(
        int                  newPivotIdx,
        PivotStateStore      pivots,
        ObPoolStore?         obPool,
        IDrawingCommandSink? sink,
        PivotLabelRenderOptions? labelOpts = null,
        KeyLevelTickContext? keyCtx = null,
        bool                 isH4Chart = false,
        int                  dTolerance    = DefaultDTolerance,
        int                  lockSwingCount = DefaultLockSwingCount)
    {
        int promotedCIdx        = -1;
        bool promotedSuccessfully = false;

        // Local helper: mark B done + trigger MAIN C demote if B was a broken D_swing (Path A)
        void MarkBDoneAndDemote(int bIdx)
        {
            pivots.SetFirstDIdx(bIdx, PivotStateStore.FirstDDone);
            DemoteMainCsForBrokenDSwing(bIdx, pivots, obPool, sink, isH4Chart, labelOpts);
        }

        var dType = pivots.GetTypeAt(newPivotIdx);

        for (var j = 0; j < newPivotIdx && !promotedSuccessfully; j++)
        {
            var bFlag = pivots.GetFlag(j);

            // Skip BROKEN_FAKE and MAIN_FAKE
            if (bFlag == 6 || bFlag == 5) continue;

            // Skip ROOT D (D_SWING with rootHighId/LowId set)
            if (bFlag == 4)
            {
                var bType2 = pivots.GetTypeAt(j);
                bool isRootD = (bType2 == 1 && pivots.GetRootHighId(j) != 0)
                            || (bType2 == -1 && pivots.GetRootLowId(j) != 0);
                if (isRootD) continue;
            }

            // Skip if B already done
            var bFirstDState = pivots.GetFirstDIdx(j);
            if (bFirstDState == PivotStateStore.FirstDDone) continue;

            // Only process B that is BROKEN + waiting for D
            var dState = pivots.GetFirstDIdx(newPivotIdx);
            if (bFirstDState != PivotStateStore.FirstDWaiting) continue;
            if (bFlag != 2) continue;
            if (dState == PivotStateStore.FirstDRollback) continue;

            var bType = pivots.GetTypeAt(j);
            var bkBar = pivots.GetBreakBar(j);
            var dBar  = pivots.GetBarIndexInternal(newPivotIdx);

            // Pine lines 4028-4306: D phải cùng type B và trong tolerance bar
            bool dTimingOk = dType == bType
                          && bkBar >= 0
                          && dBar >= bkBar - dTolerance;

            if (!dTimingOk)
                continue;

            // ── STEP 1: Find C (pivot immediately after B) ──
            var C = FindMainCAfterB(pivots, j);

            if (C < 0)
            {
                MarkBDoneAndDemote(j);
                KeyLevelMaintenance.DeleteKeylevel(j, pivots, sink, obPool);
                continue;
            }

            // Guard: C is itself a DONE B
            if (pivots.GetFirstDIdx(C) == PivotStateStore.FirstDDone)
            {
                MarkBDoneAndDemote(j);
                continue;
            }

            // Invariant: D_SWING never becomes C
            if (pivots.GetFlag(C) == 4)
            {
                MarkBDoneAndDemote(j);
                continue;
            }

            // ── STEP 3: Commit D ──
            pivots.SetFirstDIdx(j, newPivotIdx);           // B → D
            pivots.SetFirstDIdx(newPivotIdx, j);           // D → B
            pivots.SetFlag(newPivotIdx, 4);
            pivots.SetOriginalFlag(newPivotIdx, 4);
            PivotTransitionEngine.EmitLabelUpdate(pivots, newPivotIdx, sink, labelOpts);
            if (keyCtx != null)
                KeyLevelSyncEngine.SyncKeyboxWithFlag(newPivotIdx, pivots, keyCtx, sink, obPool);
            else
                PivotTransitionEngine.EmitUpdateKeyBoxActive(pivots, newPivotIdx, sink, ActiveOpacity(keyCtx));

            // Lock old swings by fixed distance
            if (pivots.GetFlagBeforeLock(newPivotIdx) == PivotStateStore.FirstDNa)
            {
                var lockFromIdx = Math.Max(newPivotIdx - lockSwingCount, 0);
                LockSwingsBefore(pivots, obPool, lockFromIdx, sink, labelOpts);
            }

            // ── STEP 4: Promote C ──
            var noPromo = pivots.GetNoPromoteC(newPivotIdx);
            if (noPromo == PivotStateStore.NoPromoteCSet)
            {
                // FIX 2.4: D not allowed to promote C — only BROKEN B
                pivots.SetFlag(j, 2);
                pivots.SetOriginalFlag(j, 2);
                PivotTransitionEngine.EmitUpdateKeyBoxBroken(pivots, j, sink, BrokenOpacity(keyCtx));
                KeyLevelMaintenance.DeleteKeylevel(j, pivots, sink, obPool);
                pivots.SetNoPromoteC(newPivotIdx, PivotStateStore.NoPromoteCNa);
                MarkBDoneAndDemote(j);
                PivotTransitionEngine.EmitLabelUpdate(pivots, j, sink, labelOpts);
                continue;
            }

            var cFlag = pivots.GetFlag(C);

            // FAKE cannot be promoted
            if (cFlag == 5 || cFlag == 6)
            {
                MarkBDoneAndDemote(j);
                continue;
            }

            // C is BROKEN: must be in FirstDWaiting state
            bool canPromote = true;
            if (cFlag == 2)
            {
                var cDStatus = pivots.GetFirstDIdx(C);
                if (cDStatus != PivotStateStore.FirstDWaiting)
                {
                    canPromote = false;
                    pivots.SetFlag(j, 2);
                    pivots.SetOriginalFlag(j, 2);
                    PivotTransitionEngine.EmitUpdateKeyBoxBroken(pivots, j, sink, BrokenOpacity(keyCtx));
                    KeyLevelMaintenance.DeleteKeylevel(j, pivots, sink, obPool);
                    pivots.SetNoPromoteC(newPivotIdx, PivotStateStore.NoPromoteCNa);
                    PivotTransitionEngine.EmitLabelUpdate(pivots, j, sink, labelOpts);
                    // SetFirstDIdx(j, Done) + DemoteMainCsForBrokenDSwing handled by MarkBDoneAndDemote below
                }
            }

            if (!canPromote)
            {
                MarkBDoneAndDemote(j);
                continue;
            }

            // If C is BROKEN with a committed D → reset that D to ACTIVE
            if (cFlag == 2)
            {
                var cDIdx = pivots.GetFirstDIdx(C);
                if (cDIdx >= 0 && cDIdx < pivots.Count)
                {
                    pivots.SetFlag(cDIdx, 1);
                    pivots.SetOriginalFlag(cDIdx, 1);
                    PivotTransitionEngine.EmitLabelUpdate(pivots, cDIdx, sink);
                }
                pivots.SetFirstDIdx(C, PivotStateStore.FirstDNa);
            }

            // C is already MAIN (flag==0) → only BROKEN B, no reboot
            if (cFlag == 0)
            {
                if (pivots.GetFirstDIdx(C) == PivotStateStore.FirstDDone)
                {
                    continue;   // INVALID
                }
                pivots.SetFlag(j, 2);
                pivots.SetOriginalFlag(j, 2);
                PivotTransitionEngine.EmitUpdateKeyBoxBroken(pivots, j, sink, BrokenOpacity(keyCtx));
                PivotTransitionEngine.EmitLabelUpdate(pivots, j, sink);
                MarkBDoneAndDemote(j);
                continue;
            }

            // ── PROMOTE C → MAIN_C (0) or MAIN_BROKEN (-1) ──
            int newFlag = (cFlag == 2) ? -1 : 0;
            pivots.SetFlag(C, newFlag);
            pivots.SetOriginalFlag(C, newFlag);
            pivots.SetMainRole(C, 1);
            pivots.SetMainDIdx(C, newPivotIdx);

            if (newFlag == 0)
            {
                if (keyCtx != null)
                    KeyLevelSyncEngine.SyncKeyboxWithFlag(C, pivots, keyCtx, sink, obPool);
                else
                    PivotTransitionEngine.EmitUpdateKeyBoxMainC(pivots, C, sink, ActiveOpacity(keyCtx));
            }
            else
            {
                KeyLevelMaintenance.DeleteKeylevel(C, pivots, sink, obPool);
                KeyLevelMaintenance.DeleteRootBKeyOfMain(C, pivots, sink, obPool);
            }

            if (cFlag == 2)
            {
                KeyLevelMaintenance.DeleteKeylevel(C, pivots, sink, obPool);
                KeyLevelMaintenance.DeleteKeylevel(j, pivots, sink, obPool);
            }

            // Propagate structure metadata to C
            if (bType == 1) pivots.SetMainHighId(C, pivots.GetHighId(j));
            else             pivots.SetMainLowId(C, pivots.GetLowId(j));
            pivots.SetStructId(C, pivots.GetStructId(j));
            pivots.SetParent(C, j);

            // Reset C's waiting-D state (if was BROKEN)
            if (cFlag == 2)
            {
                var cFirstD = pivots.GetFirstDIdx(C);
                if (cFirstD != PivotStateStore.FirstDNa)
                {
                    if (cFirstD >= 0 && cFirstD < pivots.Count)
                    {
                        pivots.SetFlag(cFirstD, 1);
                        pivots.SetOriginalFlag(cFirstD, 1);
                        PivotTransitionEngine.EmitLabelUpdate(pivots, cFirstD, sink);
                    }
                    pivots.SetFirstDIdx(C, PivotStateStore.FirstDNa);
                }
            }

            PivotTransitionEngine.EmitLabelUpdate(pivots, C, sink);

            // ── REBOOT BD: reassign pivots between B and D ──
            for (var x = j + 1; x < newPivotIdx; x++)
            {
                if (x == C) continue;
                var xFlag = pivots.GetFlag(x);
                if (xFlag == -1 || xFlag == -3) continue;   // keep MAIN_BROKEN, MAIN_FAKE_BROKEN

                // Assign ROOT B' + keyGroupId
                var xType2 = pivots.GetTypeAt(x);
                if (bType == 1) pivots.SetRootHighId(x, pivots.GetHighId(j));
                else             pivots.SetRootLowId(x, pivots.GetLowId(j));
                pivots.SetKeyGroupId(x, j);

                if (xFlag == 4) continue;  // D_SWING → ROOT D, keep flag

                var xType = pivots.GetTypeAt(x);
                if (xType == bType)
                {
                    // BROKEN_FAKE
                    pivots.SetFlag(x, 6);
                    pivots.SetOriginalFlag(x, 6);
                    if (keyCtx != null)
                        KeyLevelSyncEngine.SyncKeyboxWithFlag(x, pivots, keyCtx, sink, obPool);
                    else
                        PivotTransitionEngine.EmitUpdateKeyBoxBroken(pivots, x, sink, BrokenOpacity(keyCtx));
                    PivotTransitionEngine.EmitLabelUpdate(pivots, x, sink, labelOpts);
                }
                else
                {
                    pivots.SetFlag(x, 5);
                    pivots.SetOriginalFlag(x, 5);
                    pivots.SetFirstDIdx(x, PivotStateStore.FirstDNa);
                    if (keyCtx != null)
                        KeyLevelSyncEngine.SyncKeyboxWithFlag(x, pivots, keyCtx, sink, obPool);
                    else
                        PivotTransitionEngine.EmitUpdateKeyBoxActive(pivots, x, sink, ActiveOpacity(keyCtx));
                    PivotTransitionEngine.EmitLabelUpdate(pivots, x, sink, labelOpts);
                }
            }

            MarkBDoneAndDemote(j);
            PivotTransitionEngine.EmitLabelUpdate(pivots, j, sink);

            promotedSuccessfully = true;
            promotedCIdx         = C;
        }

        if (promotedSuccessfully && promotedCIdx >= 0)
            HandleMainCAfterPromote(pivots, promotedCIdx, obPool, sink, isH4Chart, labelOpts);

        return (promotedSuccessfully, promotedCIdx, newPivotIdx);
    }

    // ──────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Port of Pine <c>f_handle_main_c_after_promote</c> (lines 2855–2941).
    /// Scans all MAIN C pivots of the same type; if their D_SWING is done → demote to MAIN_OLD (flag=8).
    /// Also: for any D_SWING at index d where pivot[d+2].flag==8 → delete D's keybox.
    /// </summary>
    static void HandleMainCAfterPromote(
        PivotStateStore pivots,
        int promotedCIdx,
        ObPoolStore? obPool,
        IDrawingCommandSink? sink,
        bool isH4Chart,
        PivotLabelRenderOptions? labelOpts)
    {
        var promotedType = pivots.GetTypeAt(promotedCIdx);
        var deleteObs = !isH4Chart;

        for (var i = 0; i < pivots.Count; i++)
        {
            if (i == promotedCIdx) continue;
            if (pivots.GetFlag(i) != 0) continue;
            if (pivots.GetTypeAt(i) != promotedType) continue;

            var dIdx2 = pivots.GetMainDIdx(i);
            var keepThisMainC = true;
            if (dIdx2 == PivotStateStore.FirstDNa || dIdx2 < 0 || dIdx2 >= pivots.Count)
                keepThisMainC = false;
            else if (pivots.GetFirstDIdx(dIdx2) == PivotStateStore.FirstDDone)
                keepThisMainC = false;

            if (keepThisMainC) continue;

            pivots.SetFlag(i, 8);
            pivots.SetOriginalFlag(i, 8);
            KeyLevelMaintenance.DeleteKeylevel(i, pivots, sink, obPool, deleteObs);
            PivotTransitionEngine.EmitLabelUpdate(pivots, i, sink, labelOpts);

            var bIdx2 = i - 1;
            if (bIdx2 >= 0)
                KeyLevelMaintenance.DeleteKeylevel(bIdx2, pivots, sink, obPool, deleteObs);
        }

        for (var d = 0; d < pivots.Count - 2; d++)
        {
            if (pivots.GetFlag(d) != 4) continue;
            if (pivots.GetFlag(d) == -2) continue;
            if (pivots.GetFlag(d + 2) != 8) continue;

            KeyLevelMaintenance.DeleteKeylevelInternal(d, pivots, sink, obPool, deleteObs);
            pivots.SetHasKey(d, false);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Port of new Pine <c>f_handle_main_c_for_broken_d_done</c>.
    /// Called when D_swing at <paramref name="brokenDIdx"/> reaches broken done (Path A or Path B).
    /// Scans all MAIN C (flag=0) whose <c>mainDIdx == brokenDIdx</c> and demotes them to MAIN_OLD (flag=8).
    /// Cleanup mirrors <see cref="HandleMainCAfterPromote"/>: delete key+OB (skip H4), delete B key, D-strip.
    /// </summary>
    public static void DemoteMainCsForBrokenDSwing(
        int brokenDIdx,
        PivotStateStore pivots,
        ObPoolStore? obPool,
        IDrawingCommandSink? sink,
        bool isH4Chart = false,
        PivotLabelRenderOptions? labelOpts = null)
    {
        var deleteObs = !isH4Chart;

        for (var i = 0; i < pivots.Count; i++)
        {
            if (pivots.GetFlag(i) != 0) continue;
            if (pivots.GetMainDIdx(i) != brokenDIdx) continue;

            pivots.SetFlag(i, 8);
            pivots.SetOriginalFlag(i, 8);
            KeyLevelMaintenance.DeleteKeylevel(i, pivots, sink, obPool, deleteObs);
            PivotTransitionEngine.EmitLabelUpdate(pivots, i, sink, labelOpts);

            var bIdx = i - 1;
            if (bIdx >= 0)
                KeyLevelMaintenance.DeleteKeylevel(bIdx, pivots, sink, obPool, deleteObs);
        }

        // D-strip: D_SWING d where pivot d+2 just became MAIN_OLD
        for (var d = 0; d < pivots.Count - 2; d++)
        {
            if (pivots.GetFlag(d) != 4) continue;
            if (pivots.GetFlag(d + 2) != 8) continue;

            KeyLevelMaintenance.DeleteKeylevelInternal(d, pivots, sink, obPool, deleteObs);
            pivots.SetHasKey(d, false);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Port of Pine <c>f_lock_swings_before</c> (lines 2947–2975).
    /// Sets all unlocked, non-FAKE pivots before <paramref name="beforeIdx"/> to flag=-2 (LOCKED).
    /// Also deletes their keyboxes + structure OBs.
    /// </summary>
    public static void LockSwingsBefore(
        PivotStateStore pivots,
        ObPoolStore? obPool,
        int beforeIdx,
        IDrawingCommandSink? sink,
        PivotLabelRenderOptions? labelOpts)
    {
        if (beforeIdx <= 0) return;

        var maxIdx = Math.Min(beforeIdx - 1, pivots.Count - 1);
        for (var i = 0; i <= maxIdx; i++)
        {
            var currentFlag = pivots.GetFlag(i);
            if (currentFlag == -2) continue;

            KeyLevelMaintenance.DeleteKeyBoxOnLock(i, pivots, sink, obPool);

            var isFake = currentFlag is 5 or 6 or 50 or -3;
            if (!isFake)
            {
                pivots.SetFlagBeforeLock(i, currentFlag);
                pivots.SetFlag(i, -2);
            }

            PivotTransitionEngine.EmitLabelUpdate(pivots, i, sink, labelOpts);
        }
    }

    /// <summary>
    /// Pine lines 4300-4306: BROKEN chờ D nhưng D sai type / quá sớm → B done, D về ACTIVE.
    /// </summary>
    // ──────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Port of Pine <c>f_find_main_C_after_B</c> (line 1306) + <c>f_is_valid_C_candidate</c> (line 1280).
    /// Returns index of C (pivot at brokenIdx+1) if valid, else -1.
    /// Valid: flag==1 (ACTIVE) OR (flag==2 AND firstDIdx==FirstDWaiting).
    /// </summary>
    static int FindMainCAfterB(PivotStateStore pivots, int brokenIdx)
    {
        var cIdx = brokenIdx + 1;
        if (cIdx >= pivots.Count) return -1;
        var f = pivots.GetFlag(cIdx);
        if (f == 1) return cIdx;
        if (f == 2 && pivots.GetFirstDIdx(cIdx) == PivotStateStore.FirstDWaiting) return cIdx;
        return -1;
    }

    // ──────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Detect HH (Higher High) / LL (Lower Low) for the newly pushed pivot.
    /// Pine lines 3925–3957: scan backward for previous same-type pivot.
    /// Sets <c>pivSubType</c> = 1 (HH) or -1 (LL) and emits updated label.
    /// </summary>
    public static void DetectHhLl(PivotStateStore pivots, int newIdx, IDrawingCommandSink? sink)
    {
        var typ   = pivots.GetTypeAt(newIdx);
        var price = pivots.GetPivotPrice(newIdx);

        double prevSamePrice = double.NaN;
        for (var ii = newIdx - 1; ii >= 0; ii--)
        {
            if (pivots.GetTypeAt(ii) == typ)
            {
                prevSamePrice = pivots.GetPivotPrice(ii);
                break;
            }
        }

        if (double.IsNaN(prevSamePrice)) return;

        if (typ == 1 && price > prevSamePrice)
        {
            pivots.SetSubType(newIdx, 1);
            PivotTransitionEngine.EmitLabelUpdate(pivots, newIdx, sink);
        }
        else if (typ == -1 && price < prevSamePrice)
        {
            pivots.SetSubType(newIdx, -1);
            PivotTransitionEngine.EmitLabelUpdate(pivots, newIdx, sink);
        }
    }

    static int ActiveOpacity(KeyLevelTickContext? ctx) =>
        ctx?.KeyOpacityActivePercent ?? PineColors.DefaultKeyOpacityActivePercent;

    static int BrokenOpacity(KeyLevelTickContext? ctx) =>
        ctx?.KeyOpacityBrokenPercent ?? PineColors.DefaultKeyOpacityBrokenPercent;
}
