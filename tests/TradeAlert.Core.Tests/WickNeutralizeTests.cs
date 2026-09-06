using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.KeyLevel;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class WickNeutralizeTests
{
    static (double o, double h, double l, double c) Ohlc(int off) => off switch
    {
        1 => (100.0, 110.0, 99.0, 101.0),  // large upper wick
        _ => (100.0, 101.0, 99.0, 100.5),
    };

    [Fact]
    public void Neutralize_Disabled_ReturnsRaw()
    {
        var r = WickNeutralizeEngine.NeutralizeBarAtOffset(
            useWickNoiseFilter: false, wickAvgLen: 10, useLtfWickConfirm: false,
            ltfConfirmRatio: 0.5, offset: 1, Ohlc);
        Assert.Equal(110.0, r.CleanHigh);
        Assert.Equal(99.0, r.CleanLow);
    }

    [Fact]
    public void Neutralize_NoLtfBundle_DoesNotNeutralize()
    {
        var r = WickNeutralizeEngine.NeutralizeBarAtOffset(
            useWickNoiseFilter: true, wickAvgLen: 2, useLtfWickConfirm: true,
            ltfConfirmRatio: 0.5, offset: 1, Ohlc, ltfAtOffset: null);
        Assert.Equal(110.0, r.CleanHigh);
        Assert.Null(r.NeutralizedHigh);
    }

    [Fact]
    public void Neutralize_LtfPenetration_ConfirmsUpperWick()
    {
        // upper wick = 9; LTF max body = 101 → penetration 9/9 = 1.0 > 0.5
        var ltf = new LtfBarBundle(
            new[] { 100.0, 100.5 },
            new[] { 101.0, 101.0 },
            new[] { 99.5, 99.8 },
            new[] { 101.0, 100.8 });

        var r = WickNeutralizeEngine.NeutralizeBarAtOffset(
            useWickNoiseFilter: true, wickAvgLen: 2, useLtfWickConfirm: true,
            ltfConfirmRatio: 0.5, offset: 1, Ohlc, ltf);
        Assert.Equal(101.0, r.CleanHigh);
        Assert.NotNull(r.NeutralizedHigh);

        var (_, diag) = WickNeutralizeEngine.NeutralizeBarAtOffsetDetailed(
            useWickNoiseFilter: true, wickAvgLen: 2, useLtfWickConfirm: true,
            ltfConfirmRatio: 0.5, offset: 1, Ohlc, ltf);
        Assert.True(diag.UseLtfMode);
        Assert.Equal(2, diag.LtfBarCount);
        Assert.True(diag.ValidUpperWick);
        Assert.True(diag.UpperConfirmed);
        Assert.True(diag.UpperPenetrationRatio > 0.5);
    }
}
