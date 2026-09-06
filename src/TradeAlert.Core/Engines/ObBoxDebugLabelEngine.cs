using TradeAlert.Core.Drawing;
using TradeAlert.Core.Models.KeyLevel;

namespace TradeAlert.Core.Engines;

/// <summary>
/// Debug labels at OB bars explaining why no rectangle is drawn.
/// Prefix <c>dbg_ob_box_</c> — independent from pivot/OB ZIN labels.
/// </summary>
public static class ObBoxDebugLabelEngine
{
    public const uint MissingColorArgb = 0xFFFFAB40u; // amber

    public static string ChartObjectName(ObRecord r) =>
        $"dbg_ob_box_{r.Owner}_{r.Source}_{r.Bar}_{r.Number:D2}";

    public static ObBoxMissingReason ResolveReason(in ObRecord r)
    {
        if (r.Box != null)
            return ObBoxMissingReason.None;

        if (r.BoxMissingReason != ObBoxMissingReason.None)
            return r.BoxMissingReason;

        return r.State switch
        {
            0 => ObBoxMissingReason.Pending,
            3 when r.FlagLtf == 3 => ObBoxMissingReason.LtfReject,
            3 => ObBoxMissingReason.HtfLose,
            2 => ObBoxMissingReason.ShowObBoxOff,
            _ => ObBoxMissingReason.None,
        };
    }

    public static string FormatReasonText(in ObRecord r, ObBoxMissingReason reason)
    {
        var core = reason switch
        {
            ObBoxMissingReason.Pending => "pending (chưa ZIN)",
            ObBoxMissingReason.ShowObBoxOff => "ShowObBox=off",
            ObBoxMissingReason.LookbackExceeded => "lookback vượt giới hạn",
            ObBoxMissingReason.OhlcUnavailable => "không có OHLC bar OB",
            ObBoxMissingReason.OverlapTrimmed => "overlap (OB mới hơn)",
            ObBoxMissingReason.LtfReject => "LTF reject (DelObBox)",
            ObBoxMissingReason.HtfLose => "HTF LOSE (stop extend)",
            _ => "",
        };

        if (string.IsNullOrEmpty(core))
            return "";

        var text = $"NO-BOX: {core}";
        if (reason == ObBoxMissingReason.OverlapTrimmed)
        {
            text += FormatOverlapDebug(in r);
        }
        if (r.State == 2 && r.FlagLtf == 2)
        {
            var ltf = ObLabelDrawEngine.BuildZinLabelText(r);
            if (!string.IsNullOrEmpty(ltf) && ltf != "OB")
                text += $" | {ltf}";
        }

        return text;
    }

    /// <summary>OB này (mất box) + OB mới thắng overlap.</summary>
    public static string FormatOverlapDebug(in ObRecord loser)
    {
        var lost = FormatObIdentity(
            loser.Source, loser.PivotType, loser.PivotHighId, loser.PivotLowId,
            loser.Number, loser.Bar);
        var v = loser.OverlapRival;
        if (!v.HasValue)
            return $" | mất: {lost}";

        var kept = FormatObIdentity(
            v.Source, v.PivotType, v.PivotHighId, v.PivotLowId,
            v.Number, v.Bar);
        return $" | mất: {lost} → OB mới: {kept}";
    }

    /// <summary>Compact rival OB id (legacy fragment).</summary>
    public static string FormatOverlapRival(in ObRecord r)
    {
        var v = r.OverlapRival;
        if (!v.HasValue)
            return "";

        return $" → OB mới: {FormatObIdentity(v.Source, v.PivotType, v.PivotHighId, v.PivotLowId, v.Number, v.Bar)}";
    }

    public static string FormatObIdentity(in ObRecord r) =>
        FormatObIdentity(r.Source, r.PivotType, r.PivotHighId, r.PivotLowId, r.Number, r.Bar);

    public static string FormatObIdentity(
        int source, int pivotType, int pivotHighId, int pivotLowId, int number, int bar)
    {
        var src = source == 0 ? "P" : "S";
        var swing = pivotType == 1
            ? $"H{pivotHighId:D3}"
            : $"L{pivotLowId:D3}";
        return $"{src} {swing} #{number:D2} bar={bar}";
    }

    public static void RefreshPool(
        ObPoolStore pool,
        int evalBarIndex,
        int lookbackBars,
        Func<int, (double open, double high, double low, double close)>? ohlcAtLag,
        IDrawingCommandSink? sink)
    {
        if (sink == null)
            return;

        for (var i = 0; i < pool.Count; i++)
        {
            var r = pool.GetRecord(i);
            var key = ChartObjectName(r);
            if (evalBarIndex - r.Bar > lookbackBars || r.Bar < 0)
            {
                EmitDelete(key, sink);
                continue;
            }

            if (r.Box != null)
            {
                EmitDelete(key, sink);
                continue;
            }

            var reason = ResolveReason(in r);
            if (reason == ObBoxMissingReason.None)
            {
                EmitDelete(key, sink);
                continue;
            }

            var labelText = FormatReasonText(in r, reason);
            if (string.IsNullOrEmpty(labelText))
            {
                EmitDelete(key, sink);
                continue;
            }

            var price = ResolveLabelPrice(evalBarIndex, in r, ohlcAtLag);
            sink.Enqueue(new DrawingCommand
            {
                Kind        = DrawingCommandKind.AddLabel,
                LabelKeyStr = key,
                BarIndex    = r.Bar,
                Price       = price,
                IsHigh      = r.Type == 1,
                Text        = labelText,
                ColorArgb   = MissingColorArgb,
            });
        }
    }

    static double ResolveLabelPrice(
        int evalBarIndex,
        in ObRecord r,
        Func<int, (double open, double high, double low, double close)>? ohlcAtLag)
    {
        if (ohlcAtLag != null)
        {
            var lag = evalBarIndex - r.Bar;
            if (lag >= 0 && lag <= 180)
            {
                var (_, h, l, _) = ohlcAtLag(lag);
                if (!double.IsNaN(h) && !double.IsNaN(l))
                    return r.Type == 1 ? h : l;
            }
        }

        return r.X;
    }

    public static void ClearPool(ObPoolStore pool, IDrawingCommandSink? sink)
    {
        if (sink == null)
            return;

        for (var i = 0; i < pool.Count; i++)
            EmitDelete(ChartObjectName(pool.GetRecord(i)), sink);
    }

    static void EmitDelete(string key, IDrawingCommandSink sink) =>
        sink.Enqueue(new DrawingCommand { Kind = DrawingCommandKind.DelLabel, LabelKeyStr = key });
}
