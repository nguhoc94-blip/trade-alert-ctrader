using System;
using System.Collections.Generic;
using System.Linq;
using TradeAlert.Core.Engines;

namespace TradeAlert.Core.Models.KeyLevel;

/// <summary>Parallel pivot buffers — Pine port: f_push + process_break flag transitions (Loop 6).</summary>
public sealed class PivotStateStore : IPivotReadOnlyView, IPivotKeyBoxView
{
    // ---- Pine pivFirstDIdx sentinel values ----
    /// <summary>Pine na — initial default, pivot never waited for D.</summary>
    public const int FirstDNa       = int.MinValue;
    /// <summary>Pine -1 — BROKEN ROOT B now waiting for D commit.</summary>
    public const int FirstDWaiting  = -1;
    /// <summary>Pine -2 — B has finished D/promote processing, no retry.</summary>
    public const int FirstDDone     = -2;
    /// <summary>Pine -3 — D_SWING rollback marker after break rollback.</summary>
    public const int FirstDRollback = -3;

    /// <summary>Pine na for pivNoPromoteC — no restriction.</summary>
    public const int NoPromoteCNa  = int.MinValue;
    /// <summary>Pine -1 for pivNoPromoteC — D_swing must NOT promote C.</summary>
    public const int NoPromoteCSet = -1;
    readonly List<double> _pivPrice = new();
    readonly List<int> _pivIndex = new();
    readonly List<int> _pivType = new();
    readonly List<KeyBoxRef?> _pivKeyBox = new();
    readonly List<int> _pivTime = new();
    readonly List<int> _pivSubType = new();
    readonly List<int> _pivFlag = new();
    readonly List<int> _pivPrevFlag = new();
    readonly List<int> _pivOriginalFlag = new();
    readonly List<int> _pivFlagBeforePending = new();
    readonly List<double> _pivBreakClose = new();
    readonly List<int> _pivBreakBar = new();
    readonly List<double> _pivBreakSPrice = new();
    readonly List<double> _pivBreakBHigh = new();
    readonly List<double> _pivBreakBLow = new();
    /// <summary>Price distance entry-edge → break extreme, latched Step A. NaN = chưa broken / đã rollback.</summary>
    readonly List<double> _pivBreakDepthDist = new();
    readonly List<double> _pivBreakTier = new();
    readonly List<int> _pivBreakR2Stage = new();
    readonly List<bool> _pivBreakR3Alive = new();
    readonly List<int> _pivConfirmBar = new();
    readonly List<int> _pivParent = new();
    readonly List<int> _pivMainParent = new();
    readonly List<int> _pivKeyState = new();
    readonly List<bool> _pivNeedNewStruct = new();
    readonly List<int> _pivStructId = new();
    readonly List<bool> _pivKeyBreakLabeled = new();
    readonly List<int> _pivHighId = new();
    readonly List<int> _pivLowId = new();
    readonly List<int> _pivMainHighId = new();
    readonly List<int> _pivMainLowId = new();
    readonly List<int> _pivRootHighId = new();
    readonly List<int> _pivRootLowId = new();
    readonly List<int> _pivMainRole = new();
    readonly List<int> _pivFirstDIdx = new();
    readonly List<int> _pivFlagBeforeLock = new();
    readonly List<int>  _pivNoPromoteC = new();   // Pine pivNoPromoteC: FirstDNa=na, NoPromoteCSet=-1
    readonly List<int>  _pivKeyStopBar = new();   // Pine pivKeyStopBar: bar where key stopped extending
    readonly List<bool> _pivHasKey = new();
    readonly List<bool> _pivKeyVisible = new();
    readonly List<bool> _pivKeyExtending = new();
    readonly List<int> _pivKeyGroupId = new();
    readonly List<int> _pivMainDIdx = new();
    readonly List<string> _pivPushReason = new();
    /// <summary>Bar index of previous pivot for connecting line (<c>ln_{prev}_{bar}</c>). -1 if none.</summary>
    readonly List<int> _pivPrevConnectBar = new();

    public int Count => _pivPrice.Count;

