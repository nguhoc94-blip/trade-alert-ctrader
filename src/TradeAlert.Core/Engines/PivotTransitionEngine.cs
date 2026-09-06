using System;
using TradeAlert.Core.Drawing;
using TradeAlert.Core.Models.KeyLevel;

namespace TradeAlert.Core.Engines;

/// <summary>
/// Port of Pine <c>process_break</c> (lines 3312–3607).
/// Handles all flag transitions: ACTIVE/MAIN/D_SWING/MAIN_FAKE → PENDING → confirm/rollback.
///
/// Step A  (line 3352): first-break latch for flags {1,0,4,5} → PENDING {3,7,50}.
/// Step B  (line 3411): R1/R1'(M5)/R2/R3 evaluation + confirm/rollback for PENDING {3,7,50}.
///
/// Confirm semantics per <c>originalFlag</c>:
///   0 (MAIN)      → -1  (MAIN_BROKEN)  + cleanup ROOT B key + reset committed D + FIX2.3
///   5 (MAIN_FAKE) → -3  (MAIN_FAKE_BROKEN) + stop-extend keybox (not delete)
///   1 (ACTIVE)    →  2  (BROKEN) + firstDIdx=-1 if ROOT B + return 10
///   4 (D_SWING)   →  2  (BROKEN) + firstDIdx=-1 if ROOT B + return 1
/// </summary>
public sealed class PivotTransitionEngine
{
    public static void SnapshotPrevFlags(PivotStateStore pivots)
    {
        for (var i = 0; i < pivots.Count; i++)
            pivots.SetPrevFlag(i, pivots.GetFlag(i));
    }

