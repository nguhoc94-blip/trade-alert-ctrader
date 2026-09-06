using TradeAlert.Core.Drawing;
using TradeAlert.Core.Models.KeyLevel;

namespace TradeAlert.Core.Engines;

/// <summary>Pine <c>f_limit_overlapping_keylevels_soft</c> (lines 3615–3737).</summary>
public static class KeyLevelOverlapEngine
{
    /// <summary>
    /// Soft-hide excess overlapping keylevels: delete chart box, clear <c>pivKeyBox</c>,
    /// set <c>pivKeyVisible=false</c>; keep <c>pivHasKey</c> (Pine does not cascade hasKey).
    /// </summary>
    public static void LimitOverlappingKeylevelsSoft(
        PivotStateStore pivots,
        int maxKeep,
        IDrawingCommandSink? sink)
    {
        if (maxKeep < 1 || pivots.Count < 2) return;

        var eligible = new List<(int PivIdx, double Top, double Bot)>();

        for (var i = 0; i < pivots.Count; i++)
        {
            var kb = pivots.GetKeyBox(i);
            if (kb?.Spec == null) continue;

            var flag = pivots.GetFlag(i);
            if (flag != 1 && flag != 4 && flag != 5 && flag != 6) continue;

            eligible.Add((i, kb.Spec.Top, kb.Spec.Bottom));
        }

        if (eligible.Count < 2) return;

        var visited = new bool[eligible.Count];

        for (var seed = 0; seed < eligible.Count; seed++)
        {
            if (visited[seed]) continue;

            var group = new List<int> { seed };
            visited[seed] = true;

            var expanded = true;
            while (expanded)
            {
                expanded = false;
                for (var j = 0; j < eligible.Count; j++)
                {
                    if (visited[j]) continue;

                    var topJ = eligible[j].Top;
                    var botJ = eligible[j].Bot;

                    foreach (var g in group)
                    {
                        var topG = eligible[g].Top;
                        var botG = eligible[g].Bot;
                        // Pine: topG >= botJ and botG <= topJ
                        if (topG >= botJ && botG <= topJ)
                        {
                            group.Add(j);
                            visited[j] = true;
                            expanded = true;
                            break;
                        }
                    }
                }
            }

            if (group.Count <= maxKeep) continue;

            // Sort by pivIndex descending (newer bar first = keep)
            group.Sort((a, b) =>
                pivots.GetBarIndexInternal(eligible[b].PivIdx)
                    .CompareTo(pivots.GetBarIndexInternal(eligible[a].PivIdx)));

            for (var k = maxKeep; k < group.Count; k++)
            {
                var hidePivIdx = eligible[group[k]].PivIdx;
                PivotTransitionEngine.EmitSoftHideKeyBox(pivots, hidePivIdx, sink);
            }
        }
    }
}
