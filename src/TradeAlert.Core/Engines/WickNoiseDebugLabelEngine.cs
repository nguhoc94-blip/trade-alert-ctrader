using System.Text;
using TradeAlert.Core.Drawing;

namespace TradeAlert.Core.Engines;

/// <summary>
/// Chart labels at HTF bars with wick-noise candidates showing LTF H/L used for confirm.
/// Prefix <c>dbg_wick_u_</c> / <c>dbg_wick_l_</c>.
/// </summary>
public static class WickNoiseDebugLabelEngine
{
    public const int WindowBars = 15;
    public const int MaxLtfLinesInLabel = 6;

    public const uint ConfirmedColorArgb   = 0xFF00BCD4u; // cyan — wick neutralized
    public const uint PendingColorArgb     = 0xFFFFAB40u; // amber — valid wick, not confirmed
    public const uint NoLtfColorArgb       = 0xFFE040FBu; // magenta — valid wick, no LTF data
    public const uint SkippedColorArgb     = 0xFF78909Cu; // gray — wick too small / invalid

    public static string UpperChartObjectName(int bar) => $"dbg_wick_u_{bar}";
    public static string LowerChartObjectName(int bar) => $"dbg_wick_l_{bar}";

    public static void EmitWindow(
        int evalBarIndex,
        int barA,
        double htfHigh,
        double htfLow,
        WickNeutralizeEngine.WickNeutralizeDiag diag,
        int ltfCount,
        double[] ltfHigh,
        double[] ltfLow,
        double[] ltfOpen,
        double[] ltfClose,
        IDrawingCommandSink? sink)
    {
        if (sink == null || !diag.UseWickFilter || barA < 0)
            return;

        PruneOutsideWindow(evalBarIndex, sink);

        if (diag.ValidUpperWick)
        {
            var (text, color) = FormatUpper(diag, ltfCount, ltfHigh, ltfLow, ltfOpen, ltfClose);
            sink.Enqueue(new DrawingCommand
            {
                Kind        = DrawingCommandKind.AddLabel,
                LabelKeyStr = UpperChartObjectName(barA),
                BarIndex    = barA,
                Price       = htfHigh,
                IsHigh      = true,
                Text        = text,
                ColorArgb   = color,
            });
        }
        else
        {
            sink.Enqueue(new DrawingCommand
            {
                Kind        = DrawingCommandKind.DelLabel,
                LabelKeyStr = UpperChartObjectName(barA),
            });
        }

        if (diag.ValidLowerWick)
        {
            var (text, color) = FormatLower(diag, ltfCount, ltfHigh, ltfLow, ltfOpen, ltfClose);
            sink.Enqueue(new DrawingCommand
            {
                Kind        = DrawingCommandKind.AddLabel,
                LabelKeyStr = LowerChartObjectName(barA),
                BarIndex    = barA,
                Price       = htfLow,
                IsHigh      = false,
                Text        = text,
                ColorArgb   = color,
            });
        }
        else
        {
            sink.Enqueue(new DrawingCommand
            {
                Kind        = DrawingCommandKind.DelLabel,
                LabelKeyStr = LowerChartObjectName(barA),
            });
        }
    }

    static void PruneOutsideWindow(int evalBarIndex, IDrawingCommandSink sink)
    {
        var minBar = evalBarIndex - WindowBars;
        for (var b = minBar - 5; b < minBar; b++)
        {
            if (b < 0) continue;
            sink.Enqueue(new DrawingCommand { Kind = DrawingCommandKind.DelLabel, LabelKeyStr = UpperChartObjectName(b) });
            sink.Enqueue(new DrawingCommand { Kind = DrawingCommandKind.DelLabel, LabelKeyStr = LowerChartObjectName(b) });
        }
    }

    static (string Text, uint Color) FormatUpper(
        WickNeutralizeEngine.WickNeutralizeDiag d,
        int ltfCount,
        double[] ltfHigh,
        double[] ltfLow,
        double[] ltfOpen,
        double[] ltfClose)
    {
        var head = d.UpperConfirmed ? "↑W NEUT" : (d.UseLtfMode ? "↑W cand" : "↑W noLTF");
        var color = d.UpperConfirmed ? ConfirmedColorArgb
            : d.UseLtfMode ? PendingColorArgb : NoLtfColorArgb;

        var sb = new StringBuilder();
        sb.AppendLine(head);
        sb.AppendLine($"wick={Fp(d.UpperWick)} pen={FpRatio(d.UpperPenetrationRatio)}");
        sb.AppendLine($"ltfMaxB={Fp(d.LtfMaxBodyHigh)} ltfMaxH={Fp(d.LtfMaxHigh)}");
        sb.AppendLine($"cleanH={Fp(d.CleanHigh)} n={d.LtfBarCount}");
        AppendLtfLines(sb, ltfCount, ltfHigh, ltfLow, ltfOpen, ltfClose);
        return (sb.ToString().TrimEnd(), color);
    }

    static (string Text, uint Color) FormatLower(
        WickNeutralizeEngine.WickNeutralizeDiag d,
        int ltfCount,
        double[] ltfHigh,
        double[] ltfLow,
        double[] ltfOpen,
        double[] ltfClose)
    {
        var head = d.LowerConfirmed ? "↓W NEUT" : (d.UseLtfMode ? "↓W cand" : "↓W noLTF");
        var color = d.LowerConfirmed ? ConfirmedColorArgb
            : d.UseLtfMode ? PendingColorArgb : NoLtfColorArgb;

        var sb = new StringBuilder();
        sb.AppendLine(head);
        sb.AppendLine($"wick={Fp(d.LowerWick)} pen={FpRatio(d.LowerPenetrationRatio)}");
        sb.AppendLine($"ltfMinB={Fp(d.LtfMinBodyLow)} ltfMinL={Fp(d.LtfMinLow)}");
        sb.AppendLine($"cleanL={Fp(d.CleanLow)} n={d.LtfBarCount}");
        AppendLtfLines(sb, ltfCount, ltfHigh, ltfLow, ltfOpen, ltfClose);
        return (sb.ToString().TrimEnd(), color);
    }

    static void AppendLtfLines(
        StringBuilder sb,
        int ltfCount,
        double[] ltfHigh,
        double[] ltfLow,
        double[] ltfOpen,
        double[] ltfClose)
    {
        if (ltfCount <= 0)
        {
            sb.Append("LTF: <empty>");
            return;
        }

        var show = Math.Min(ltfCount, MaxLtfLinesInLabel);
        for (var i = 0; i < show; i++)
        {
            sb.AppendLine(
                $"[{i}] H={Fp(ltfHigh[i])} L={Fp(ltfLow[i])} O={Fp(ltfOpen[i])} C={Fp(ltfClose[i])}");
        }
        if (ltfCount > show)
            sb.Append($"+{ltfCount - show} more");
    }

    static string Fp(double p) =>
        double.IsNaN(p) ? "?" : p.ToString("0.#####");

    static string FpRatio(double r) =>
        double.IsNaN(r) ? "?" : r.ToString("0.##");
}