    /// <summary>
    /// Tick all pivots for bar <paramref name="barIndex"/>.
    /// Returns aggregate result: <c>HasActiveBroken</c> (suppress new push this bar),
    /// <c>NeedStructIncrement</c> (structureIdCounter++).
    ///
    /// <paramref name="highUsed"/>/<paramref name="lowUsed"/> = Pine <c>cleanHigh1Raw/cleanLow1Raw</c>
    /// (wick-neutralized). Caller must pass these, not <c>ohlc1.High/Low</c>.
    ///
    /// <paramref name="rawClearBody"/>/<paramref name="rawIsDoji"/> = Pine <c>f_has_clear_body_structure_at(1)</c>
    /// and <c>f_bar_structure_is_doji_at(1)</c> on RAW ohlc1 (NOT eff-merged).
    /// Used for both Step A first-break latch and Step B R3 <c>cClear</c>.
    /// </summary>
    public BreakTickResult Tick(
        int             barIndex,
        PivotStateStore pivots,
        double          openUsed,
        double          highUsed,     // Pine cleanHigh1Raw (wick-neutralized)
        double          lowUsed,      // Pine cleanLow1Raw  (wick-neutralized)
        double          closeUsed,
        bool            rawClearBody, // Pine raw f_has_clear_body_structure_at(1)
        bool            rawIsDoji,    // Pine raw f_bar_structure_is_doji_at(1)
        bool            isM5,
        int             breakR3MaxK   = 3,
        IDrawingCommandSink? drawingSink = null,
        ObPoolStore?    obPool = null)
    {
        var windowMax        = Math.Max(2, breakR3MaxK);
        bool hasActiveBroken = false;
        bool needStructInc   = false;

        for (var i = 0; i < pivots.Count; i++)
        {
            var pivBar = pivots.GetBarIndexInternal(i);
            if (pivBar >= barIndex) continue;
            if (pivots.GetConfirmBar(i) >= 0) continue;

            // Skip if firstDIdx == -2 (fully processed)
            if (pivots.GetFirstDIdx(i) == PivotStateStore.FirstDDone) continue;

            var flag    = pivots.GetFlag(i);

            // Pine 3336–3337: skip MAIN_OLD, BROKEN_FAKE, MAIN_FAKE_BROKEN (lookback = pool trim, not break gate)
            if (flag is 8 or 6 or -3) continue;
            var pivType = pivots.GetTypeAt(i);
            var sPrice  = pivots.GetPivotPrice(i);

            // ---- STEP B: PENDING evaluation (flags 3, 7, 50) ----
            if (flag == 3 || flag == 7 || flag == 50)
            {
                var breakBar  = pivots.GetBreakBar(i);
                var storedS   = pivots.GetBreakSPrice(i);
                if (breakBar < 0 || double.IsNaN(storedS)) continue;

                var k = barIndex - breakBar;
                if (k <= 0) continue;

                var bHigh   = pivots.GetBreakBHigh(i);
                var bLow    = pivots.GetBreakBLow(i);
                var tier    = pivots.GetBreakTier(i);
                var r2Stage = pivots.GetBreakR2Stage(i);
                var r3Alive = pivots.GetBreakR3Alive(i);

                bool cBull       = closeUsed > openUsed;
                bool cBear       = closeUsed < openUsed;
                bool cColorMatch = (pivType == 1 && cBull) || (pivType == -1 && cBear);
                // Pine line 3431: f_has_clear_body_structure_at(1) — RAW ohlc1, not eff-merged
                bool cClear      = rawClearBody && !rawIsDoji;

                bool bodyPass = (pivType == 1 && openUsed > storedS && closeUsed > storedS)
                                || (pivType == -1 && openUsed < storedS && closeUsed < storedS);

                bool closeViolate = (pivType == 1 && closeUsed < storedS)
                                    || (pivType == -1 && closeUsed > storedS);

                bool eBeyondB = (!double.IsNaN(bHigh) && !double.IsNaN(bLow))
                    && ((pivType == 1 && closeUsed > bHigh) || (pivType == -1 && closeUsed < bLow));

                bool eligR1  = !isM5 && !double.IsNaN(tier) && tier >= 2.0 / 3.0;
                bool eligR1p = isM5;
                bool eligR2  = !isM5 && !double.IsNaN(tier) && tier >= 1.0 / 3.0 && tier < 2.0 / 3.0;
                const bool eligR3 = true;

                bool passR1  = eligR1  && k == 1 && bodyPass;
                bool passR1p = eligR1p && k == 1 && bodyPass;

                bool passR2 = false;
                if (eligR2)
                {
                    if (k == 1 && bodyPass)
                        pivots.SetBreakR2Stage(i, 1);
                    else if (k == 2 && r2Stage == 1)
                        passR2 = bodyPass;
                }

                bool passR3 = false;
                if (eligR3 && r3Alive && k >= 1 && k <= breakR3MaxK)
                    passR3 = cColorMatch && cClear && eBeyondB;

                if (r3Alive && closeViolate)
                    pivots.SetBreakR3Alive(i, false);

                bool confirmed = passR1 || passR1p || passR2 || passR3;

                if (confirmed)
                {
                    var origFlag = pivots.GetOriginalFlag(i);
                    pivots.SetConfirmBar(i, barIndex);

                    if (origFlag == 0)
                    {
                        // ===== MAIN → MAIN_BROKEN =====
                        var roleBroken = pivots.GetMainRole(i);
                        pivots.SetFlag(i, -1);
                        pivots.SetNeedNewStruct(i, true);
                        pivots.SetMainRole(i, 0);

                        KeyLevelMaintenance.DeleteKeylevel(i, pivots, drawingSink, obPool);
                        KeyLevelMaintenance.DeleteRootBKeyOfMain(i, pivots, drawingSink, obPool);

                        // Reset any committed D back to ACTIVE
                        var firstD = pivots.GetFirstDIdx(i);
                        if (firstD != PivotStateStore.FirstDNa)
                        {
                            if (firstD >= 0 && firstD < pivots.Count)
                            {
                                pivots.SetFlag(firstD, 1);
                                pivots.SetOriginalFlag(firstD, 1);
                            }
                            pivots.SetFirstDIdx(i, PivotStateStore.FirstDNa);
                        }

                        // FIX 2.3: if roleBroken == 0, find prev D_SWING same type and mark pivNoPromoteC = -1
                        if (roleBroken == 0)
                        {
                            var yIdx = FindPrevDSwingSameType(pivots, i, pivType);
                            if (yIdx >= 0)
                                pivots.SetNoPromoteC(yIdx, PivotStateStore.NoPromoteCSet);
                        }

                        // Now waiting for new D
                        pivots.SetFirstDIdx(i, PivotStateStore.FirstDWaiting);
                        needStructInc = true;

                        EmitLabelUpdate(pivots, i, drawingSink);
                    }
                    else if (origFlag == 5)
                    {
                        // ===== MAIN_FAKE → MAIN_FAKE_BROKEN =====
                        pivots.SetFlag(i, -3);
                        pivots.SetNeedNewStruct(i, false);
                        pivots.SetMainRole(i, 0);

                        // Stop-extend keybox (keep box but set right edge to breakBar time)
                        pivots.SetKeyStopBar(i, barIndex);
                        EmitStopExtendKeyBox(
                            pivots, i, barIndex, drawingSink,
                            rightEdgeBarIndex: pivots.GetBreakBar(i),
                            stopRightAtBreakBar: true);

                        KeyLevelMaintenance.DeleteRootBKeyOfMain(i, pivots, drawingSink, obPool);

                        // Reset committed D
                        var firstD5 = pivots.GetFirstDIdx(i);
                        if (firstD5 != PivotStateStore.FirstDNa)
                        {
                            if (firstD5 >= 0 && firstD5 < pivots.Count)
                            {
                                pivots.SetFlag(firstD5, 1);
                                pivots.SetOriginalFlag(firstD5, 1);
                            }
                            pivots.SetFirstDIdx(i, PivotStateStore.FirstDNa);
                        }

                        EmitLabelUpdate(pivots, i, drawingSink);
                        // return 1 (loop continues)
                    }
                    else
                    {
                        // ===== ACTIVE(1) or D_SWING(4) → BROKEN(2) =====
                        pivots.SetFlag(i, 2);
                        pivots.SetOriginalFlag(i, 2);

                        // Update keybox to dashed broken style
                        EmitUpdateKeyBoxBroken(pivots, i, drawingSink);

                        // ROOT B check: no rootHighId/LowId → this IS the root
                        bool isRootB = (pivType == 1 && pivots.GetRootHighId(i) == 0)
                                    || (pivType == -1 && pivots.GetRootLowId(i) == 0);
                        if (isRootB)
                            pivots.SetFirstDIdx(i, PivotStateStore.FirstDWaiting);

                        EmitLabelUpdate(pivots, i, drawingSink);

                        if (origFlag == 1)
                            hasActiveBroken = true;
                        // origFlag == 4 → return 1 (D_SWING BROKEN)
                    }
                    continue;
                }

                if (closeViolate || k >= windowMax)
                {
                    // ===== ROLLBACK =====
                    var prevFlag = pivots.GetFlagBeforePending(i);

                    // D_SWING with committed D rollback → mark rollback sentinel
                    if (prevFlag == 4)
                    {
                        var dState = pivots.GetFirstDIdx(i);
                        if (dState >= 0)
                            pivots.SetFirstDIdx(i, PivotStateStore.FirstDRollback);
                    }

                    // MAIN_FAKE_BREAK_PENDING rollback → restore to MAIN_FAKE explicitly
                    if (flag == 50)
                    {
                        pivots.SetFlag(i, 5);
                        pivots.SetOriginalFlag(i, 5);
                    }
                    else
                    {
                        pivots.SetFlag(i, prevFlag);
                    }

                    pivots.ResetBreakTracking(i);
                    EmitSyncKeyboxStyle(pivots, i, drawingSink);
                    EmitLabelUpdate(pivots, i, drawingSink);
                }
                continue;
            }

            // ---- STEP A: first-break latch for ACTIVE(1)/MAIN(0)/D_SWING(4)/MAIN_FAKE(5) ----
            if (flag != 1 && flag != 0 && flag != 4 && flag != 5) continue;
            // Pine line 3359: bClear = f_has_clear_body_structure_at(1) — RAW ohlc1, not eff-merged
            if (rawIsDoji || !rawClearBody) continue;
            if (!double.IsNaN(pivots.GetBreakClose(i))) continue;

            bool bBull = closeUsed > openUsed;
            bool bBear = closeUsed < openUsed;
            bool bColorMatch = (pivType == 1 && bBull) || (pivType == -1 && bBear);

            bool bodyCrossB = (pivType == 1 && openUsed <= sPrice && closeUsed > sPrice)
                           || (pivType == -1 && openUsed >= sPrice && closeUsed < sPrice);
            bool gapOverB   = (pivType == 1 && openUsed > sPrice && closeUsed > sPrice)
                           || (pivType == -1 && openUsed < sPrice && closeUsed < sPrice);
            double bBody = Math.Abs(closeUsed - openUsed);

            if (!(bColorMatch && (bodyCrossB || gapOverB) && bBody > 0)) continue;

            // Save original flag (never overwrite on confirm)
            pivots.SetFlagBeforePending(i, flag);
            pivots.SetOriginalFlag(i, flag);

            int pendingFlag;
            if (flag == 4)
            {
                // D_SWING: only break if NOT a ROOT D
                bool isRootD = (pivType == 1 && pivots.GetRootHighId(i) != 0)
                            || (pivType == -1 && pivots.GetRootLowId(i) != 0);
                if (isRootD) continue;  // ROOT D cannot be broken
                pendingFlag = 7;        // BREAK_PENDING_D
            }
            else if (flag == 5)
            {
                pendingFlag = 50;       // MAIN_FAKE_BREAK_PENDING
            }
            else
            {
                pendingFlag = 3;        // BREAK_PENDING (ACTIVE or MAIN)
            }

            pivots.SetFlag(i, pendingFlag);
            pivots.SetBreakClose(i, closeUsed);
            pivots.SetBreakBar(i, barIndex);
            pivots.SetBreakSPrice(i, sPrice);
            pivots.SetBreakBHigh(i, highUsed);
            pivots.SetBreakBLow(i, lowUsed);
            pivots.SetBreakDepthDist(i, ComputeBreakDepthDist(pivots, i));

            double tierRatio = pivType == 1
                ? (closeUsed - sPrice) / bBody
                : (sPrice - closeUsed) / bBody;
            pivots.SetBreakTier(i, tierRatio);
            pivots.SetBreakR2Stage(i, 0);
            pivots.SetBreakR3Alive(i, true);

            EmitLabelUpdate(pivots, i, drawingSink);
        }

        return new BreakTickResult(hasActiveBroken, needStructInc);
    }

