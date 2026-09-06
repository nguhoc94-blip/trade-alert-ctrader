using System.Collections.Generic;
using TradeAlert.Core.Drawing;
using TradeAlert.Core.Models.KeyLevel;

namespace TradeAlert.Core.Engines;

/// <summary>Pine <c>f_limit_overlapping_ob_boxes</c> + <c>f_process_overlap_group</c>.</summary>
public static class ObOverlapEngine
{
    public static void LimitOverlappingObBoxes(
        ObPoolStore pool,
        int barIndex,
        Func<int, (double open, double high, double low, double close)> ohlcAtLag,
        IDrawingCommandSink? sink)
    {
        if (pool.Count < 2) return;

        var green = new List<int>();
        var red = new List<int>();

        for (var i = 0; i < pool.Count; i++)
        {
            var r = pool.GetRecord(i);
            if (r.Box == null || r.State == 3) continue;
            if (r.Type == 1) green.Add(i);
            else if (r.Type == -1) red.Add(i);
        }

        ProcessOverlapGroup(pool, barIndex, green, ohlcAtLag, sink);
        ProcessOverlapGroup(pool, barIndex, red, ohlcAtLag, sink);
    }

    static void ProcessOverlapGroup(
        ObPoolStore pool,
        int barIndex,
        List<int> indices,
        Func<int, (double open, double high, double low, double close)> ohlcAtLag,
        IDrawingCommandSink? sink)
    {
        if (indices.Count < 2) return;

        var tops = new double[indices.Count];
        var bots = new double[indices.Count];
        var valid = new bool[indices.Count];

        for (var i = 0; i < indices.Count; i++)
        {
            var barOb = pool.GetRecord(indices[i]).Bar;
            var lag = barIndex - barOb;
            if (lag < 0 || lag > 180)
                continue;
            var (_, h, l, _) = ohlcAtLag(lag);
            tops[i] = h;
            bots[i] = l;
            valid[i] = true;
        }

        var visited = new bool[indices.Count];
        for (var seed = 0; seed < indices.Count; seed++)
        {
            if (visited[seed] || !valid[seed]) continue;

            var group = new List<int> { seed };
            visited[seed] = true;

            var expanded = true;
            while (expanded)
            {
                expanded = false;
                for (var j = 0; j < indices.Count; j++)
                {
                    if (visited[j] || !valid[j]) continue;

                    foreach (var g in group)
                    {
                        if (!valid[g]) continue;
                        if (tops[g] >= bots[j] && bots[g] <= tops[j])
                        {
                            group.Add(j);
                            visited[j] = true;
                            expanded = true;
                            break;
                        }
                    }
                }
            }

            if (group.Count < 2) continue;

            group.Sort((a, b) =>
                pool.GetRecord(indices[b]).Bar.CompareTo(pool.GetRecord(indices[a]).Bar));

            var keeper = pool.GetRecord(indices[group[0]]);
            var rival = new ObOverlapRival(
                keeper.Bar, keeper.Source, keeper.Owner, keeper.Number,
                keeper.PivotType, keeper.PivotHighId, keeper.PivotLowId);

            for (var k = 1; k < group.Count; k++)
            {
                var obIdx = indices[group[k]];
                var r = pool.GetRecord(obIdx);
                if (r.Box == null) continue;
                pool.SetBoxMissingReason(obIdx, ObBoxMissingReason.OverlapTrimmed);
                pool.SetOverlapRival(obIdx, rival);
                ObDrawEngine.EmitDelete(r.Box, sink);
                pool.SetBox(obIdx, null);
                pool.SetExtending(obIdx, false);
            }
        }
    }
}
