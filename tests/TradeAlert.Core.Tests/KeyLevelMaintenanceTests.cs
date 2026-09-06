using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.KeyLevel;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class KeyLevelMaintenanceTests
{
    [Fact]
    public void DeleteKeylevel_CascadesToKeyGroupMembers()
    {
        var pivots = new PivotStateStore();
        pivots.PushDefaults(1.0, 10, 1);
        pivots.PushDefaults(1.1, 20, -1);
        pivots.SetHasKey(0, true);
        pivots.SetHasKey(1, true);
        pivots.SetKeyGroupId(1, 0);

        KeyLevelMaintenance.DeleteKeylevel(0, pivots, null, null);

        Assert.False(pivots.GetHasKey(0));
        Assert.False(pivots.GetHasKey(1));
    }

    [Fact]
    public void DeleteKeyBoxOnLock_KeepsHasKey()
    {
        var pivots = new PivotStateStore();
        pivots.PushDefaults(1.0, 10, 1);
        pivots.SetHasKey(0, true);
        pivots.SetFlag(0, 1);

        MainPromotionEngine.LockSwingsBefore(pivots, null, beforeIdx: 1, null, null);

        Assert.Equal(-2, pivots.GetFlag(0));
        Assert.True(pivots.GetHasKey(0));
    }
}
