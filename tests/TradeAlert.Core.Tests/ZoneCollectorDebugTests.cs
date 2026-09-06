using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.KeyLevel;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class ZoneCollectorDebugTests
{
    [Fact]
    public void DescribePivotCollectOutcome_MirrorsZoneCollectorGreenLow()
    {
        var kb = new KeyBoxRef { Spec = new KeyBoxSpec { Top = 101, Bottom = 100 } };
        var outcome = ZoneCollectorDebug.DescribePivotCollectOutcome(
            hasKey: true,
            kb: kb,
            keyExtending: true,
            typ: -1,
            flag: 1,
            mainRole: 0);
        Assert.Equal("GKL", outcome);
    }

    [Fact]
    public void DescribePivotCollectOutcome_SkipsNonExtending()
    {
        var kb = new KeyBoxRef { Spec = new KeyBoxSpec { Top = 101, Bottom = 100 } };
        var outcome = ZoneCollectorDebug.DescribePivotCollectOutcome(
            hasKey: true,
            kb: kb,
            keyExtending: false,
            typ: -1,
            flag: 1,
            mainRole: 0);
        Assert.Equal("SKIP !keyExtending", outcome);
    }

    [Fact]
    public void DescribePivotCollectOutcome_BrokenHighIsGreen()
    {
        var kb = new KeyBoxRef { Spec = new KeyBoxSpec { Top = 101, Bottom = 100 } };
        var outcome = ZoneCollectorDebug.DescribePivotCollectOutcome(
            hasKey: true,
            kb: kb,
            keyExtending: true,
            typ: 1,
            flag: 2,
            mainRole: 0);
        Assert.Equal("GKL", outcome);
    }
}
