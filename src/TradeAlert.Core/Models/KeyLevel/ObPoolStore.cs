using System;
using System.Collections.Generic;
using System.Linq;

namespace TradeAlert.Core.Models.KeyLevel;

/// <summary>Pool OB mirror Pine parallel arrays — mọi cột cùng <see cref="Count"/>.</summary>
public sealed class ObPoolStore
{
    readonly List<int> _obState = new();
    readonly List<int> _obCount = new();
    readonly List<double> _obX = new();
    readonly List<int> _obBar = new();
    readonly List<int> _obType = new();
    readonly List<int> _obOwner = new();
    readonly List<int> _obSource = new();
    readonly List<int> _obNumber = new();
    readonly List<int> _obPivotType = new();
    readonly List<int> _obPivotHighId = new();
    readonly List<int> _obPivotLowId = new();
    readonly List<int> _obLastCheckedBar = new();
    readonly List<ObBoxSpec?> _obBoxes = new();
    readonly List<bool> _obExtending = new();
    readonly List<int> _obFlagLtf = new();
    readonly List<int> _obLtfHasBars = new();
    readonly List<int> _obLtfTotalBars = new();
    readonly List<double> _obLtfScanHigh = new();
    readonly List<double> _obLtfScanLow = new();
    readonly List<ObLtfConfirmKind> _obLtfConfirmKind = new();
    readonly List<int> _obLtfTouchCount = new();
    readonly List<int> _obLtfFlatCandles = new();
    readonly List<int> _obLtfBufferMissBars = new();
    readonly List<int> _obLtfScanMaxBar = new();
    readonly List<int> _obLtfConfirmBar = new();
    readonly List<ObLtfConfirmPath> _obLtfConfirmPath = new();
    readonly List<int> _obLtfBufferFlatAtConfirm = new();
    readonly List<string?> _obLabelDrawingKeys = new();
    readonly List<ObBoxMissingReason> _obBoxMissingReason = new();
    readonly List<ObOverlapRival> _obOverlapRival = new();

    public int Count => _obState.Count;

    public void PushDefaults()
    {
        _obState.Add(0);
        _obCount.Add(0);
        _obX.Add(double.NaN);
        _obBar.Add(0);
        _obType.Add(0);
        _obOwner.Add(-1);
        _obSource.Add(0);
        _obNumber.Add(0);
        _obPivotType.Add(0);
        _obPivotHighId.Add(0);
        _obPivotLowId.Add(0);
        _obLastCheckedBar.Add(0);
        _obBoxes.Add(null);
        _obExtending.Add(false);
        _obFlagLtf.Add(0);
        _obLtfHasBars.Add(0);
        _obLtfTotalBars.Add(0);
        _obLtfScanHigh.Add(double.NaN);
        _obLtfScanLow.Add(double.NaN);
        _obLtfConfirmKind.Add(ObLtfConfirmKind.Pending);
        _obLtfTouchCount.Add(0);
        _obLtfFlatCandles.Add(0);
        _obLtfBufferMissBars.Add(0);
        _obLtfScanMaxBar.Add(-1);
        _obLtfConfirmBar.Add(-1);
        _obLtfConfirmPath.Add(ObLtfConfirmPath.None);
        _obLtfBufferFlatAtConfirm.Add(0);
        _obLabelDrawingKeys.Add(null);
        _obBoxMissingReason.Add(ObBoxMissingReason.Pending);
        _obOverlapRival.Add(ObOverlapRival.None);
    }

