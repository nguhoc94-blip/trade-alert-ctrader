using System;
using TradeAlert.Core.Drawing;
using TradeAlert.Core.Models.KeyLevel;

namespace TradeAlert.Core.Engines;

/// <summary>Pine <c>build_ob_box</c> + extend/stop/delete drawing commands.</summary>
public static class ObDrawEngine
{
    public static string ChartObjectName(ObBoxSpec spec) =>
        $"ob_{spec.LeftTimeChartLocal.Ticks}_{spec.Top:F5}_{spec.Source}_{spec.Owner}_{spec.Number:D2}";

    public static (uint Fill, uint Border) ColorsFor(ObBoxSpec spec) =>
        Loop6StylePalette.ObColorsFor(spec.ObType);

    public static ObBoxSpec? BuildFromBar(
        int barIndex,
        int obBar,
        int obType,
        int source,
        int owner,
        int number,
        int lookbackLimit,
        Func<int, (double open, double high, double low, double close)> ohlcAtLag,
        Func<int, DateTime> chartTimeAtLag)
    {
        var (spec, _) = TryBuildFromBar(barIndex, obBar, obType, source, owner, number, lookbackLimit, ohlcAtLag, chartTimeAtLag);
        return spec;
    }

    public static (ObBoxSpec? Spec, ObBoxMissingReason? FailReason) TryBuildFromBar(
        int barIndex,
        int obBar,
        int obType,
        int source,
        int owner,
        int number,
        int lookbackLimit,
        Func<int, (double open, double high, double low, double close)> ohlcAtLag,
        Func<int, DateTime> chartTimeAtLag)
    {
        var lag = barIndex - obBar;
        if (barIndex - obBar > lookbackLimit)
            return (null, ObBoxMissingReason.LookbackExceeded);
        if (lag < 0 || lag > 180)
            return (null, ObBoxMissingReason.OhlcUnavailable);

        var t = ohlcAtLag(lag);
        if (double.IsNaN(t.high) || double.IsNaN(t.low))
            return (null, ObBoxMissingReason.OhlcUnavailable);

        var time = chartTimeAtLag(lag);

        return (new ObBoxSpec
        {
            LeftTimeChartLocal  = time,
            RightTimeChartLocal = time,
            Top    = t.high,
            Bottom = t.low,
            ObType = obType,
            Source = source,
            Owner  = owner,
            Number = number,
        }, null);
    }

    public static void EmitCreate(ObBoxSpec spec, IDrawingCommandSink? sink)
    {
        if (sink == null) return;
        var (fill, border) = ColorsFor(spec);
        sink.Enqueue(new DrawingCommand
        {
            Kind            = DrawingCommandKind.EnqueueObBox,
            ObBoxSpec       = spec,
            LabelKeyStr     = ChartObjectName(spec),
            ColorArgb       = fill,
            BorderColorArgb = border,
        });
    }

    public static void EmitExtend(ObBoxSpec spec, int barIndex, IDrawingCommandSink? sink)
    {
        if (sink == null) return;
        sink.Enqueue(new DrawingCommand
        {
            Kind        = DrawingCommandKind.ExtendObBox,
            LabelKeyStr = ChartObjectName(spec),
            BarIndex    = barIndex,
        });
    }

    public static void EmitStopExtend(ObBoxSpec spec, int pineRightBarIndex, IDrawingCommandSink? sink)
    {
        if (sink == null) return;
        sink.Enqueue(new DrawingCommand
        {
            Kind        = DrawingCommandKind.StopExtendObBox,
            LabelKeyStr = ChartObjectName(spec),
            // Pine box.set_right(t): open of bar after lose touch (time[1] when touch at checkBar).
            // Host maps −1 via KeyLevelVisual.ToChartRightBarIndex (cTrader Time2 spans full candle).
            BarIndex    = pineRightBarIndex,
        });
    }

    public static void EmitDelete(ObBoxSpec? spec, IDrawingCommandSink? sink)
    {
        if (sink == null || spec == null) return;
        sink.Enqueue(new DrawingCommand
        {
            Kind        = DrawingCommandKind.DelObBox,
            LabelKeyStr = ChartObjectName(spec),
        });
    }

    /// <summary>Extend all ZIN OB boxes (state==2, extending) and refresh style from palette.</summary>
    public static void TickExtendAll(ObPoolStore pool, int barIndex, IDrawingCommandSink? sink)
    {
        for (var i = 0; i < pool.Count; i++)
        {
            var r = pool.GetRecord(i);
            if (r.State != 2 || r.Box == null) continue;
            if (r.Extending)
                EmitExtend(r.Box, barIndex, sink);
            EmitUpdateStyle(r.Box, sink);
        }
    }

    /// <summary>Re-apply fill/border from current host palette (parameter changes).</summary>
    public static void EmitUpdateStyle(ObBoxSpec spec, IDrawingCommandSink? sink)
    {
        if (sink == null) return;
        var (fill, border) = ColorsFor(spec);
        sink.Enqueue(new DrawingCommand
        {
            Kind            = DrawingCommandKind.UpdateObBox,
            LabelKeyStr     = ChartObjectName(spec),
            ColorArgb       = fill,
            BorderColorArgb = border,
        });
    }
}