    // ── Helper: emit label update via drawingSink ──────────────────────────────
    internal static void EmitLabelUpdate(
        PivotStateStore pivots, int i, IDrawingCommandSink? sink,
        PivotLabelRenderOptions? labelOpts = null)
    {
        if (labelOpts != null)
        {
            PivotLabelEngine.EmitLabelUpdate(pivots, i, sink, labelOpts);
            return;
        }

        if (sink == null) return;
        var typ    = pivots.GetTypeAt(i);
        var hid    = pivots.GetHighId(i);
        var lid    = pivots.GetLowId(i);
        var sid    = typ == 1 ? $"H{hid}" : $"L{lid}";
        var key    = $"sw_{sid}";
        var fn     = pivots.GetFlag(i);
        var subTyp = pivots.GetSubType(i);

        uint activeColor = subTyp == 1  ? PineColors.HighHH
                         : subTyp == -1 ? PineColors.LowLL
                         : typ == 1     ? PineColors.HighActive
                                        : PineColors.LowActive;

        var origFlag = pivots.GetOriginalFlag(i);
        (string text, uint color) = fn switch
        {
            1   => ($"ACTIVE {sid}",             activeColor),
            3   => origFlag == 0
                    ? ($"MAIN {sid} break?",      PineColors.MainYellow)
                    : ($"ACTIVE {sid}",           activeColor),
            7   => ($"D (pending) {sid}",         PineColors.DOrange),
            50  => ($"MAIN FAKE PENDING {sid}",   PineColors.MainYellow),
            2   => ($"BROKEN {sid}",              PineColors.BrokenGray),
            0   => ($"MAIN C {sid}",              PineColors.MainYellow),
            -1  => ($"MAIN BROKEN {sid}",         PineColors.MainBrokenRed),
            -3  => ($"MAIN FAKE BROKEN {sid}",    PineColors.DOrange),
            -2  => ("",                           0u),
            4   => ($"D {sid}",                   PineColors.DOrange),
            5   => ($"MAIN FAKE {sid}",           PineColors.FakeBlue),
            6   => ($"BROKEN FAKE {sid}",         PineColors.BrokenGray),
            8   => ($"MAIN_OLD {sid}",            PineColors.MainOldGray),
            _   => ($"{sid} [{fn}]",              activeColor),
        };

        if (color == 0) return;

        sink.Enqueue(new DrawingCommand
        {
            Kind        = DrawingCommandKind.AddLabel,
            LabelKeyStr = key,
            BarIndex    = pivots.GetBarIndexInternal(i),
            Price       = pivots.GetPivotPrice(i),
            IsHigh      = typ == 1,
            Text        = text,
            ColorArgb   = color,
        });
    }

