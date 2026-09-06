using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.KeyLevel;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class ObLtfConfirmEngineTests
{
    static LtfRingBufferStore BufferWithBar(int htfBar, double obX, int typ,
        params (double o, double h, double l, double c)[] ltfBars)
    {
        var buf = new LtfRingBufferStore(8);
        var o = new double[ltfBars.Length];
        var h = new double[ltfBars.Length];
        var l = new double[ltfBars.Length];
        var c = new double[ltfBars.Length];
        for (var i = 0; i < ltfBars.Length; i++)
        {
            o[i] = ltfBars[i].o;
            h[i] = ltfBars[i].h;
            l[i] = ltfBars[i].l;
            c[i] = ltfBars[i].c;
        }
        buf.PushSnapshot(htfBar, new LtfBarBundle(o, h, l, c));
        return buf;
    }

    [Fact]
    public void ConfirmCheckBar_NoBuffer_AutoConfirmsNoLtfDataKind()
    {
        var buf = new LtfRingBufferStore(4);
        var r = ObLtfConfirmEngine.ConfirmCheckBar(10, 1.0, -1, buf);
        Assert.Null(r.IsConfirm);
        Assert.Equal(ObLtfConfirmKind.NoLtfData, r.Kind);
        Assert.Equal(1, r.BufferMissBars);

        var pool = new ObPoolStore();
        pool.Push(2, 1, 1.0, 10, -1, 0, 0, 1, -1, 1, 2);
        ObLtfConfirmEngine.ApplyOutcome(0, r, pool, hideObLoseZin: true, sink: null,
            new ObLtfConfirmEngine.ObLtfConfirmApplyContext(10, ObLtfConfirmPath.CheckBar, 0));
        Assert.Equal(2, pool.GetRecord(0).FlagLtf);
        Assert.Equal(ObLtfConfirmKind.NoLtfData, pool.GetRecord(0).LtfConfirmKind);
        Assert.Equal(1, pool.GetRecord(0).LtfBufferMissBars);
    }

    [Fact]
    public void CheckFullRange_EmptyScan_AutoConfirmsNoLtfDataKind()
    {
        var buf = new LtfRingBufferStore(8);
        var r = ObLtfConfirmEngine.CheckFullRange(12, barOb: 11, 1.0, -1, scanRange: 10, buf);
        Assert.Null(r.IsConfirm);
        Assert.Equal(0, r.HasLtfBars);
        Assert.Equal(ObLtfConfirmKind.NoLtfData, r.Kind);

        var pool = new ObPoolStore();
        pool.Push(2, 1, 1.0, 11, -1, 0, 0, 1, -1, 1, 2);
        ObLtfConfirmEngine.ApplyOutcome(0, r, pool, hideObLoseZin: true, sink: null);
        Assert.Equal(ObLtfConfirmKind.NoLtfData, pool.GetRecord(0).LtfConfirmKind);
    }

    [Fact]
    public void ConfirmCheckBar_NoTouch_PersistsScanRangeOnPool()
    {
        var buf = BufferWithBar(10, 1.0, -1, (1.05, 1.08, 1.02, 1.06));
        var r = ObLtfConfirmEngine.ConfirmCheckBar(10, 1.0, -1, buf);

        var pool = new ObPoolStore();
        pool.Push(2, 1, 1.0, 10, -1, 0, 0, 1, -1, 1, 2);
        ObLtfConfirmEngine.ApplyOutcome(0, r, pool, hideObLoseZin: true, sink: null,
            new ObLtfConfirmEngine.ObLtfConfirmApplyContext(10, ObLtfConfirmPath.CheckBar, 3));

        var rec = pool.GetRecord(0);
        Assert.Equal(1.08, rec.LtfScanHigh, 6);
        Assert.Equal(1.02, rec.LtfScanLow, 6);
        Assert.Equal(ObLtfConfirmKind.HasLtfNoTouch, rec.LtfConfirmKind);
        Assert.Equal(1, rec.LtfFlatCandles);
        Assert.Equal(ObLtfConfirmPath.CheckBar, rec.LtfConfirmPath);
    }

    [Fact]
    public void CheckFullRange_MultiLtfBars_AggregatesScanHighLow()
    {
        var buf = new LtfRingBufferStore(8);
        buf.PushSnapshot(11, new LtfBarBundle(
            new[] { 1.05, 1.04 }, new[] { 1.08, 1.07 }, new[] { 1.02, 1.01 }, new[] { 1.06, 1.05 }));

        var r = ObLtfConfirmEngine.CheckFullRange(20, barOb: 10, 1.0, -1, scanRange: 10, buf);

        Assert.True(r.IsConfirm);
        Assert.Equal(ObLtfConfirmKind.HasLtfNoTouch, r.Kind);
        Assert.Equal(1.08, r.LtfScanHigh, 6);
        Assert.Equal(1.01, r.LtfScanLow, 6);
        Assert.Equal(2, r.LtfFlatCandles);
        Assert.True(r.BufferMissBars > 0);
    }

    [Fact]
    public void ConfirmCheckBar_OneTouchFullPierce_StandardKind()
    {
        var buf = BufferWithBar(10, 1.0, -1, (0.9, 1.2, 0.8, 1.1));
        var r = ObLtfConfirmEngine.ConfirmCheckBar(10, 1.0, -1, buf);
        Assert.True(r.IsConfirm);
        Assert.Equal(ObLtfConfirmKind.Standard, r.Kind);
    }

    [Fact]
    public void ConfirmCheckBar_TouchWithoutFullPierce_Rejects()
    {
        var buf = BufferWithBar(10, 1.0, -1, (0.95, 1.2, 0.8, 0.95));
        var r = ObLtfConfirmEngine.ConfirmCheckBar(10, 1.0, -1, buf);
        Assert.False(r.IsConfirm);
    }

    [Fact]
    public void CheckFullRange_TwoTouchesAcrossBars_Rejects()
    {
        var buf = new LtfRingBufferStore(8);
        buf.PushSnapshot(11, new LtfBarBundle(
            new[] { 0.9 }, new[] { 1.2 }, new[] { 0.8 }, new[] { 1.1 }));
        buf.PushSnapshot(12, new LtfBarBundle(
            new[] { 0.9 }, new[] { 1.2 }, new[] { 0.8 }, new[] { 1.1 }));

        var r = ObLtfConfirmEngine.CheckFullRange(20, barOb: 10, 1.0, -1, scanRange: 10, buf);
        Assert.False(r.IsConfirm);
        Assert.Equal(2, r.TouchCount);
    }

    [Fact]
    public void ApplyOutcome_Reject_SetsLoseAndFlag()
    {
        var pool = new ObPoolStore();
        pool.Push(2, 1, 1.0, 10, -1, 0, 0, 1, -1, 1, 2);
        var res = new ObLtfConfirmEngine.ObLtfConfirmResult(false, 2, 1, 3, ObLtfConfirmKind.Pending);
        ObLtfConfirmEngine.ApplyOutcome(0, res, pool, hideObLoseZin: true, sink: null);
        Assert.Equal(3, pool.GetRecord(0).State);
        Assert.Equal(3, pool.GetRecord(0).FlagLtf);
    }
}

