namespace TradeAlert.Core.Drawing;

public interface IDrawingCommandSink
{
    void Enqueue(in DrawingCommand command);
}