    // ── KeyBox style emission helpers ──────────────────────────────────────────

    internal static void EmitDelKeyBox(
        PivotStateStore pivots, int i, IDrawingCommandSink? sink, ObPoolStore? obPool = null)
        => KeyLevelMaintenance.DeleteKeylevel(i, pivots, sink, obPool);

    /// <summary>
    /// Pine soft-hide (overlap): <c>box.delete</c> + <c>pivKeyBox=na</c> + <c>pivKeyVisible=false</c>;
    /// does not clear <c>pivHasKey</c>.
    /// </summary>
    internal static void EmitSoftHideKeyBox(PivotStateStore pivots, int i, IDrawingCommandSink? sink)
    {
        if (sink == null) return;
        var kb = pivots.GetKeyBox(i);
        if (kb?.Spec == null) return;
        var kbName = KeyLevelVisual.ChartObjectName(kb.Spec);
        sink.Enqueue(new DrawingCommand
        {
            Kind        = DrawingCommandKind.DelKeyBox,
            LabelKeyStr = kbName,
        });
        pivots.SetKeyBox(i, null);
        pivots.SetKeyVisible(i, false);
        pivots.SetKeyExtending(i, false);
    }

    internal static void EmitUpdateKeyBoxBroken(
        PivotStateStore pivots,
        int i,
        IDrawingCommandSink? sink,
        int brokenTranspPercent = PineColors.DefaultKeyOpacityBrokenPercent)
    {
        if (sink == null) return;
        var kb = pivots.GetKeyBox(i);
        if (kb?.Spec == null) return;
        var kbName = KeyLevelVisual.ChartObjectName(kb.Spec);
        var (fill, border) = KeyLevelVisual.BrokenColors(pivots.GetTypeAt(i), brokenTranspPercent);
        sink.Enqueue(new DrawingCommand
        {
            Kind              = DrawingCommandKind.UpdateKeyBox,
            LabelKeyStr       = kbName,
            ColorArgb         = fill,
            BorderColorArgb   = border,
            IsDashed          = true,
            BorderThickness   = KeyLevelVisual.BrokenBorderThickness,
        });
    }