    public void PushDefaults(
        double price,
        int barIndex,
        int type,
        int timeMs = 0)
    {
        _pivPrice.Add(price);
        _pivIndex.Add(barIndex);
        _pivType.Add(type);
        _pivKeyBox.Add(null);
        _pivTime.Add(timeMs);
        _pivSubType.Add(0);
        _pivFlag.Add(0);
        _pivPrevFlag.Add(0);
        _pivOriginalFlag.Add(0);
        _pivFlagBeforePending.Add(0);
        _pivBreakClose.Add(double.NaN);
        _pivBreakBar.Add(0);
        _pivBreakSPrice.Add(double.NaN);
        _pivBreakBHigh.Add(double.NaN);
        _pivBreakBLow.Add(double.NaN);
        _pivBreakDepthDist.Add(double.NaN);
        _pivBreakTier.Add(double.NaN);
        _pivBreakR2Stage.Add(0);
        _pivBreakR3Alive.Add(false);
        _pivConfirmBar.Add(-1);
        _pivParent.Add(-1);
        _pivMainParent.Add(-1);
        _pivKeyState.Add(0);
        _pivNeedNewStruct.Add(false);
        _pivStructId.Add(0);
        _pivKeyBreakLabeled.Add(false);
        _pivHighId.Add(0);
        _pivLowId.Add(0);
        _pivMainHighId.Add(0);
        _pivMainLowId.Add(0);
        _pivRootHighId.Add(0);
        _pivRootLowId.Add(0);
        _pivMainRole.Add(0);
        _pivFirstDIdx.Add(FirstDNa);      // Pine na — not waiting
        _pivFlagBeforeLock.Add(FirstDNa); // Pine na — never locked
        _pivNoPromoteC.Add(NoPromoteCNa); // Pine na — no restriction
        _pivHasKey.Add(false);
        _pivKeyVisible.Add(false);
        _pivKeyExtending.Add(false);
        _pivKeyGroupId.Add(FirstDNa);     // Pine na
        _pivMainDIdx.Add(FirstDNa);       // Pine na
        _pivKeyStopBar.Add(FirstDNa);     // Pine na
        _pivPushReason.Add("");
        _pivPrevConnectBar.Add(-1);
    }

