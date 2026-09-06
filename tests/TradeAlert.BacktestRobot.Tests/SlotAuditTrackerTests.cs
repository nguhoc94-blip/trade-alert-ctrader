using TradeAlert.BacktestRobot.Execution;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests;

public class SlotAuditTrackerTests
{
    [Fact]
    public void RecordsPerSlotIndependently()
    {
        var t = new SlotAuditTracker();
        t.RecordFire(0);
        t.RecordFire(2);
        t.RecordFire(2);
        t.RecordPlan(2);
        t.RecordGatePass(2);
        t.RecordOrder(2);
        t.RecordPendingCancelPostBPushObstacle(2);

        Assert.Equal((1, 0, 0, 0, 0), t.Get(0));
        Assert.Equal((2, 1, 1, 0, 1), t.Get(2));
        Assert.Equal(1, t.GetPendingCancelPostBPushObstacle(2));
        Assert.Equal((0, 0, 0, 0, 0), t.Get(5));
    }
}