    internal static void EmitUpdateKeyBoxMainC(
        PivotStateStore pivots,
        int i,
        IDrawingCommandSink? sink,
        int activeTranspPercent = PineColors.DefaultKeyOpacityActivePercent)
    {
        if (sink == null) return;
        var kb = pivots.GetKeyBox(i);
        if (kb?.Spec == null) return;
        var kbName = KeyLevelVisual.ChartObjectName(kb.Spec);
        var (fill, border) = KeyLevelVisual.MainCColors(activeTranspPercent);
        sink.Enqueue(new DrawingCommand
        {
            Kind            = DrawingCommandKind.UpdateKeyBox,
            LabelKeyStr     = kbName,
            ColorArgb       = fill,
            BorderColorArgb = border,
            IsMainC         = true,
        });
    }

    internal static void EmitUpdateKeyBoxActive(
        PivotStateStore pivots,
        int i,
        IDrawingCommandSink? sink,
        int activeTranspPercent = PineColors.DefaultKeyOpacityActivePercent)
    {
        if (sink == null) return;
        var kb = pivots.GetKeyBox(i);
        if (kb?.Spec == null) return;
        var kbName = KeyLevelVisual.ChartObjectName(kb.Spec);
        var (fill, border) = KeyLevelVisual.ActiveColors(pivots.GetTypeAt(i), activeTranspPercent);
        sink.Enqueue(new DrawingCommand
        {
            Kind            = DrawingCommandKind.UpdateKeyBox,
            LabelKeyStr     = kbName,
            ColorArgb       = fill,
            BorderColorArgb = border,
            IsDashed        = false,
        });
    }

    /// <param name="rightEdgeBarIndex">Pine bar index for <c>set_right</c> (<c>time</c> / <c>time[lag]</c>); host maps −1 for cTrader.</param>
    /// <param name="stopRightAtBreakBar">true = <paramref name="rightEdgeBarIndex"/>; false = <paramref name="currentBarIndex"/> (Pine <c>time</c> on key-break).</param>
    internal static void EmitStopExtendKeyBox(
        PivotStateStore pivots,
        int i,
        int currentBarIndex,
        IDrawingCommandSink? sink,
        int? rightEdgeBarIndex = null,
        bool stopRightAtBreakBar = true)
    {
        if (sink == null) return;
        var kb = pivots.GetKeyBox(i);
        if (kb?.Spec == null) return;
        var kbName = KeyLevelVisual.ChartObjectName(kb.Spec);
        var edgeBar = stopRightAtBreakBar ? rightEdgeBarIndex ?? currentBarIndex : currentBarIndex;
        sink.Enqueue(new DrawingCommand
        {
            Kind                 = DrawingCommandKind.StopExtendKeyBox,
            LabelKeyStr          = kbName,
            BarIndex             = edgeBar,
            StopRightAtBreakBar  = stopRightAtBreakBar,
        });
        pivots.SetKeyExtending(i, false);
    }