public sealed class ObLabelDrawEngineTests
{
    static ObRecord Record(int state, int flagLtf, ObLtfConfirmKind kind) => new()
    {
        State = state,
        FlagLtf = flagLtf,
        LtfConfirmKind = kind,
        Source = 0,
        Bar = 100,
        Count = 1,
        LastCheckedBar = 120,
    };

    static ObRecord RecordHasLtfNoTouch(double x, double scanHigh, double scanLow) => new()
    {
        State = 2,
        FlagLtf = 2,
        LtfConfirmKind = ObLtfConfirmKind.HasLtfNoTouch,
        X = x,
        LtfScanHigh = scanHigh,
        LtfScanLow = scanLow,
        Source = 0,
        Bar = 480,
        Count = 1,
        LastCheckedBar = 502,
        LtfHasBars = 1,
        LtfTotalBars = 1,
        LtfFlatCandles = 3,
        LtfTouchCount = 0,
        LtfConfirmBar = 502,
        LtfConfirmPath = ObLtfConfirmPath.CheckBar,
        LtfBufferFlatAtConfirm = 150,
        LtfScanMaxBar = 500,
    };

    [Theory]
    [InlineData(ObLtfConfirmKind.Standard, "OB")]
    [InlineData(ObLtfConfirmKind.NoLtfData, "OB no LTF data")]
    public void BuildZinLabelText_ConfirmedKinds(ObLtfConfirmKind kind, string expected) =>
        Assert.StartsWith(expected, ObLabelDrawEngine.BuildZinLabelText(Record(2, 2, kind)));

    [Fact]
    public void BuildZinLabelText_HasLtfNoTouch_IncludesScanHighLowAndDebug()
    {
        var text = ObLabelDrawEngine.BuildZinLabelText(RecordHasLtfNoTouch(1.0, 1.08, 1.02));
        Assert.Contains("OB has LTF but not real H=1.08 L=1.02 x=1", text);
        Assert.Contains("1Bar", text);
        Assert.Contains("m5=3", text);
        Assert.Contains("htfLtf=1/1", text);
    }

    [Fact]
    public void BuildZinLabelText_HasLtfNoTouch_FallbackWithoutScanRange()
    {
        var text = ObLabelDrawEngine.BuildZinLabelText(Record(2, 2, ObLtfConfirmKind.HasLtfNoTouch));
        Assert.StartsWith("OB has LTF but not real", text);
        Assert.Contains("st=2", text);
    }

    [Fact]
    public void BuildZinLabelText_HasLtfMiss_ShowsMissHistHint()
    {
        var r = RecordHasLtfNoTouch(1.0, 1.08, 1.02);
        r.LtfBufferMissBars = 40;
        r.LtfTotalBars = 50;
        r.LtfHasBars = 10;
        r.LtfConfirmPath = ObLtfConfirmPath.FullRange;
        var text = ObLabelDrawEngine.BuildZinLabelText(r);
        Assert.Contains("MISS_RING", text);
        Assert.Contains("miss=40", text);
    }

    [Fact]
    public void BuildZinLabelText_PendingZin_ShowsCanary() =>
        Assert.Equal("OB?", ObLabelDrawEngine.BuildZinLabelText(Record(2, 0, ObLtfConfirmKind.Pending)));

    [Fact]
    public void BuildZinLabelText_PendingOb_ShowsOb() =>
        Assert.Equal("OB", ObLabelDrawEngine.BuildZinLabelText(Record(0, 0, ObLtfConfirmKind.Pending)));
}
