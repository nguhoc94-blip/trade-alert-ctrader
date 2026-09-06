using TradeAlert.Core.Drawing;
using TradeAlert.Core.Models.KeyLevel;

namespace TradeAlert.Core.Engines;

/// <summary>Pine <c>f_sync_keybox_with_flag</c> (lines 1575–1692).</summary>
public static class KeyLevelSyncEngine
{
    public static void SyncKeyboxWithFlag(
        int i,
        PivotStateStore pivots,
        KeyLevelTickContext ctx,
        IDrawingCommandSink? sink,
        ObPoolStore? obPool = null)
    {
        if (!ctx.EnableKeylevel || i < 0 || i >= pivots.Count) return;

        var flag     = pivots.GetFlag(i);
        var hasKey   = pivots.GetHasKey(i);
        var visible  = pivots.GetKeyVisible(i);
        var mainRole = pivots.GetMainRole(i);
        var kb       = pivots.GetKeyBox(i);

        var allowKey = ComputeAllowKey(pivots, i, flag, hasKey);
        var allowCreate = allowKey
                       && (visible || flag == 2 || (flag == 0 && mainRole == 1));

        if (!allowKey || flag == -1 || flag == -2)
        {
            if (kb != null)
                KeyLevelMaintenance.DeleteKeylevelInternal(i, pivots, sink, obPool);
            return;
        }

        if (flag == 8)
        {
            KeyLevelMaintenance.DeleteKeylevel(i, pivots, sink, obPool);
            return;
        }

        if (!allowCreate)
            return;

        if (kb == null)
        {
            RecreateKeyBox(pivots, i, ctx, sink);
            kb = pivots.GetKeyBox(i);
        }

        if (kb != null)
            ApplyStyleForFlag(pivots, i, ctx, sink);
    }

    /// <summary>
    /// Pine: khi 3/7/50 không gọi f_sync — box giữ style lúc latch.
    /// C# sync/bar: dùng <see cref="PivotStateStore.GetOriginalFlag"/> (fallback flagBeforePending).
    /// </summary>
    public static int ResolveKeyStyleFlag(PivotStateStore pivots, int i)
    {
        var flag = pivots.GetFlag(i);
        if (flag is not (3 or 7 or 50))
            return flag;

        var orig = pivots.GetOriginalFlag(i);
        if (orig is 0 or 1 or 2 or 4 or 5)
            return orig;

        var before = pivots.GetFlagBeforePending(i);
        return before >= 0 ? before : orig;
    }

    static bool ComputeAllowKey(PivotStateStore pivots, int i, int flag, bool hasKey)
    {
        var keyState = pivots.GetKeyState(i);
        if (keyState == 1) return true;
        if (keyState == -1) return false;

        bool allowKey;
        if (flag == 1 || flag == 0 || flag == 2 || flag == 4)
            allowKey = hasKey;
        // BREAK_PENDING (3/7): Pine không gọi f_sync khi latch — box giữ trên chart; C# sync/bar phải giữ.
        else if ((flag == 3 || flag == 7) && hasKey)
            allowKey = true;
        else if ((flag == 5 || flag == 6 || flag == 50 || flag == -3) && hasKey)
            allowKey = true;
        else
            allowKey = false;

        if (allowKey && (flag == 5 || flag == 6 || flag == 4 || flag == 50 || flag == -3))
        {
            var groupId = pivots.GetKeyGroupId(i);
            if (groupId != PivotStateStore.FirstDNa && groupId >= 0 && groupId < pivots.Count)
            {
                if (!pivots.GetHasKey(groupId))
                {
                    pivots.SetHasKey(i, false);
                    return false;
                }
            }
        }

        return allowKey;
    }

    static void RecreateKeyBox(PivotStateStore pivots, int i, KeyLevelTickContext ctx, IDrawingCommandSink? sink)
    {
        var typ   = pivots.GetTypeAt(i);
        var pBar  = pivots.GetBarIndexInternal(i);
        var price = pivots.GetPriceInternal(i);
        var pk    = typ == 1 ? "H" : "L";

        var atrProxy = !double.IsNaN(ctx.AtrValue) && ctx.AtrValue > 0 ? ctx.AtrValue : ctx.TickSize * 100;

        var refPick = KeyLevelEngine.FindReferenceCandleFromPivot(
            ctx.BarIndex, pBar, pk,
            ctx.KeylevelLookback, ctx.AvgBody, ctx.KeylevelMinBodyMult,
            useAtrRule: ctx.KeylevelUseAtrRule, ctx.KeylevelAtrLen, ctx.KeylevelAtrMult,
            ctx.OhlcAtLag, atrValueWhenRuleEnabled: ctx.AtrValue);

        var kbSpec = KeyLevelEngine.BuildKeylevelBox(
            ctx.BarIndex, pBar, pk, price,
            refPick.BestLag, refPick.BodyAbs,
            ctx.SearchRangeBars, ctx.KeylevelAtrLen, atrProxy,
            KeyLevelVisual.ActiveColors(1, ctx.KeyOpacityActivePercent).FillArgb,
            KeyLevelVisual.ActiveColors(-1, ctx.KeyOpacityActivePercent).FillArgb,
            opacity: 100 - ctx.KeyOpacityActivePercent, minTick: ctx.TickSize,
            ctx.OhlcAtLag, ctx.ChartTimeAtLag, sink);

        if (kbSpec == null) return;

        pivots.SetKeyBox(i, new KeyBoxRef { Spec = kbSpec });
        pivots.SetKeyVisible(i, true);
        pivots.SetKeyExtending(i, true);
    }

    static void ApplyStyleForFlag(PivotStateStore pivots, int i, KeyLevelTickContext ctx, IDrawingCommandSink? sink)
    {
        var styleFlag = ResolveKeyStyleFlag(pivots, i);
        var brokenTransp = ctx.KeyOpacityBrokenPercent;
        switch (styleFlag)
        {
            case 0:
                // MAIN / MAIN C — vàng (Pine isMainC khi flag==0; pending từ orig 0 giữ vàng)
                PivotTransitionEngine.EmitUpdateKeyBoxMainC(pivots, i, sink, ctx.KeyOpacityActivePercent);
                break;
            case 2:
            case 6:
                PivotTransitionEngine.EmitUpdateKeyBoxBroken(pivots, i, sink, brokenTransp);
                break;
            case 1:
            case 4:
            case 5:
            case -3:
                PivotTransitionEngine.EmitUpdateKeyBoxActive(pivots, i, sink, ctx.KeyOpacityActivePercent);
                break;
        }
    }
}