    internal static void EmitSyncKeyboxStyle(PivotStateStore pivots, int i, IDrawingCommandSink? sink)
    {
        if (sink == null) return;
        var flag = pivots.GetFlag(i);
        switch (flag)
        {
            case -1:
            case -2:
            case 8:
                EmitDelKeyBox(pivots, i, sink);
                break;
            case 3:
            case 7:
            case 50:
                // Rollback / re-sync: khôi phục style theo originalFlag (MAIN C = vàng)
                ApplyKeyStyleFromResolvedFlag(pivots, i, sink);
                break;
            default:
                ApplyKeyStyleFromResolvedFlag(pivots, i, sink);
                break;
        }
    }

    static void ApplyKeyStyleFromResolvedFlag(PivotStateStore pivots, int i, IDrawingCommandSink? sink)
    {
        var styleFlag = KeyLevelSyncEngine.ResolveKeyStyleFlag(pivots, i);
        switch (styleFlag)
        {
            case 0:
                EmitUpdateKeyBoxMainC(pivots, i, sink);
                break;
            case 2:
            case 6:
                EmitUpdateKeyBoxBroken(pivots, i, sink);
                break;
            case 1:
            case 4:
            case 5:
            case -3:
                EmitUpdateKeyBoxActive(pivots, i, sink);
                break;
        }
    }

    // ── Pine helper ports ──────────────────────────────────────────────────────

    /// <summary>Pine f_find_prev_d_swing_same_type — scan backward for flag==4 cùng type.</summary>
    internal static int FindPrevDSwingSameType(PivotStateStore pivots, int fromIdx, int typ)
    {
        for (var j = fromIdx - 1; j >= 0; j--)
            if (pivots.GetFlag(j) == 4 && pivots.GetTypeAt(j) == typ)
                return j;
        return -1;
    }

    /// <summary>
    /// Khoảng cách price từ mép entry (KeyBottom cho HIGH / KeyTop cho LOW) tới cực trị break.
    /// Gọi tại Step A latch khi keybox còn nguyên. NaN nếu không tính được.
    /// </summary>
    internal static double ComputeBreakDepthDist(PivotStateStore pivots, int i)
    {
        var spec = pivots.GetKeyBox(i)?.Spec;
        if (spec is null) return double.NaN;

        var keyTop = Math.Max(spec.Top, spec.Bottom);
        var keyBottom = Math.Min(spec.Top, spec.Bottom);
        var type = pivots.GetTypeAt(i);

        if (type == 1)
        {
            var d = pivots.GetBreakBHigh(i) - keyBottom;
            return d > 0 ? d : double.NaN;
        }

        if (type == -1)
        {
            var d = keyTop - pivots.GetBreakBLow(i);
            return d > 0 ? d : double.NaN;
        }

        return double.NaN;
    }
}

/// <summary>Result returned by <see cref="PivotTransitionEngine.Tick"/>.</summary>
public readonly struct BreakTickResult
{
    /// <summary>At least one ACTIVE pivot was BROKEN this bar.
    /// Caller should skip pushing new pivot (wait for D on next bar).</summary>
    public bool HasActiveBroken    { get; }
    /// <summary>At least one MAIN pivot was BROKEN. Caller should increment structureIdCounter.</summary>
    public bool NeedStructIncrement { get; }

    public BreakTickResult(bool hasActiveBroken, bool needStructIncrement)
    {
        HasActiveBroken     = hasActiveBroken;
        NeedStructIncrement = needStructIncrement;
    }
}

internal static class PivotStateStoreInternals
{
    /// <summary>Lấy bar_index của pivot — used in transition engine.</summary>
    public static int GetBarIndexInternal(this PivotStateStore p, int i)
    {
        var view = (IPivotReadOnlyView)p;
        return view.GetBarIndex(i);
    }

    public static double GetPriceInternal(this PivotStateStore p, int i)
    {
        var view = (IPivotReadOnlyView)p;
        return view.GetPrice(i);
    }
}
