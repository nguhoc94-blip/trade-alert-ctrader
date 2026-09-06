using TradeAlert.BacktestRobot.Execution;
using TradeAlert.Core.Models.Alerts;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests;

public class BBrokenPositionHandlerTests
{
    static TrackedSetup Setup(string label = "L6BT|R1|B50", double entry = 100, double tp = 104) => new()
    {
        Context = new PendingOrderContext
        {
            Label = label,
            IsBuy = true,
            EntryPrice = entry,
            OriginalTakeProfit = tp,
            SwingBPivotBar = 50,
            Direction = SignalDirection.Buy,
        },
        CurrentTakeProfit = tp,
    };

    static BBrokenHandlerConfig MoveTpCfg() => new()
    {
        EnableMoveTpToEntryOnBBroken = true,
        OpenPositionMode = BBrokenOpenPositionActionMode.MoveTpToEntry,
        MoveTpRejectMode = BBrokenTpRejectFallbackMode.CloseImmediately,
        TickSize = 0.01,
    };

    [Fact]
    public void Pending_ShouldCancel()
    {
        var pending = BBrokenPositionHandler.EvaluatePending("L6BT|R1|B50", hasPendingOrder: true);
        Assert.True(pending.ShouldCancelPending);
        Assert.Contains("CANCEL", BBrokenPositionHandler.FormatPendingCancelLog(pending.Label));
    }

    [Fact]
    public void OpenPosition_MoveTpToEntry()
    {
        var action = BBrokenPositionHandler.EvaluateOpenPosition(
            Setup(), hasOpenPosition: true, positionTakeProfit: 104, MoveTpCfg());

        Assert.NotNull(action);
        Assert.True(action!.ShouldMoveTpToEntry);
        Assert.False(action.ShouldCloseImmediately);
        Assert.Equal(100, action.NewTakeProfit);
    }

    [Fact]
    public void OpenPosition_SkipsWhenAlreadyAtEntry()
    {
        var action = BBrokenPositionHandler.EvaluateOpenPosition(
            Setup(tp: 100), hasOpenPosition: true, positionTakeProfit: 100, MoveTpCfg());

        Assert.NotNull(action);
        Assert.True(action!.AlreadyAtEntry);
        Assert.False(action.ShouldMoveTpToEntry);
    }

    [Fact]
    public void OpenPosition_CloseImmediatelyMode()
    {
        var cfg = new BBrokenHandlerConfig
        {
            EnableMoveTpToEntryOnBBroken = true,
            OpenPositionMode = BBrokenOpenPositionActionMode.CloseImmediately,
            MoveTpRejectMode = BBrokenTpRejectFallbackMode.CloseImmediately,
            TickSize = 0.01,
        };
        var action = BBrokenPositionHandler.EvaluateOpenPosition(
            Setup(), hasOpenPosition: true, positionTakeProfit: 104, cfg);

        Assert.NotNull(action);
        Assert.True(action!.ShouldCloseImmediately);
    }

    [Fact]
    public void OpenPosition_SkipsSecondMoveWhenFlagSet()
    {
        var setup = Setup();
        setup.TpMovedToEntryOnBBroken = true;
        setup.CurrentTakeProfit = 104;

        var action = BBrokenPositionHandler.EvaluateOpenPosition(
            setup, hasOpenPosition: true, positionTakeProfit: 104, MoveTpCfg());

        Assert.NotNull(action);
        Assert.True(action!.AlreadyAtEntry);
    }
}
