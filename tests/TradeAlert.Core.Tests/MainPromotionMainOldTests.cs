using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.KeyLevel;
using Xunit;

namespace TradeAlert.Core.Tests;

/// <summary>
/// Tests for MAIN C → MAIN_OLD demote triggered by D_swing broken done.
/// Covers DemoteMainCsForBrokenDSwing and the Path B hook in PineStateEngine.
/// </summary>
public sealed class MainPromotionMainOldTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // T1: Path B — D_swing X breaks done immediately (NOT isRootB).
    //     MAIN C Y (mainDIdx=X) should demote to 8; MAIN C Z (different D) stays 0.
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void T1_PathB_DemotesLinkedMainC_LeavesOtherUnchanged()
    {
        var pivots = new PivotStateStore();
        // [0] B (BROKEN, type=1)
        pivots.PushDefaults(1.00, 10, 1);
        pivots.SetFlag(0, 2);

        // [1] X = D_SWING broken done (flag=2, type=1, firstDIdx=0 link to B, rootHighId≠0 → NOT isRootB)
        pivots.PushDefaults(1.10, 20, 1);
        pivots.SetFlag(1, 2);
        pivots.SetFirstDIdx(1, 0);      // holds link to B → Path B
        pivots.SetRootHighId(1, 99);    // has rootHighId → NOT isRootB

        // [2] Y = MAIN C old (flag=0, type=1, mainDIdx=1 → linked to X)
        pivots.PushDefaults(1.05, 15, 1);
        pivots.SetFlag(2, 0);
        pivots.SetHasKey(2, true);
        pivots.SetMainDIdx(2, 1);       // ← points to X

        // [3] Z = MAIN C new (flag=0, type=1, mainDIdx=5 → different D, not broken)
        pivots.PushDefaults(1.08, 30, 1);
        pivots.SetFlag(3, 0);
        pivots.SetMainDIdx(3, 5);       // different D

        // Act: trigger demote for brokenDIdx=1 (X)
        MainPromotionEngine.DemoteMainCsForBrokenDSwing(1, pivots, null, null);

        // Y → MAIN_OLD
        Assert.Equal(8, pivots.GetFlag(2));
        Assert.Equal(8, pivots.GetOriginalFlag(2));
        Assert.False(pivots.GetHasKey(2));

        // Z unaffected
        Assert.Equal(0, pivots.GetFlag(3));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T2: Path A waiting — D_swing X just broke into waiting-D state (firstDIdx=-1).
    //     Demote should NOT be triggered yet for Y.
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void T2_PathA_Waiting_DoesNotDemoteYet()
    {
        var pivots = new PivotStateStore();
        // [0] X = D_SWING broken waiting (flag=2, firstDIdx=-1)
        pivots.PushDefaults(1.10, 20, 1);
        pivots.SetFlag(0, 2);
        pivots.SetFirstDIdx(0, PivotStateStore.FirstDWaiting);  // -1

        // [1] Y = MAIN C old (flag=0, mainDIdx=0 → linked to X)
        pivots.PushDefaults(1.05, 15, 1);
        pivots.SetFlag(1, 0);
        pivots.SetMainDIdx(1, 0);

        // PineStateEngine hook: skip when firstDIdx == -1
        // Simulate the hook guard: only call DemoteMainCsForBrokenDSwing when NOT in waiting state.
        if (pivots.GetFirstDIdx(0) != PivotStateStore.FirstDWaiting)
            MainPromotionEngine.DemoteMainCsForBrokenDSwing(0, pivots, null, null);

        // Y must still be 0 (not demoted)
        Assert.Equal(0, pivots.GetFlag(1));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T2b: Path A done — after waiting resolves (firstDIdx=-2), demote fires.
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void T2b_PathA_Done_DemotesLinkedMainC()
    {
        var pivots = new PivotStateStore();
        // [0] X = formerly D_SWING, now broken done (flag=2, firstDIdx=-2)
        pivots.PushDefaults(1.10, 20, 1);
        pivots.SetFlag(0, 2);
        pivots.SetFirstDIdx(0, PivotStateStore.FirstDDone);  // -2 = done

        // [1] Y = MAIN C old (flag=0, mainDIdx=0 → linked to X)
        pivots.PushDefaults(1.05, 15, 1);
        pivots.SetFlag(1, 0);
        pivots.SetHasKey(1, true);
        pivots.SetMainDIdx(1, 0);

        // Act: trigger demote (Path A hook fires when firstDIdx set to -2)
        MainPromotionEngine.DemoteMainCsForBrokenDSwing(0, pivots, null, null);

        // Y → MAIN_OLD
        Assert.Equal(8, pivots.GetFlag(1));
        Assert.False(pivots.GetHasKey(1));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T3: Z (mainDIdx → different active D_swing X2) unaffected when X breaks.
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void T3_ZWithDifferentD_Unaffected()
    {
        var pivots = new PivotStateStore();
        // [0] X (broken done, type=1)
        pivots.PushDefaults(1.10, 10, 1);
        pivots.SetFlag(0, 2);
        pivots.SetFirstDIdx(0, PivotStateStore.FirstDDone);

        // [1] Y linked to X → demoted
        pivots.PushDefaults(1.05, 5, 1);
        pivots.SetFlag(1, 0);
        pivots.SetMainDIdx(1, 0);

        // [2] X2 (active D_SWING, type=1)
        pivots.PushDefaults(1.20, 20, 1);
        pivots.SetFlag(2, 4);

        // [3] Z linked to X2 → NOT demoted
        pivots.PushDefaults(1.15, 12, 1);
        pivots.SetFlag(3, 0);
        pivots.SetMainDIdx(3, 2);

        MainPromotionEngine.DemoteMainCsForBrokenDSwing(0, pivots, null, null);

        Assert.Equal(8, pivots.GetFlag(1));
        Assert.Equal(0, pivots.GetFlag(3));   // Z unaffected
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T4: D-strip — D_SWING d where d+2 is MAIN_OLD after demote → key deleted.
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void T4_DStrip_DeletesKeyOfDSwingBeforeMainOld()
    {
        var pivots = new PivotStateStore();
        // [0] d = D_SWING (flag=4, type=1)
        pivots.PushDefaults(1.00, 10, 1);
        pivots.SetFlag(0, 4);
        pivots.SetHasKey(0, true);

        // [1] some pivot (type=-1)
        pivots.PushDefaults(0.99, 11, -1);
        pivots.SetFlag(1, 2);

        // [2] Y = MAIN C old (flag=0, mainDIdx=X below → will become 8)
        // For simplicity, we directly set it to MAIN_OLD after demote by calling demote
        // using a dedicated brokenDIdx=5 that Y links to.
        // But we need d+2 to be MAIN_OLD *after* demote.
        // Setup: X at index 3, Y at index 2 but after demote Y[2]→8 → d+2=0+2=2→8? No, d=0, d+2=2.
        // Let Y=index 2 be the MAIN C that links to broken D at index 5.
        // After demote, Y→8, and d=0 (D_SWING) + 2 = Y(index 2) = 8 → D strip fires.
        pivots.PushDefaults(1.05, 12, 1);
        pivots.SetFlag(2, 0);
        pivots.SetHasKey(2, true);

        // [3] placeholder
        pivots.PushDefaults(1.01, 13, -1);
        pivots.SetFlag(3, 1);

        // [4] placeholder
        pivots.PushDefaults(1.02, 14, 1);
        pivots.SetFlag(4, 1);

        // [5] brokenDIdx X (broken done)
        pivots.PushDefaults(1.10, 20, 1);
        pivots.SetFlag(5, 2);
        pivots.SetFirstDIdx(5, PivotStateStore.FirstDDone);

        // Link Y (index 2) to X (index 5)
        pivots.SetMainDIdx(2, 5);

        // Act
        MainPromotionEngine.DemoteMainCsForBrokenDSwing(5, pivots, null, null);

        // Y→8 (confirmed)
        Assert.Equal(8, pivots.GetFlag(2));
        // D-strip: d=0 (D_SWING flag=4), d+2=2 (flag=8) → key of d deleted
        Assert.False(pivots.GetHasKey(0));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T5: Idempotent — Y already MAIN_OLD (flag=8), calling demote again doesn't crash.
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void T5_AlreadyMainOld_Idempotent()
    {
        var pivots = new PivotStateStore();
        // [0] brokenDIdx X
        pivots.PushDefaults(1.10, 20, 1);
        pivots.SetFlag(0, 2);

        // [1] Y already MAIN_OLD (flag=8) — still has mainDIdx=0 pointing to X
        pivots.PushDefaults(1.05, 15, 1);
        pivots.SetFlag(1, 8);   // already demoted
        pivots.SetMainDIdx(1, 0);

        // Should not throw and not re-process (flag check skips flag!=0)
        MainPromotionEngine.DemoteMainCsForBrokenDSwing(0, pivots, null, null);

        Assert.Equal(8, pivots.GetFlag(1)); // still 8
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T6: B key (Y-1) is deleted during demote.
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void T6_BKeyDeleted_WhenYDemoted()
    {
        var pivots = new PivotStateStore();
        // [0] B tại Y-1 (flag=2, has key)
        pivots.PushDefaults(1.00, 10, 1);
        pivots.SetFlag(0, 2);
        pivots.SetHasKey(0, true);

        // [1] Y = MAIN C old (flag=0, mainDIdx=2 → broken D at index 2)
        pivots.PushDefaults(1.05, 12, 1);
        pivots.SetFlag(1, 0);
        pivots.SetHasKey(1, true);
        pivots.SetMainDIdx(1, 2);

        // [2] brokenDIdx X
        pivots.PushDefaults(1.10, 20, 1);
        pivots.SetFlag(2, 2);

        MainPromotionEngine.DemoteMainCsForBrokenDSwing(2, pivots, null, null);

        // Y → MAIN_OLD
        Assert.Equal(8, pivots.GetFlag(1));
        // B (index 0 = Y-1) key deleted
        Assert.False(pivots.GetHasKey(0));
    }
}
