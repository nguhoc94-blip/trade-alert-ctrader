using TradeAlert.Core.Drawing;
using TradeAlert.Core.Models.KeyLevel;

namespace TradeAlert.Core.Engines;

/// <summary>
/// Pine <c>f_delete_keylevel_internal</c> / <c>f_delete_keylevel</c> / lock box delete (2947–2966).
/// </summary>
public static class KeyLevelMaintenance
{
    /// <summary>Pine <c>f_delete_keylevel_internal</c> — xóa box + OB; không đổi <c>pivHasKey</c>.</summary>
    public static void DeleteKeylevelInternal(
        int idx,
        PivotStateStore pivots,
        IDrawingCommandSink? sink,
        ObPoolStore? obPool,
        bool deleteObs = true)
    {
        if (idx < 0 || idx >= pivots.Count) return;

        var kb = pivots.GetKeyBox(idx);
        if (kb?.Spec != null && sink != null)
        {
            sink.Enqueue(new DrawingCommand
            {
                Kind        = DrawingCommandKind.DelKeyBox,
                LabelKeyStr = KeyLevelVisual.ChartObjectName(kb.Spec),
            });
        }

        pivots.SetKeyBox(idx, null);
        pivots.SetKeyExtending(idx, false);

        if (deleteObs && obPool != null)
            ObPoolMaintenance.DeleteObsOfPivot(obPool, idx, sink);
    }

    /// <summary>Pine <c>f_delete_keylevel</c> — internal + <c>hasKey=false</c> + cascade theo <c>keyGroupId</c>.</summary>
    public static void DeleteKeylevel(
        int idx,
        PivotStateStore pivots,
        IDrawingCommandSink? sink,
        ObPoolStore? obPool = null,
        bool deleteObs = true)
    {
        if (idx < 0 || idx >= pivots.Count) return;

        DeleteKeylevelInternal(idx, pivots, sink, obPool, deleteObs);
        pivots.SetHasKey(idx, false);

        for (var i = 0; i < pivots.Count; i++)
        {
            if (i == idx) continue;
            var groupId = pivots.GetKeyGroupId(i);
            if (groupId != idx) continue;
            DeleteKeylevelInternal(i, pivots, sink, obPool, deleteObs);
            pivots.SetHasKey(i, false);
        }
    }

    /// <summary>Pine lock (2963–2969): chỉ xóa box + OB; giữ <c>hasKey</c>.</summary>
    public static void DeleteKeyBoxOnLock(
        int idx,
        PivotStateStore pivots,
        IDrawingCommandSink? sink,
        ObPoolStore? obPool)
    {
        DeleteKeylevelInternal(idx, pivots, sink, obPool, deleteObs: true);
    }

    /// <summary>Pine <c>f_delete_rootB_key_of_main</c> (1507–1525) — BROKEN + cùng structId.</summary>
    public static void DeleteRootBKeyOfMain(
        int mainIdx,
        PivotStateStore pivots,
        IDrawingCommandSink? sink,
        ObPoolStore? obPool = null)
    {
        if (mainIdx <= 0 || mainIdx >= pivots.Count) return;

        var bIdx = mainIdx - 1;
        if (pivots.GetFlag(bIdx) != 2) return;
        if (pivots.GetStructId(bIdx) != pivots.GetStructId(mainIdx)) return;

        DeleteKeylevel(bIdx, pivots, sink, obPool);
    }
}
