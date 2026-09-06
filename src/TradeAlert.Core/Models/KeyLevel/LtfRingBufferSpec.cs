using System;

namespace TradeAlert.Core.Models.KeyLevel;

/// <summary>Contract LTF ring + flat — không feed runtime security trong Loop 3.</summary>
public sealed class LtfRingBufferSpec
{
    public int Capacity { get; }

    readonly int[] _barIndices;
    readonly int[] _startIdx;
    readonly int[] _endIdx;
    int _head;

    public LtfRingBufferSpec(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
        _barIndices = new int[capacity];
        _startIdx = new int[capacity];
        _endIdx = new int[capacity];
        for (var i = 0; i < capacity; i++)
        {
            _barIndices[i] = int.MinValue;
            _startIdx[i] = int.MinValue;
            _endIdx[i] = int.MinValue;
        }
    }

    public int Head => _head;

    public void AdvanceHead()
    {
        _head = (_head + 1) % Capacity;
    }

    public void SetSlot(int logicalIndex, int barIdx, int start, int end)
    {
        var i = logicalIndex % Capacity;
        if (i < 0) i += Capacity;
        _barIndices[i] = barIdx;
        _startIdx[i] = start;
        _endIdx[i] = end;
    }

    public bool InvariantIndicesInRange()
    {
        for (var i = 0; i < Capacity; i++)
        {
            if (_startIdx[i] > _endIdx[i] && _startIdx[i] != int.MinValue && _endIdx[i] != int.MinValue)
                return false;
        }

        return true;
    }
}