    public void RemoveAt(int index)
    {
        if ((uint)index >= (uint)Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        _pivPrice.RemoveAt(index);
        _pivIndex.RemoveAt(index);
        _pivType.RemoveAt(index);
        _pivKeyBox.RemoveAt(index);
        _pivTime.RemoveAt(index);
        _pivSubType.RemoveAt(index);
        _pivFlag.RemoveAt(index);
        _pivPrevFlag.RemoveAt(index);
        _pivOriginalFlag.RemoveAt(index);
        _pivFlagBeforePending.RemoveAt(index);
        _pivBreakClose.RemoveAt(index);
        _pivBreakBar.RemoveAt(index);
        _pivBreakSPrice.RemoveAt(index);
        _pivBreakBHigh.RemoveAt(index);
        _pivBreakBLow.RemoveAt(index);
        _pivBreakDepthDist.RemoveAt(index);
        _pivBreakTier.RemoveAt(index);
        _pivBreakR2Stage.RemoveAt(index);
        _pivBreakR3Alive.RemoveAt(index);
        _pivConfirmBar.RemoveAt(index);
        _pivParent.RemoveAt(index);
        _pivMainParent.RemoveAt(index);
        _pivKeyState.RemoveAt(index);
        _pivNeedNewStruct.RemoveAt(index);
        _pivStructId.RemoveAt(index);
        _pivKeyBreakLabeled.RemoveAt(index);
        _pivHighId.RemoveAt(index);
        _pivLowId.RemoveAt(index);
        _pivMainHighId.RemoveAt(index);
        _pivMainLowId.RemoveAt(index);
        _pivRootHighId.RemoveAt(index);
        _pivRootLowId.RemoveAt(index);
        _pivMainRole.RemoveAt(index);
        _pivFirstDIdx.RemoveAt(index);
        _pivFlagBeforeLock.RemoveAt(index);
        _pivNoPromoteC.RemoveAt(index);
        _pivHasKey.RemoveAt(index);
        _pivKeyVisible.RemoveAt(index);
        _pivKeyExtending.RemoveAt(index);
        _pivKeyGroupId.RemoveAt(index);
        _pivMainDIdx.RemoveAt(index);
        _pivKeyStopBar.RemoveAt(index);
        _pivPushReason.RemoveAt(index);
        _pivPrevConnectBar.RemoveAt(index);
    }

    public bool InvariantAllColumnsSameCount()
    {
        var c = Count;
        return new[]
        {
            _pivIndex.Count, _pivType.Count, _pivKeyBox.Count, _pivTime.Count, _pivSubType.Count,
            _pivFlag.Count, _pivPrevFlag.Count, _pivOriginalFlag.Count, _pivFlagBeforePending.Count,
            _pivBreakClose.Count, _pivBreakBar.Count, _pivBreakSPrice.Count, _pivBreakBHigh.Count,
            _pivBreakBLow.Count, _pivBreakDepthDist.Count, _pivBreakTier.Count, _pivBreakR2Stage.Count, _pivBreakR3Alive.Count,
            _pivConfirmBar.Count, _pivParent.Count, _pivMainParent.Count, _pivKeyState.Count,
            _pivNeedNewStruct.Count, _pivStructId.Count, _pivKeyBreakLabeled.Count, _pivHighId.Count,
            _pivLowId.Count, _pivMainHighId.Count, _pivMainLowId.Count, _pivRootHighId.Count,
            _pivRootLowId.Count, _pivMainRole.Count, _pivFirstDIdx.Count, _pivFlagBeforeLock.Count,
            _pivNoPromoteC.Count, _pivHasKey.Count, _pivKeyVisible.Count, _pivKeyExtending.Count,
            _pivKeyGroupId.Count, _pivMainDIdx.Count, _pivKeyStopBar.Count, _pivPushReason.Count,
            _pivPrevConnectBar.Count
        }.All(x => x == c);
    }

    int IPivotReadOnlyView.Count => Count;

    int IPivotReadOnlyView.GetBarIndex(int i) => _pivIndex[i];
    double IPivotReadOnlyView.GetPrice(int i) => _pivPrice[i];
    int IPivotReadOnlyView.GetType(int i) => _pivType[i];

    bool IPivotReadOnlyView.TryGetKeyBoxRef(int i, out KeyBoxRef? keyBoxRef)
    {
        keyBoxRef = _pivKeyBox[i];
        return true;
    }

    public PivotSnapshot GetSnapshot(int i) =>
        new(i, _pivPrice[i], _pivIndex[i], _pivType[i], _pivKeyBox[i], _pivTime[i], _pivSubType[i], _pivFlag[i]);

    public void SetKeyBox(int i, KeyBoxRef? box) => _pivKeyBox[i] = box;

    // ---- Setters/getters for transition engine ----
    public int  GetFlag(int i)              => _pivFlag[i];
    public int  GetPrevFlag(int i)          => _pivPrevFlag[i];
    public void SetFlag(int i, int flag)    => _pivFlag[i] = flag;
    public void SetPrevFlag(int i, int f)   => _pivPrevFlag[i] = f;
    public KeyBoxRef? GetKeyBox(int i)      => _pivKeyBox[i];
    public int  GetTypeAt(int i)            => _pivType[i];
    public bool GetKeyExtending(int i)      => _pivKeyExtending[i];
    public void SetKeyExtending(int i, bool v) => _pivKeyExtending[i] = v;
    public int  GetHighId(int i)            => _pivHighId[i];
    public int  GetLowId(int i)             => _pivLowId[i];
    public double GetBreakClose(int i)      => _pivBreakClose[i];
    public void SetBreakClose(int i, double v) => _pivBreakClose[i] = v;
    public int  GetBreakBar(int i)          => _pivBreakBar[i];
    public void SetBreakBar(int i, int v)   => _pivBreakBar[i] = v;
    public int  GetConfirmBar(int i)        => _pivConfirmBar[i];
    public void SetConfirmBar(int i, int v) => _pivConfirmBar[i] = v;

    public int  GetFlagBeforePending(int i) => _pivFlagBeforePending[i];
    public void SetFlagBeforePending(int i, int v) => _pivFlagBeforePending[i] = v;

    public int  GetOriginalFlag(int i) => _pivOriginalFlag[i];
    public void SetOriginalFlag(int i, int v) => _pivOriginalFlag[i] = v;

    public double GetBreakSPrice(int i)       => _pivBreakSPrice[i];
    public void SetBreakSPrice(int i, double v) => _pivBreakSPrice[i] = v;

    public double GetBreakBHigh(int i)        => _pivBreakBHigh[i];
    public void SetBreakBHigh(int i, double v) => _pivBreakBHigh[i] = v;

    public double GetBreakBLow(int i)         => _pivBreakBLow[i];
    public void SetBreakBLow(int i, double v) => _pivBreakBLow[i] = v;

    /// <summary>Price distance từ mép entry (KeyBottom HIGH / KeyTop LOW) tới cực trị break. Chia cho pipSize → pip.</summary>
    public double GetBreakDepthDist(int i)         => _pivBreakDepthDist[i];
    public void SetBreakDepthDist(int i, double v) => _pivBreakDepthDist[i] = v;

    public double GetBreakTier(int i)         => _pivBreakTier[i];
    public void SetBreakTier(int i, double v) => _pivBreakTier[i] = v;

    public int GetBreakR2Stage(int i)         => _pivBreakR2Stage[i];
    public void SetBreakR2Stage(int i, int v) => _pivBreakR2Stage[i] = v;

    public bool GetBreakR3Alive(int i)        => _pivBreakR3Alive[i];
    public void SetBreakR3Alive(int i, bool v) => _pivBreakR3Alive[i] = v;

    /// <inheritdoc cref="PivotStateStoreInternals.GetPriceInternal" />
    public double GetPivotPrice(int i) => _pivPrice[i];

    /// <summary>Clear break-tracking fields sau rollback BREAK_PENDING.</summary>
    public void ResetBreakTracking(int i)
    {
        SetBreakClose(i, double.NaN);
        SetBreakBar(i, -1);
        SetBreakSPrice(i, double.NaN);
        SetBreakBHigh(i, double.NaN);
        SetBreakBLow(i, double.NaN);
        SetBreakDepthDist(i, double.NaN);
        SetBreakTier(i, double.NaN);
        SetBreakR2Stage(i, 0);
        SetBreakR3Alive(i, false);
    }

    /// <summary>Pullback / micro / bypass rule that confirmed this swing (Pine <c>_batchRsn</c>).</summary>
    public string GetPushReason(int i) => _pivPushReason[i];
    public void SetPushReason(int i, string reason) => _pivPushReason[i] = reason ?? "";

    /// <summary>Previous pivot bar for connecting line key <c>ln_{prev}_{bar}</c>.</summary>
    public int GetPrevConnectBar(int i) => _pivPrevConnectBar[i];
    public void SetPrevConnectBar(int i, int v) => _pivPrevConnectBar[i] = v;

    /// <summary>Push pivot mới với id (high/low). Trả về index mới push.</summary>
    public int PushPivot(
        double price, int barIndex, int type, int timeMs, int highId, int lowId,
        string? pushReason = null)
    {
        PushDefaults(price, barIndex, type, timeMs);
        var idx = Count - 1;
        _pivHighId[idx] = highId;
        _pivLowId[idx]  = lowId;
        _pivFlag[idx]   = 1; // ACTIVE
        _pivPrevFlag[idx] = 0;
        _pivOriginalFlag[idx] = 1;
        _pivFlagBeforePending[idx] = 1;
        _pivPushReason[idx] = pushReason ?? "";
        if (idx > 0)
            _pivPrevConnectBar[idx] = _pivIndex[idx - 1];
        return idx;
    }

    // ---- Getters/setters for MainPromotionEngine + PivotTransitionEngine ----
    public int  GetFirstDIdx(int i)              => _pivFirstDIdx[i];
    public void SetFirstDIdx(int i, int v)       => _pivFirstDIdx[i] = v;

    public int  GetMainRole(int i)               => _pivMainRole[i];
    public void SetMainRole(int i, int v)        => _pivMainRole[i] = v;

    public int  GetRootHighId(int i)             => _pivRootHighId[i];
    public void SetRootHighId(int i, int v)      => _pivRootHighId[i] = v;

    public int  GetRootLowId(int i)              => _pivRootLowId[i];
    public void SetRootLowId(int i, int v)       => _pivRootLowId[i] = v;

    public void SetHighId(int i, int v)          => _pivHighId[i] = v;
    public void SetLowId(int i, int v)           => _pivLowId[i] = v;

    public int  GetParent(int i)                 => _pivParent[i];
    public void SetParent(int i, int v)          => _pivParent[i] = v;

    public int  GetMainParent(int i)             => _pivMainParent[i];
    public void SetMainParent(int i, int v)      => _pivMainParent[i] = v;

    public int  GetStructId(int i)               => _pivStructId[i];
    public void SetStructId(int i, int v)        => _pivStructId[i] = v;

    public int  GetMainHighId(int i)             => _pivMainHighId[i];
    public void SetMainHighId(int i, int v)      => _pivMainHighId[i] = v;

    public int  GetMainLowId(int i)              => _pivMainLowId[i];
    public void SetMainLowId(int i, int v)       => _pivMainLowId[i] = v;

    public bool GetNeedNewStruct(int i)          => _pivNeedNewStruct[i];
    public void SetNeedNewStruct(int i, bool v)  => _pivNeedNewStruct[i] = v;

    /// <summary>Pine pivNoPromoteC: <see cref="NoPromoteCNa"/> = na (no restriction), <see cref="NoPromoteCSet"/> = -1 (block promotion).</summary>
    public int  GetNoPromoteC(int i)             => _pivNoPromoteC[i];
    public void SetNoPromoteC(int i, int v)      => _pivNoPromoteC[i] = v;

    public int  GetMainDIdx(int i)               => _pivMainDIdx[i];
    public void SetMainDIdx(int i, int v)        => _pivMainDIdx[i] = v;

    public int  GetSubType(int i)                => _pivSubType[i];
    public void SetSubType(int i, int v)         => _pivSubType[i] = v;

    /// <summary>Pine pivKeyGroupId: index of ROOT B that owns this FAKE/D_SWING. <see cref="FirstDNa"/> = na.</summary>
    public int  GetKeyGroupId(int i)             => _pivKeyGroupId[i];
    public void SetKeyGroupId(int i, int v)      => _pivKeyGroupId[i] = v;

    /// <summary>Pine pivFlagBeforeLock: original flag saved before LOCK. <see cref="FirstDNa"/> = na (not locked).</summary>
    public int  GetFlagBeforeLock(int i)         => _pivFlagBeforeLock[i];
    public void SetFlagBeforeLock(int i, int v)  => _pivFlagBeforeLock[i] = v;

    /// <summary>Pine pivKeyStopBar: bar_index where keybox stopped extending (MAIN_FAKE_BROKEN). <see cref="FirstDNa"/> = na.</summary>
    public int  GetKeyStopBar(int i)             => _pivKeyStopBar[i];
    public void SetKeyStopBar(int i, int v)      => _pivKeyStopBar[i] = v;

    public bool GetHasKey(int i)                 => _pivHasKey[i];
    public void SetHasKey(int i, bool v)         => _pivHasKey[i] = v;

    public bool GetKeyVisible(int i)             => _pivKeyVisible[i];
    public void SetKeyVisible(int i, bool v)     => _pivKeyVisible[i] = v;

    public bool GetKeyBreakLabeled(int i)        => _pivKeyBreakLabeled[i];
    public void SetKeyBreakLabeled(int i, bool v) => _pivKeyBreakLabeled[i] = v;

    public int  GetKeyState(int i)               => _pivKeyState[i];
    public void SetKeyState(int i, int v)        => _pivKeyState[i] = v;

    // ---- IPivotKeyBoxView impl ----
    KeyBoxRef? IPivotKeyBoxView.GetKeyBox(int i) => _pivKeyBox[i];
    int IPivotKeyBoxView.GetType(int i) => _pivType[i];
    int IPivotKeyBoxView.GetFlag(int i) => _pivFlag[i];
    bool IPivotKeyBoxView.GetHasKey(int i) => _pivHasKey[i];
    bool IPivotKeyBoxView.GetKeyExtending(int i) => _pivKeyExtending[i];
    int IPivotKeyBoxView.GetMainRole(int i) => _pivMainRole[i];
    int IPivotKeyBoxView.Count => Count;
}
