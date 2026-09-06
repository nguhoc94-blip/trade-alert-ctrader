using System;
using System.Collections.Generic;
using TradeAlert.Core.Engines;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>
/// Rule 3 OB override — entry is the KLV B edge or an overlapping ZIN OB edge nearest swing C.
/// Near-B touch geometry follows the <em>winning</em> candidate's own box (KeyLevel span or OB box).
/// </summary>
public sealed class EntryEdgePick
{
    public double Entry { get; init; }
    public bool FromOb { get; init; }
    /// <summary>Low edge of the selected candidate zone (near-B touch span).</summary>
    public double NearBZoneLow { get; init; }
    /// <summary>High edge of the selected candidate zone (near-B touch span).</summary>
    public double NearBZoneHigh { get; init; }
}

public static class KeylevelObReader
{
    sealed class EntryCandidate
    {
        public double Edge { get; init; }
        public double ZoneLow { get; init; }
        public double ZoneHigh { get; init; }
        public bool FromOb { get; init; }
    }

    /// <summary>
    /// Resolves limit entry from KLV B and optional OB edges. When <paramref name="swingCReference"/>
    /// is set, picks the candidate nearest to C; otherwise uses the furthest OB extension across
    /// chart TF and M5 (when <paramref name="crossTf"/> is supplied).
    /// </summary>
    public static EntryEdgePick ResolveEntry(
        PineStateEngine chartState,
        in SwingBResult swingB,
        bool isBuy,
        double? swingCReference,
        PineStateEngine? obFallbackState,
        SwingCEdgeCrossTfContext? crossTf)
    {
        var keyTop = swingB.KeyTop;
        var keyBottom = swingB.KeyBottom;
        var keyCandidate = BuildKeyCandidate(isBuy, keyTop, keyBottom);

        var candidates = new List<EntryCandidate> { keyCandidate };

        CollectOverlapObEdges(chartState, isBuy, keyTop, keyBottom, candidates);
        if (crossTf is not null)
            CollectOverlapObEdges(crossTf.M5State, isBuy, keyTop, keyBottom, candidates);
        else if (obFallbackState is not null && !ReferenceEquals(obFallbackState, chartState))
            CollectOverlapObEdges(obFallbackState, isBuy, keyTop, keyBottom, candidates);

        var winner = swingCReference is null
            ? PickFurthestObExtension(isBuy, keyTop, keyBottom, candidates) ?? keyCandidate
            : PickEntryNearestToC(isBuy, swingCReference.Value, candidates) ?? keyCandidate;

        return ToPick(winner);
    }

    /// <summary>Returns the OB-derived entry override, or null if no qualifying OB (legacy).</summary>
    public static double? FindEntryOverride(
        PineStateEngine state,
        bool isBuy,
        double keyTop,
        double keyBottom)
    {
        var candidates = new List<EntryCandidate>();
        CollectOverlapObEdges(state, isBuy, keyTop, keyBottom, candidates);
        var winner = PickFurthestObExtension(isBuy, keyTop, keyBottom, candidates);
        return winner?.Edge;
    }

    static EntryCandidate BuildKeyCandidate(bool isBuy, double keyTop, double keyBottom) => new()
    {
        Edge = isBuy ? keyTop : keyBottom,
        ZoneLow = keyBottom,
        ZoneHigh = keyTop,
        FromOb = false,
    };

    static EntryEdgePick ToPick(EntryCandidate c) => new()
    {
        Entry = c.Edge,
        FromOb = c.FromOb,
        NearBZoneLow = c.ZoneLow,
        NearBZoneHigh = c.ZoneHigh,
    };

    static EntryCandidate? PickFurthestObExtension(
        bool isBuy,
        double keyTop,
        double keyBottom,
        IReadOnlyList<EntryCandidate> candidates)
    {
        EntryCandidate? best = null;
        foreach (var c in candidates)
        {
            if (!c.FromOb)
                continue;

            if (isBuy)
            {
                if (c.Edge <= keyTop)
                    continue;
                if (best is null || c.Edge > best.Edge)
                    best = c;
            }
            else
            {
                if (c.Edge >= keyBottom)
                    continue;
                if (best is null || c.Edge < best.Edge)
                    best = c;
            }
        }

        return best;
    }

    static void CollectOverlapObEdges(
        PineStateEngine state,
        bool isBuy,
        double keyTop,
        double keyBottom,
        List<EntryCandidate> candidates)
    {
        var klo = Math.Min(keyTop, keyBottom);
        var khi = Math.Max(keyTop, keyBottom);
        var wantType = isBuy ? -1 : 1;

        var obPool = state.ObPool;
        for (var i = 0; i < obPool.Count; i++)
        {
            var r = obPool.GetRecord(i);
            if (r.State != 2 || !r.Extending || r.Box is null || r.Type != wantType)
                continue;

            var obTop = Math.Max(r.Box.Top, r.Box.Bottom);
            var obBot = Math.Min(r.Box.Top, r.Box.Bottom);
            if (obBot > khi || obTop < klo)
                continue;

            if (isBuy)
            {
                if (obTop > keyTop)
                {
                    candidates.Add(new EntryCandidate
                    {
                        Edge = obTop,
                        ZoneLow = obBot,
                        ZoneHigh = obTop,
                        FromOb = true,
                    });
                }
            }
            else if (obBot < keyBottom)
            {
                candidates.Add(new EntryCandidate
                {
                    Edge = obBot,
                    ZoneLow = obBot,
                    ZoneHigh = obTop,
                    FromOb = true,
                });
            }
        }
    }

    static EntryCandidate? PickEntryNearestToC(
        bool isBuy,
        double cReference,
        IReadOnlyList<EntryCandidate> candidates)
    {
        EntryCandidate? best = null;
        var bestDist = double.MaxValue;

        foreach (var c in candidates)
        {
            double dist;
            if (isBuy)
            {
                if (c.Edge >= cReference)
                    continue;
                dist = cReference - c.Edge;
            }
            else
            {
                if (c.Edge <= cReference)
                    continue;
                dist = c.Edge - cReference;
            }

            if (dist < bestDist)
            {
                bestDist = dist;
                best = c;
            }
        }

        return best;
    }
}
