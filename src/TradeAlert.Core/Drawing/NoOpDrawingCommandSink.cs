namespace TradeAlert.Core.Drawing;

public sealed class NoOpDrawingCommandSink : IDrawingCommandSink
{
    public void Enqueue(in DrawingCommand command) { }
}
