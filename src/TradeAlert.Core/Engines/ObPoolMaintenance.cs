using TradeAlert.Core.Drawing;
using TradeAlert.Core.Models.KeyLevel;

namespace TradeAlert.Core.Engines;

/// <summary>Pine <c>f_delete_OBs_of_pivot</c>, trim helpers.</summary>
public static class ObPoolMaintenance
{
    public static void DeleteObsOfPivot(ObPoolStore pool, int ownerIdx, IDrawingCommandSink? sink)
    {
        foreach (var i in pool.CollectIndicesByOwner(ownerIdx))
        {
            var r = pool.GetRecord(i);
            ObDrawEngine.EmitDelete(r.Box, sink);
            ObLabelDrawEngine.EmitDelete(r.LabelDrawingKey, sink);
            pool.RemoveAt(i);
        }
    }

    /// <summary>
    /// Pine <c>f_cancel_pending_OBs_same_dir</c> (lines 1893–1920).
    /// Before adding a new pending OB (state=0), REMOVE all existing pending OBs
    /// that share the same <paramref name="typ"/> AND belong to a DIFFERENT owner pivot
    /// (owner == <paramref name="newOwnerIdx"/> is kept).
    /// Pine removes from the array entirely; state is not set to 3.
    /// </summary>
    public static void CancelPendingObsSameDir(ObPoolStore pool, int newOwnerIdx, int typ, IDrawingCommandSink? sink)
    {
        var toRemove = new List<int>();
        for (var i = 0; i < pool.Count; i++)
        {
            var r = pool.GetRecord(i);
            if (r.State == 0 && r.Type == typ && r.Owner != newOwnerIdx)
            {
                // Pine deletes the label and removes the record entirely
                ObLabelDrawEngine.EmitDelete(r.LabelDrawingKey, sink);
                toRemove.Add(i);
            }
        }
        // Remove back-to-front to preserve indices
        for (var i = toRemove.Count - 1; i >= 0; i--)
            pool.RemoveAt(toRemove[i]);
    }

    public static void PushOb(
        ObPoolStore pool,
        int barIndex,
        int barOb,
        double xOb,
        int typ,
        int ownerIdx,
        int source,
        int pivotType,
        int pivotHighId,
        int pivotLowId,
        int obScanBars,
        Func<int, (double open, double high, double low, double close)> ohlcAtLag,
        Func<int, DateTime> chartTimeAtLag,
        int lookbackBars,
        bool showObBox,
        bool useObConfirm,
        bool hideObLoseZin,
        IDrawingCommandSink? sink)
    {
        if (pool.Exists(barOb, ownerIdx, source))
            return;

        var (state, count, lastBar) = OBEngine.ComputeInitialObState(barIndex, barOb, typ, xOb, obScanBars, ohlcAtLag);

        // Pine f_cancel_pending_OBs_same_dir: remove old pending OBs of OTHER pivots before adding new pending one.
        if (state == 0)
            CancelPendingObsSameDir(pool, ownerIdx, typ, sink);
        var number = pool.CountByOwner(ownerIdx, source) + 1;

        pool.Push(state, count, xOb, barOb, typ, ownerIdx, source,
            number, pivotType, pivotHighId, pivotLowId);
        var idx = pool.Count - 1;
        pool.SetLastCheckedBar(idx, lastBar);

        var foundLag = barIndex - barOb;
        if (foundLag >= 0 && foundLag <= 180)
        {
            var ft = ohlcAtLag(foundLag);
            var labelPrice = typ == 1 ? ft.high : ft.low;
            var labelKey = ObLabelDrawEngine.ChartObjectName(ownerIdx, source, barOb, number);
            pool.SetLabelDrawingKey(idx, labelKey);
            ObLabelDrawEngine.EmitCreate(barOb, labelPrice, typ, ownerIdx, source, number, sink);
            if (state == 3)
                ObLabelDrawEngine.SetLoseLabel(pool, idx, hideObLoseZin, sink);
        }

        if (state == 2 && showObBox)
        {
            var (spec, fail) = ObDrawEngine.TryBuildFromBar(
                barIndex, barOb, typ, source, ownerIdx, number, lookbackBars, ohlcAtLag, chartTimeAtLag);
            if (spec != null)
            {
                pool.SetBox(idx, spec);
                pool.SetExtending(idx, true);
                ObDrawEngine.EmitCreate(spec, sink);
                pool.SetBoxMissingReason(idx, ObBoxMissingReason.None);
            }
            else if (fail.HasValue)
                pool.SetBoxMissingReason(idx, fail.Value);
        }
        else if (state == 2 && !showObBox)
            pool.SetBoxMissingReason(idx, ObBoxMissingReason.ShowObBoxOff);
        else if (state == 3)
            pool.SetBoxMissingReason(idx, ObBoxMissingReason.HtfLose);

        if (!useObConfirm)
        {
            if (state == 0 || state == 2)
            {
                pool.SetFlagLtf(idx, 2);
                pool.SetLtfConfirmKind(idx, ObLtfConfirmKind.Standard);
            }
        }
    }
}
