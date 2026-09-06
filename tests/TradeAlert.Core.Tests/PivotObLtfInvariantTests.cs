using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.KeyLevel;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class PivotObLtfInvariantTests
{
    [Fact]
    public void PivotStateStore_PushRemove_KeepsColumnSync()
    {
        var s = new PivotStateStore();
        s.PushDefaults(1, 10, 1);
        s.PushDefaults(2, 11, -1);
        Assert.True(s.InvariantAllColumnsSameCount());
        Assert.Equal(2, s.Count);
        s.RemoveAt(0);
        Assert.True(s.InvariantAllColumnsSameCount());
        Assert.Equal(1, s.Count);
    }

    [Fact]
    public void ObPoolStore_PushRemove_KeepsColumnSync()
    {
        var p = new ObPoolStore();
        p.PushDefaults();
        p.PushDefaults();
        Assert.True(p.InvariantAllColumnsSameCount());
        p.RemoveAt(1);
        Assert.True(p.InvariantAllColumnsSameCount());
    }

    [Fact]
    public void OBEngine_Lifecycle_Stub_IsObsolete()
    {
        // ObLifecycleTransitionNotMapped is now a no-op stub — lifecycle is ported via Tick().
        var ob = new OBEngine();
#pragma warning disable CS0618
        ob.ObLifecycleTransitionNotMapped(); // should not throw
#pragma warning restore CS0618
    }

    [Fact]
    public void ObEngine_TryCaptureIdentity_RoundTrip()
    {
        var p = new ObPoolStore();
        p.PushDefaults();
        var ob = new OBEngine();
        Assert.True(ob.TryCaptureObIdentity(p, 0, out var snap));
        Assert.Equal(0, snap.PoolIndex);
    }

    [Fact]
    public void LtfRingBufferSpec_InvalidStartEnd_ReturnsFalseFromInvariant()
    {
        var r = new LtfRingBufferSpec(4);
        r.SetSlot(0, 1, 10, 9);
        Assert.False(r.InvariantIndicesInRange());
    }

    [Fact]
    public void LtfRingBufferSpec_ValidSlot_PassesInvariant()
    {
        var r = new LtfRingBufferSpec(4);
        r.SetSlot(0, 1, 2, 8);
        Assert.True(r.InvariantIndicesInRange());
    }
}
