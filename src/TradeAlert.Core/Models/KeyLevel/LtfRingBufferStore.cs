using System.Collections.Generic;
using TradeAlert.Core.Engines;

namespace TradeAlert.Core.Models.KeyLevel;

/// <summary>
/// Pine LTF ring + flat arrays — <c>f_push_ltf_snapshot</c>, <c>f_get_ltf_from_buffer</c>.
/// </summary>
public sealed class LtfRingBufferStore
{
    readonly int _capacity;
    readonly int _flatMax;
    readonly int[] _barIndices;
    readonly int[] _startIdx;
    readonly int[] _endIdx;
    readonly List<double> _flatOpen = new();
    readonly List<double> _flatHigh = new();
    readonly List<double> _flatLow = new();
    readonly List<double> _flatClose = new();
    int _head;

    public LtfRingBufferStore(int capacity, int flatMaxMultiplier = 40)
    {
        if (capacity <= 0) throw new System.ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _flatMax = capacity * flatMaxMultiplier;
        _barIndices = new int[capacity];
        _startIdx = new int[capacity];
        _endIdx = new int[capacity];
        ClearMetadata();
    }

    public int Capacity => _capacity;
    public int FlatCount => _flatOpen.Count;
    public bool HasAnyLtfData => _flatOpen.Count > 0;

    public void Clear()
    {
        _flatOpen.Clear();
        _flatHigh.Clear();
        _flatLow.Clear();
        _flatClose.Clear();
        ClearMetadata();
        _head = 0;
    }

    void ClearMetadata()
    {
        for (var i = 0; i < _capacity; i++)
        {
            _barIndices[i] = int.MinValue;
            _startIdx[i] = int.MinValue;
            _endIdx[i] = int.MinValue;
        }
    }

    /// <summary>Push LTF bars for HTF bar <paramref name="htfBarIndex"/> (Pine bar_index-1).</summary>
    public void PushSnapshot(int htfBarIndex, WickNeutralizeEngine.ILtfBarBundle? bundle)
    {
        if (bundle is null || bundle.Count <= 0)
            return;

        var n = bundle.Count;
        if (_flatOpen.Count + n > _flatMax)
        {
            _flatOpen.Clear();
            _flatHigh.Clear();
            _flatLow.Clear();
            _flatClose.Clear();
            ClearMetadata();
            _head = 0;
        }

        var start = _flatOpen.Count;
        for (var i = 0; i < n; i++)
        {
            if (bundle is LtfBarBundle lb)
            {
                var t = lb.OhlcAt(i);
                _flatOpen.Add(t.o);
                _flatHigh.Add(t.h);
                _flatLow.Add(t.l);
                _flatClose.Add(t.c);
            }
            else
            {
                _flatOpen.Add(bundle.OpenAt(i));
                _flatHigh.Add(bundle.HighAt(i));
                _flatLow.Add(bundle.LowAt(i));
                _flatClose.Add(bundle.CloseAt(i));
            }
        }

        var end = start + n - 1;
        _barIndices[_head] = htfBarIndex;
        _startIdx[_head] = start;
        _endIdx[_head] = end;
        _head = (_head + 1) % _capacity;
    }

    public bool TryGetRange(int targetHtfBarIndex, out int startIdx, out int endIdx)
    {
        for (var i = 0; i < _capacity; i++)
        {
            if (_barIndices[i] == targetHtfBarIndex)
            {
                startIdx = _startIdx[i];
                endIdx = _endIdx[i];
                return startIdx != int.MinValue && endIdx != int.MinValue;
            }
        }
        startIdx = endIdx = -1;
        return false;
    }

    public bool HasBar(int targetHtfBarIndex)
    {
        for (var i = 0; i < _capacity; i++)
            if (_barIndices[i] == targetHtfBarIndex)
                return _startIdx[i] != int.MinValue && _endIdx[i] != int.MinValue;
        return false;
    }

    public (double o, double h, double l, double c) FlatOhlcAt(int flatIndex) =>
        (_flatOpen[flatIndex], _flatHigh[flatIndex], _flatLow[flatIndex], _flatClose[flatIndex]);

    /// <summary>Pine <c>oLtf[offset]</c> — LTF bars inside HTF bar <paramref name="targetHtfBarIndex"/>.</summary>
    public WickNeutralizeEngine.ILtfBarBundle? TryGetBundle(int targetHtfBarIndex)
    {
        if (!TryGetRange(targetHtfBarIndex, out var start, out var end))
            return null;
        return new LtfSliceBundle(this, start, end);
    }

    /// <summary>Read-only view of a flat LTF slice in the ring buffer.</summary>
    sealed class LtfSliceBundle : WickNeutralizeEngine.ILtfBarBundle
    {
        readonly LtfRingBufferStore _store;
        readonly int _start;
        readonly int _count;

        public LtfSliceBundle(LtfRingBufferStore store, int start, int end)
        {
            _store = store;
            _start = start;
            _count = end - start + 1;
        }

        public int Count => _count;
        public double OpenAt(int i)  => _store.FlatOhlcAt(_start + i).o;
        public double HighAt(int i)  => _store.FlatOhlcAt(_start + i).h;
        public double LowAt(int i)   => _store.FlatOhlcAt(_start + i).l;
        public double CloseAt(int i) => _store.FlatOhlcAt(_start + i).c;
    }
}
