using System.Collections.Generic;
using TradeAlert.Core.Drawing;
using TradeAlert.Core.Engines;
using Xunit;

namespace TradeAlert.Core.Tests;

public class UtilsEngineFmtTests
{
    [Fact]
    public void FmtH_FmtL_MatchInvariant()
    {
        var u = new UtilsEngine(new NoOpDrawingCommandSink());
        Assert.Equal("H03", u.FmtH(3));
        Assert.Equal("L12", u.FmtL(12));
    }

    sealed class ListSink : IDrawingCommandSink
    {
        public readonly List<DrawingCommand> Commands = new();
        public void Enqueue(in DrawingCommand command) => Commands.Add(command);
    }

    [Fact]
    public void DrawingCommands_Enqueued()
    {
        var sink = new ListSink();
        var u = new UtilsEngine(sink);
        u.AddLabel(1, 1.5, true, "x", 7);
        u.AddLine(0, 0, 1, 1);
        u.DelLabel(3);
        Assert.Equal(3, sink.Commands.Count);
    }
}
