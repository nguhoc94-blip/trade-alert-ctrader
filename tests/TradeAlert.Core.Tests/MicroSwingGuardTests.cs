using System;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.KeyLevel;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class MicroSwingGuardTests
{
    sealed class FakePivots : IPivotReadOnlyView
    {
        public int Count => 1;
        public int GetBarIndex(int i) => i == 0 ? 5 : 0;
        public double GetPrice(int i) => 100;
        public int GetType(int i) => 1;
        public bool TryGetKeyBoxRef(int i, out KeyBoxRef? keyBoxRef)
        {
            keyBoxRef = null;
            return true;
        }
    }

    /// <summary>Emulates Pine cleanLow1Raw[off] / cleanHigh1Raw[off] (bar = barIndex - 1 - off).</summary>
    sealed class FakeClean : ICleanOhlcSeries
    {
        public double CleanLowAtOffset(int off)  => 90 + off;
        public double CleanHighAtOffset(int off) => 110 - off;
    }

    // -----------------------------------------------------------------------
    // Legacy stub behaviour
    // -----------------------------------------------------------------------

    [Fact]
    public void MicroSwing3etect_Legacy_ThrowsInvalidOperation()
    {
        // MicroSwing3etect() is now a deprecated stub — real logic is in Detect().
        var e = new MicroSwingEngine();
#pragma warning disable CS0618
        Assert.Throws<InvalidOperationException>(() => e.MicroSwing3etect());
#pragma warning restore CS0618
    }

    // -----------------------------------------------------------------------
    // Guard dependency checks
    // -----------------------------------------------------------------------

    [Fact]
    public void TryMicroSwing3etect_MissingPivot_ReturnsFalseWithReason()
    {
        var e = new MicroSwingEngine();
        var ctx = new MicroSwingEngine.MicroSwingGuardContext(
            null,
            new FakeClean(),
            PendingCandidatesAvailable: true,
            PivotKeyBoxesAligned: Array.Empty<KeyBoxRef?>());
        Assert.False(e.TryMicroSwing3etect(ctx, out var reason));
        Assert.Equal(MicroSwingEngine.MicroSwingGuardReason.MissingPivotView, reason);
    }

    [Fact]
    public void TryMicroSwing3etect_FullContext_ReturnsTrue_AllDepsPresent()
    {
        // After porting Detect(), TryMicroSwing3etect returns true when all deps present.
        var e = new MicroSwingEngine();
        var spec = new KeyBoxSpec
        {
            LeftTimeChartLocal  = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Unspecified),
            RightTimeChartLocal = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Unspecified),
            Top = 1, Bottom = 0, HighColorArgb = 0, LowColorArgb = 0, Opacity = 0, ExtendRight = true
        };
        var kref = new KeyBoxRef { Spec = spec };
        var ctx = new MicroSwingEngine.MicroSwingGuardContext(
            new FakePivots(),
            new FakeClean(),
            PendingCandidatesAvailable: true,
            PivotKeyBoxesAligned: new[] { kref });
        Assert.True(e.TryMicroSwing3etect(ctx, out var reason));
        Assert.Equal(MicroSwingEngine.MicroSwingGuardReason.None, reason);
    }

    // -----------------------------------------------------------------------
    // Detect() logic tests
    // -----------------------------------------------------------------------

    [Fact]
    public void Detect_NoTrigger_WhenCloseDoesNotReachLevel()
    {
        // closeA=95, nTyp=1 (HIGH), kb=null so lvl=pivotPrice=100 → closeA<100 → no trigger
        var e    = new MicroSwingEngine();
        var penH = new PendingSwingQueue();
        var penL = new PendingSwingQueue();
        var result = e.Detect(
            useMicroSwingRule: true,
            barIndex: 20,
            pivots: new FakePivots(),   // nBar=5, nTyp=1, nPr=100
            effClearBody: true,
            effIsDoji: false,
            effBull: true,
            effBear: false,
            closeA: 95.0,              // < 100 → no trigger
            lastPushedSwingType: 1,
            clean: new FakeClean(),
            penH: penH,
            penL: penL);
        Assert.False(result.Detected);
    }

    [Fact]
    public void Detect_Trigger_WhenBullCloseAboveLevel()
    {
        // closeA=105, nTyp=1 (HIGH), kb=null so lvl=100 → closeA>=100 → trigger LOW rescue
        // barIndex=20, nBar=5 → aBar-nBar=14 >= 3 ✓
        // Clean.CleanLowAtOffset scans offsets 1..13, returns 90+off → min at off=1 → bar=18, price=91
        var e    = new MicroSwingEngine();
        var penH = new PendingSwingQueue();
        var penL = new PendingSwingQueue();
        var result = e.Detect(
            useMicroSwingRule: true,
            barIndex: 20,
            pivots: new FakePivots(),   // nBar=5, nTyp=1, nPr=100
            effClearBody: true,
            effIsDoji: false,
            effBull: true,
            effBear: false,
            closeA: 105.0,
            lastPushedSwingType: 1,
            clean: new FakeClean(),
            penH: penH,
            penL: penL);
        Assert.True(result.Detected);
        Assert.Equal(-1, result.MTyp);   // rescue LOW
        Assert.Equal(18, result.MBar);   // barIndex-1-off=20-1-1=18
        Assert.Equal(91.0, result.MPrice); // CleanLow(1)=90+1=91
    }
}