    public void RemoveAt(int index)
    {
        if ((uint)index >= (uint)Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        _obState.RemoveAt(index);
        _obCount.RemoveAt(index);
        _obX.RemoveAt(index);
        _obBar.RemoveAt(index);
        _obType.RemoveAt(index);
        _obOwner.RemoveAt(index);
        _obSource.RemoveAt(index);
        _obNumber.RemoveAt(index);
        _obPivotType.RemoveAt(index);
        _obPivotHighId.RemoveAt(index);
        _obPivotLowId.RemoveAt(index);
        _obLastCheckedBar.RemoveAt(index);
        _obBoxes.RemoveAt(index);
        _obExtending.RemoveAt(index);
        _obFlagLtf.RemoveAt(index);
        _obLtfHasBars.RemoveAt(index);
        _obLtfTotalBars.RemoveAt(index);
        _obLtfScanHigh.RemoveAt(index);
        _obLtfScanLow.RemoveAt(index);
        _obLtfConfirmKind.RemoveAt(index);
        _obLtfTouchCount.RemoveAt(index);
        _obLtfFlatCandles.RemoveAt(index);
        _obLtfBufferMissBars.RemoveAt(index);
        _obLtfScanMaxBar.RemoveAt(index);
        _obLtfConfirmBar.RemoveAt(index);
        _obLtfConfirmPath.RemoveAt(index);
        _obLtfBufferFlatAtConfirm.RemoveAt(index);
        _obLabelDrawingKeys.RemoveAt(index);
        _obBoxMissingReason.RemoveAt(index);
        _obOverlapRival.RemoveAt(index);
    }

    public ObRecord GetRecord(int i) =>
        new()
        {
            State = _obState[i],
            Count = _obCount[i],
            X = _obX[i],
            Bar = _obBar[i],
            Type = _obType[i],
            Owner = _obOwner[i],
            Source = _obSource[i],
            Number = _obNumber[i],
            PivotType = _obPivotType[i],
            PivotHighId = _obPivotHighId[i],
            PivotLowId = _obPivotLowId[i],
            LastCheckedBar = _obLastCheckedBar[i],
            Box = _obBoxes[i],
            Extending = _obExtending[i],
            FlagLtf = _obFlagLtf[i],
            LtfHasBars = _obLtfHasBars[i],
            LtfTotalBars = _obLtfTotalBars[i],
            LtfScanHigh = _obLtfScanHigh[i],
            LtfScanLow = _obLtfScanLow[i],
            LtfConfirmKind = _obLtfConfirmKind[i],
            LtfTouchCount = _obLtfTouchCount[i],
            LtfFlatCandles = _obLtfFlatCandles[i],
            LtfBufferMissBars = _obLtfBufferMissBars[i],
            LtfScanMaxBar = _obLtfScanMaxBar[i],
            LtfConfirmBar = _obLtfConfirmBar[i],
            LtfConfirmPath = _obLtfConfirmPath[i],
            LtfBufferFlatAtConfirm = _obLtfBufferFlatAtConfirm[i],
            LabelDrawingKey = _obLabelDrawingKeys[i],
            BoxMissingReason = _obBoxMissingReason[i],
            OverlapRival = _obOverlapRival[i],
        };

    public void SetBox(int i, ObBoxSpec? box) => _obBoxes[i] = box;

    public bool Exists(int bar, int owner, int source)
    {
        for (var i = 0; i < Count; i++)
            if (_obBar[i] == bar && _obOwner[i] == owner && _obSource[i] == source)
                return true;
        return false;
    }

    public int CountByOwner(int owner, int source)
    {
        var n = 0;
        for (var i = 0; i < Count; i++)
            if (_obOwner[i] == owner && _obSource[i] == source)
                n++;
        return n;
    }

    public int? GetPivotObBar(int owner)
    {
        for (var i = 0; i < Count; i++)
            if (_obOwner[i] == owner && _obSource[i] == 0)
                return _obBar[i];
        return null;
    }

    /// <summary>Remove all OB rows owned by pivot (Pine <c>f_delete_OBs_of_pivot</c>).</summary>
    public List<int> CollectIndicesByOwner(int owner)
    {
        var list = new List<int>();
        for (var i = 0; i < Count; i++)
            if (_obOwner[i] == owner)
                list.Add(i);
        list.Sort((a, b) => b.CompareTo(a));
        return list;
    }
    public void SetLabelDrawingKey(int i, string? key) => _obLabelDrawingKeys[i] = key;

    // Setters for lifecycle mutations
    public void SetState(int i, int state)           => _obState[i]          = state;
    public void SetCount(int i, int count)           => _obCount[i]          = count;
    public void SetExtending(int i, bool extending)  => _obExtending[i]      = extending;
    public void SetFlagLtf(int i, int flagLtf)       => _obFlagLtf[i]        = flagLtf;
    public void SetLastCheckedBar(int i, int bar)    => _obLastCheckedBar[i]  = bar;
    public void SetLtfHasBars(int i, int v)          => _obLtfHasBars[i]     = v;
    public void SetLtfTotalBars(int i, int v)        => _obLtfTotalBars[i]   = v;
    public void SetLtfScanHigh(int i, double v)      => _obLtfScanHigh[i]    = v;
    public void SetLtfScanLow(int i, double v)       => _obLtfScanLow[i]     = v;
    public void SetLtfConfirmKind(int i, ObLtfConfirmKind kind) => _obLtfConfirmKind[i] = kind;
    public void SetLtfTouchCount(int i, int v)            => _obLtfTouchCount[i] = v;
    public void SetLtfFlatCandles(int i, int v)           => _obLtfFlatCandles[i] = v;
    public void SetLtfBufferMissBars(int i, int v)        => _obLtfBufferMissBars[i] = v;
    public void SetLtfScanMaxBar(int i, int v)            => _obLtfScanMaxBar[i] = v;
    public void SetLtfConfirmBar(int i, int v)            => _obLtfConfirmBar[i] = v;
    public void SetLtfConfirmPath(int i, ObLtfConfirmPath path) => _obLtfConfirmPath[i] = path;
    public void SetLtfBufferFlatAtConfirm(int i, int v)   => _obLtfBufferFlatAtConfirm[i] = v;
    public void SetBoxMissingReason(int i, ObBoxMissingReason reason)
    {
        _obBoxMissingReason[i] = reason;
        if (reason != ObBoxMissingReason.OverlapTrimmed)
            _obOverlapRival[i] = ObOverlapRival.None;
    }

    public void SetOverlapRival(int i, ObOverlapRival rival) => _obOverlapRival[i] = rival;

    public void Push(
        int state, int count, double x, int bar, int typ, int owner, int source,
        int number, int pivotType, int pivotHighId, int pivotLowId)
    {
        _obState.Add(state);
        _obCount.Add(count);
        _obX.Add(x);
        _obBar.Add(bar);
        _obType.Add(typ);
        _obOwner.Add(owner);
        _obSource.Add(source);
        _obNumber.Add(number);
        _obPivotType.Add(pivotType);
        _obPivotHighId.Add(pivotHighId);
        _obPivotLowId.Add(pivotLowId);
        _obLastCheckedBar.Add(bar); // lastChecked starts at barOB
        _obBoxes.Add(null);
        _obExtending.Add(false);
        _obFlagLtf.Add(0);
        _obLtfHasBars.Add(0);
        _obLtfTotalBars.Add(0);
        _obLtfScanHigh.Add(double.NaN);
        _obLtfScanLow.Add(double.NaN);
        _obLtfConfirmKind.Add(ObLtfConfirmKind.Pending);
        _obLtfTouchCount.Add(0);
        _obLtfFlatCandles.Add(0);
        _obLtfBufferMissBars.Add(0);
        _obLtfScanMaxBar.Add(-1);
        _obLtfConfirmBar.Add(-1);
        _obLtfConfirmPath.Add(ObLtfConfirmPath.None);
        _obLtfBufferFlatAtConfirm.Add(0);
        _obLabelDrawingKeys.Add(null);
        _obBoxMissingReason.Add(state == 0
            ? ObBoxMissingReason.Pending
            : ObBoxMissingReason.None);
        _obOverlapRival.Add(ObOverlapRival.None);
    }

    /// <summary>Pine trim pivot shift — decrement owner indices after oldest pivot removed at index 0.</summary>
    public void ShiftOwnersAfterPivotRemovedAtZero()
    {
        for (var i = 0; i < Count; i++)
        {
            if (_obOwner[i] > 0)
                _obOwner[i]--;
        }
    }

    public bool InvariantAllColumnsSameCount()
    {
        var c = Count;
        return new[]
        {
            _obCount.Count, _obX.Count, _obBar.Count, _obType.Count, _obOwner.Count,
            _obSource.Count, _obNumber.Count, _obPivotType.Count, _obPivotHighId.Count,
            _obPivotLowId.Count, _obLastCheckedBar.Count, _obBoxes.Count, _obExtending.Count,
            _obFlagLtf.Count, _obLtfHasBars.Count, _obLtfTotalBars.Count,
            _obLtfConfirmKind.Count, _obLtfTouchCount.Count, _obLtfFlatCandles.Count,
            _obLtfBufferMissBars.Count, _obLtfScanMaxBar.Count, _obLtfConfirmBar.Count,
            _obLtfConfirmPath.Count, _obLtfBufferFlatAtConfirm.Count,
            _obLabelDrawingKeys.Count, _obBoxMissingReason.Count, _obOverlapRival.Count
        }.All(x => x == c);
    }
}
