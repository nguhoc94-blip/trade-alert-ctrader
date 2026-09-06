using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.KeyLevel;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class WickNoiseDebugLabelTests
{
    static (double, double, double, double) Ohlc(int lag) => lag switch
    {
        1 => (100.0, 105.0, 99.0, 104.0),
        2 => (99.0, 101.0, 98.5, 100.5),
        _ => (98.0, 100.0, 97.0, 99.0),
    };

    [Fact]
    public void Neutralize_populates_ltf_max_high_and_min_low()
    {
        var ltf = new LtfBarBundle(
            new[] { 100.0, 101.0 },
            new[] { 104.5, 105.2 },
            new[] { 99.5, 99.8 },
            new[] { 104.0, 101.5 });

        var (_, diag) = WickNeutralizeEngine.NeutralizeBarAtOffsetDetailed(
            useWickNoiseFilter: true,
            wickAvgLen: 2,
            useLtfWickConfirm: true,
            ltfConfirmRatio: 0.5,
            offset: 1,
            Ohlc,
            ltf);

        Assert.Equal(105.2, diag.LtfMaxHigh);
        Assert.Equal(99.5, diag.LtfMinLow);
        Assert.Equal(2, diag.LtfBarCount);
    }
}
