using System.Collections.Generic;
using TradeAlert.Core.Drawing;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.KeyLevel;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class ObOverlapEngineTests
{
    sealed class CollectSink : IDrawingCommandSink
    {
        public readonly List<DrawingCommand> Commands = new();
        public void Enqueue(in DrawingCommand cmd) => Commands.Add(cmd);
    }

    [Fact]
    public void OverlapGroup_KeepsNewestBar_DeletesOlderBox()
    {
        var pool = new ObPoolStore();
        pool.Push(2, 1, 1.0, 10, 1, 0, 0, 1, 1, 1, 2);
        pool.SetBox(0, new ObBoxSpec { Top = 2, Bottom = 1 });
        pool.Push(2, 1, 1.05, 20, 1, 0, 0, 2, 1, 2, 3);
        pool.SetBox(1, new ObBoxSpec { Top = 2.1, Bottom = 0.9 });

        var sink = new CollectSink();
        ObOverlapEngine.LimitOverlappingObBoxes(
            pool, 25,
            lag => (1, 2, 0.5, 1.5),
            sink);

        Assert.Null(pool.GetRecord(0).Box);
        Assert.NotNull(pool.GetRecord(1).Box);
        Assert.True(pool.GetRecord(0).OverlapRival.HasValue);
        Assert.Equal(20, pool.GetRecord(0).OverlapRival.Bar);
        Assert.Equal(2, pool.GetRecord(0).OverlapRival.Number);
        Assert.Contains(sink.Commands, c => c.Kind == DrawingCommandKind.DelObBox);
    }
}
