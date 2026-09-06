using System;
using System.Collections.Generic;

namespace TradeAlert.Core.Models.Alerts;

/// <summary>In-memory per session — không eviction âm thầm; <see cref="ResetSession"/> / <see cref="RemoveOlderThanBarIndex"/> explicit.</summary>
public sealed class AlertDuplicateMemoryStore : IAlertDuplicateStore
{
    readonly HashSet<string> _canonical = new(StringComparer.Ordinal);
    readonly List<(long BarIndex, string Canonical)> _ordered = new();

    public bool TryRegister(in DuplicateKey key, out DuplicateRejectReason reason)
    {
        var c = key.ToCanonicalString();
        if (_canonical.Add(c))
        {
            _ordered.Add((key.SourceBarIndex, c));
            reason = DuplicateRejectReason.None;
            return true;
        }

        reason = DuplicateRejectReason.AlreadyRegisteredInSession;
        return false;
    }

    public void ResetSession()
    {
        _canonical.Clear();
        _ordered.Clear();
    }

    public void RemoveOlderThanBarIndex(long minBarIndexExclusive)
    {
        for (var i = _ordered.Count - 1; i >= 0; i--)
        {
            if (_ordered[i].BarIndex < minBarIndexExclusive)
            {
                _canonical.Remove(_ordered[i].Canonical);
                _ordered.RemoveAt(i);
            }
        }
    }
}
