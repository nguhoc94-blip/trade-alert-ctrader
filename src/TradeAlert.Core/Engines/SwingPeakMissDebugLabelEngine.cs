using TradeAlert.Core.Drawing;
using TradeAlert.Core.Models.KeyLevel;

namespace TradeAlert.Core.Engines;

/// <summary>
/// Chart labels on the last <see cref="SwingPeakMissTracker.WindowBars"/> bars explaining
/// why each bar was not chosen as swing HIGH (đỉnh). Objects use prefix <c>dbg_peakH_</c>.
/// </summary>
public static class SwingPeakMissDebugLabelEngine
{
    public const uint MissColorArgb   = 0xFFFF7043u; // deep orange
    public const uint PendingColorArgb = 0xFF90A4AEu; // blue-gray
    public const uint FifoMemColorArgb = 0xFFAB47BCu; // purple — in 8-entry overflow queue

    public static string ChartObjectName(int bar) => $"dbg_peakH_{bar}";

    public static void EmitWindow(
        SwingPeakMissTracker tracker,
        PivotStateStore pivots,
        int evalBarIndex,
        int lookbackBars,
        IDrawingCommandSink? sink)
    {
        if (sink == null) return;

        var pickedBars = CollectRecentHighPivotBars(pivots, evalBarIndex, lookbackBars);

        foreach (var e in tracker.WindowEntries(evalBarIndex))
        {
            if (pickedBars.Contains(e.Bar))
            {
                sink.Enqueue(new DrawingCommand
                {
                    Kind        = DrawingCommandKind.DelLabel,
                    LabelKeyStr = ChartObjectName(e.Bar),
                });
                continue;
            }

            if (e.Code == "PICKED")
                continue;

            var (text, color) = FormatEntry(e);
            sink.Enqueue(new DrawingCommand
            {
                Kind        = DrawingCommandKind.AddLabel,
                LabelKeyStr = ChartObjectName(e.Bar),
                BarIndex    = e.Bar,
                Price       = e.Price,
                IsHigh      = true,
                Text        = text,
                ColorArgb   = color,
            });
        }
    }

    static HashSet<int> CollectRecentHighPivotBars(PivotStateStore pivots, int evalBarIndex, int lookbackBars)
    {
        var set = new HashSet<int>();
        var minBar = evalBarIndex - SwingPeakMissTracker.WindowBars;
        for (var i = 0; i < pivots.Count; i++)
        {
            if (pivots.GetTypeAt(i) != 1) continue;
            var bar = pivots.GetBarIndexInternal(i);
            if (bar < minBar) continue;
            if (!PivotLabelEngine.IsPivotVisible(pivots, i, evalBarIndex, lookbackBars)) continue;
            set.Add(bar);
        }
        return set;
    }

    static (string Text, uint Color) FormatEntry(SwingPeakMissTracker.Entry e)
    {
        var head = e.Code switch
        {
            "NO_CAND"   => "no cand",
            "WAIT_B"    => "wait B",
            "WAIT_CD"   => "wait CD",
            "WAIT_RULE" => "wait rule",
            "EXT_LOSS"  => "⊗ EXT",
            "UNCONF"    => "— unconf",
            "OPP_CLR"   => "~ opp",
            "EXP_NO_B"  => "× no B",
            "EXP_NO_CD" => "× no CD",
            "EXP_OVF"   => "× queue",
            "FIFO_MEM"  => "◆ fifo",
            "FIFO_DROP" => "× fifo",
            "MICRO_CLR" => "× μ queue",
            "ALT_BLK"   => "alt blk",
            _           => e.Code,
        };

        var line2 = string.IsNullOrEmpty(e.Detail) ? $"#{e.Bar}" : e.Detail;
        var text  = $"{head}\n{line2}";
        var color = e.Code switch
        {
            "WAIT_B" or "WAIT_CD" or "WAIT_RULE" => PendingColorArgb,
            "FIFO_MEM" or "FIFO_DROP"            => FifoMemColorArgb,
            _                                    => MissColorArgb,
        };
        return (text, color);
    }
}
