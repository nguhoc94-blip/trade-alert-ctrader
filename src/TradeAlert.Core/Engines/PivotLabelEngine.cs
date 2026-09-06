using TradeAlert.Core.Drawing;
using TradeAlert.Core.Models.KeyLevel;

namespace TradeAlert.Core.Engines;

/// <summary>Pine <c>update_label</c> (lines 2987–3224) + visibility toggles.</summary>
public static class PivotLabelEngine
{
    public static bool IsPivotVisible(PivotStateStore pivots, int i, int barIndex, int lookbackBars)
    {
        if (i < 0 || i >= pivots.Count) return false;
        var sBar = pivots.GetBarIndexInternal(i);
        return barIndex - sBar <= lookbackBars;
    }

    public static int GetFlagForRendering(PivotStateStore pivots, int i)
    {
        var flag = pivots.GetFlag(i);
        if (flag != -2) return flag;

        var saved = pivots.GetFlagBeforeLock(i);
        return saved != PivotStateStore.FirstDNa ? saved : 1;
    }

    public static void EmitLabelUpdate(
        PivotStateStore pivots,
        int i,
        IDrawingCommandSink? sink,
        PivotLabelRenderOptions? opts = null)
    {
        if (sink == null || opts == null) return;
        if (!IsPivotVisible(pivots, i, opts.BarIndex, opts.LookbackBars)) return;

        var flag = pivots.GetFlag(i);
        if (flag == -2 && opts.DeleteObsOnLockLabelRefresh && opts.ObPool != null)
            ObPoolMaintenance.DeleteObsOfPivot(opts.ObPool, i, sink);

        var flagRender = GetFlagForRendering(pivots, i);
        var isLocked = flag == -2;

        if (isLocked && !opts.ShowLockedSwings)
        {
            EmitHideLabel(pivots, i, sink);
            return;
        }

        if (flagRender == 1 && !opts.ShowActiveSwings)
        {
            EmitHideLabel(pivots, i, sink);
            return;
        }

        if (flagRender is 5 or 6 or 50 or -3 && !opts.ShowFakeSwings)
        {
            EmitHideLabel(pivots, i, sink);
            return;
        }

        if (!TryBuildLabel(pivots, i, flagRender, isLocked, out var text, out var color))
            return;

        var typ = pivots.GetTypeAt(i);
        var hid = pivots.GetHighId(i);
        var lid = pivots.GetLowId(i);
        var key = typ == 1 ? $"sw_H{hid}" : $"sw_L{lid}";

        sink.Enqueue(new DrawingCommand
        {
            Kind        = DrawingCommandKind.AddLabel,
            LabelKeyStr = key,
            BarIndex    = pivots.GetBarIndexInternal(i),
            Price       = pivots.GetPivotPrice(i),
            IsHigh      = typ == 1,
            Text        = text,
            ColorArgb   = color,
        });
    }

    public static void RefreshAll(PivotStateStore pivots, IDrawingCommandSink? sink, PivotLabelRenderOptions opts)
    {
        if (sink == null) return;
        for (var i = 0; i < pivots.Count; i++)
            EmitLabelUpdate(pivots, i, sink, opts);
    }

    static void EmitHideLabel(PivotStateStore pivots, int i, IDrawingCommandSink? sink)
    {
        var typ = pivots.GetTypeAt(i);
        var key = typ == 1 ? $"sw_H{pivots.GetHighId(i)}" : $"sw_L{pivots.GetLowId(i)}";
        sink!.Enqueue(new DrawingCommand
        {
            Kind        = DrawingCommandKind.AddLabel,
            LabelKeyStr = key,
            Text        = "",
            ColorArgb   = 0x00FFFFFFu,
        });
    }

