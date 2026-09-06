using System.Globalization;
using TradeAlert.Core.Drawing;

namespace TradeAlert.Core.Engines;

/// <summary>Mapping từ lib_utils — Fmt + drawing command queue.</summary>
public sealed class UtilsEngine
{
    readonly IDrawingCommandSink _sink;

    public UtilsEngine(IDrawingCommandSink sink) => _sink = sink;

    public string FmtH(int id) => "H" + id.ToString("00", CultureInfo.InvariantCulture);

    public string FmtL(int id) => "L" + id.ToString("00", CultureInfo.InvariantCulture);

    public void AddLabel(int barIndex, double price, bool isHigh, string text, int styleToken = 0)
    {
        _sink.Enqueue(new DrawingCommand
        {
            Kind = DrawingCommandKind.AddLabel,
            BarIndex = barIndex,
            Price = price,
            IsHigh = isHigh,
            Text = text,
            LabelKey = styleToken
        });
    }

    public void AddLine(int x1, double y1, int x2, double y2)
    {
        _sink.Enqueue(new DrawingCommand
        {
            Kind = DrawingCommandKind.AddLine,
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2
        });
    }

    public void DelLabel(int labelToken)
    {
        _sink.Enqueue(new DrawingCommand { Kind = DrawingCommandKind.DelLabel, LabelKey = labelToken });
    }

    public void DelLine(int lineToken)
    {
        _sink.Enqueue(new DrawingCommand { Kind = DrawingCommandKind.DelLine, LineKey = lineToken });
    }
}