    static bool TryBuildLabel(
        PivotStateStore pivots, int i, int flagRender, bool isLocked,
        out string text, out uint color)
    {
        text = "";
        color = 0;

        var typ = pivots.GetTypeAt(i);
        var hid = pivots.GetHighId(i);
        var lid = pivots.GetLowId(i);
        var sid = typ == 1 ? $"H{hid:D3}" : $"L{lid:D3}";
        var subTyp = pivots.GetSubType(i);
        var lockSuffix = isLocked ? " (lock)" : "";

        uint activeColor = subTyp == 1 ? Loop6StylePalette.HhLabel()
            : subTyp == -1 ? Loop6StylePalette.LlLabel()
            : typ == 1 ? Loop6StylePalette.HighLabel()
            : Loop6StylePalette.LowLabel();

        switch (flagRender)
        {
            case 1:
                text = $"ACTIVE {sid}{RootSuffix(pivots, i)}{lockSuffix}";
                color = activeColor;
                return true;
            case 4:
                text = $"D {sid}{lockSuffix}";
                color = Loop6StylePalette.DOrangeLabel();
                return true;
            case 2:
                text = $"BROKEN {sid}{RootSuffix(pivots, i)}{DStatusSuffix(pivots, i)}{lockSuffix}";
                color = Loop6StylePalette.BrokenLabel();
                return true;
            case 8:
                text = $"MAIN_OLD {sid}{lockSuffix}";
                color = PineColors.MainOldGray;
                return true;
            case 0:
                var role = pivots.GetMainRole(i);
                var mainId = typ == -1 ? $"L{lid:D3}" : $"H{hid:D3}";
                text = role == 1 ? $"MAIN C {mainId}{lockSuffix}" : $"MAIN{lockSuffix}";
                color = role == 1 ? Loop6StylePalette.MainLabel() : Loop6StylePalette.BrokenLabel();
                return true;
            case -1:
                var selfId = sid;
                var brokenId = pivots.GetMainHighId(i) > 0 ? $"H{pivots.GetMainHighId(i):D3}"
                    : pivots.GetMainLowId(i) > 0 ? $"L{pivots.GetMainLowId(i):D3}" : "";
                text = !string.IsNullOrEmpty(brokenId)
                    ? $"MAIN BROKEN {lockSuffix}"
                    : $"MAIN BROKEN {selfId}{lockSuffix}";
                color = PineColors.MainBrokenRed;
                return true;
            case 5:
                text = $"MAIN FAKE {sid}{RootSuffix(pivots, i)}{lockSuffix}";
                color = Loop6StylePalette.FakeLabel();
                return true;
            case 50:
                text = $"MAIN FAKE PENDING {sid}{RootSuffix(pivots, i)}{lockSuffix}";
                color = Loop6StylePalette.MainLabel();
                return true;
            case -3:
                text = $"MAIN FAKE BROKEN {sid}{RootSuffix(pivots, i)}{lockSuffix}";
                color = Loop6StylePalette.DOrangeLabel();
                return true;
            case 6:
                text = $"BROKEN FAKE {sid}{RootSuffix(pivots, i)}{lockSuffix}";
                color = Loop6StylePalette.BrokenLabel();
                return true;
            case 3:
                var orig = pivots.GetOriginalFlag(i);
                if (orig == 0)
                {
                    var mid = typ == -1 ? $"L{lid:D3}" : $"H{hid:D3}";
                    text = $"MAIN {mid} break?{lockSuffix}";
                    color = Loop6StylePalette.MainLabel();
                }
                else
                {
                    text = $"ACTIVE {sid} break?{lockSuffix}";
                    color = activeColor;
                }
                return true;
            case 7:
                text = $"D (pending) {sid}{lockSuffix}";
                color = Loop6StylePalette.DOrangeLabel();
                return true;
            default:
                return false;
        }
    }

    static string RootSuffix(PivotStateStore pivots, int i)
    {
        var rh = pivots.GetRootHighId(i);
        var rl = pivots.GetRootLowId(i);
        if (rh > 0) return $" | ROOT H{rh:D3}";
        if (rl > 0) return $" | ROOT L{rl:D3}";
        return "";
    }

    static string DStatusSuffix(PivotStateStore pivots, int i)
    {
        var d = pivots.GetFirstDIdx(i);
        if (d == PivotStateStore.FirstDDone) return " [D done]";
        if (d == PivotStateStore.FirstDWaiting) return " [waiting D]";
        if (d >= 0) return " [D commit]";
        return "";
    }
}
