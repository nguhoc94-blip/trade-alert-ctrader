using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.KeyLevel;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class ObBoxDebugLabelEngineTests
{
    static ObRecord Record(
        int state = 2,
        int flagLtf = 2,
        ObLtfConfirmKind kind = ObLtfConfirmKind.NoLtfData,
        ObBoxMissingReason missing = ObBoxMissingReason.None,
        ObBoxSpec? box = null) => new()
    {
        State = state,
        FlagLtf = flagLtf,
        LtfConfirmKind = kind,
        BoxMissingReason = missing,
        Box = box,
        Bar = 100,
        Type = -1,
        Owner = 3,
        Source = 0,
        Number = 1,
        X = 1.05,
    };

    [Theory]
    [InlineData(ObBoxMissingReason.Pending, "NO-BOX: pending (chưa ZIN)")]
    [InlineData(ObBoxMissingReason.ShowObBoxOff, "NO-BOX: ShowObBox=off")]
    [InlineData(ObBoxMissingReason.LookbackExceeded, "NO-BOX: lookback vượt giới hạn")]
    [InlineData(ObBoxMissingReason.OverlapTrimmed, "NO-BOX: overlap (OB mới hơn)")]
    [InlineData(ObBoxMissingReason.LtfReject, "NO-BOX: LTF reject (DelObBox)")]
    public void FormatReasonText_KnownReasons(ObBoxMissingReason reason, string expectedPrefix)
    {
        var text = ObBoxDebugLabelEngine.FormatReasonText(Record(missing: reason), reason);
        Assert.StartsWith(expectedPrefix, text);
    }

    [Fact]
    public void FormatReasonText_ZinNoLtf_AppendsLtfKind()
    {
        var r = Record(kind: ObLtfConfirmKind.NoLtfData, missing: ObBoxMissingReason.ShowObBoxOff);
        var text = ObBoxDebugLabelEngine.FormatReasonText(r, ObBoxMissingReason.ShowObBoxOff);
        Assert.Contains("OB no LTF data", text);
    }

    [Fact]
    public void ResolveReason_HasBox_ReturnsNone()
    {
        var r = Record(box: new ObBoxSpec { Top = 1.1, Bottom = 1.0 });
        Assert.Equal(ObBoxMissingReason.None, ObBoxDebugLabelEngine.ResolveReason(in r));
    }

    [Fact]
    public void ResolveReason_State0_FallsBackToPending()
    {
        var r = Record(state: 0, flagLtf: 0, kind: ObLtfConfirmKind.Pending, missing: ObBoxMissingReason.None);
        Assert.Equal(ObBoxMissingReason.Pending, ObBoxDebugLabelEngine.ResolveReason(in r));
    }

    [Fact]
    public void FormatReasonText_Overlap_IncludesLoserAndRival()
    {
        var r = Record(missing: ObBoxMissingReason.OverlapTrimmed);
        r.Source = 1;
        r.PivotType = 1;
        r.PivotHighId = 753;
        r.Number = 2;
        r.Bar = 995;
        r.OverlapRival = new ObOverlapRival(1000, 0, 2, 1, 1, 753, 0);
        var text = ObBoxDebugLabelEngine.FormatReasonText(r, ObBoxMissingReason.OverlapTrimmed);
        Assert.Contains("mất: S H753 #02 bar=995", text);
        Assert.Contains("OB mới: P H753 #01 bar=1000", text);
    }

    [Fact]
    public void FormatOverlapRival_NoRival_ReturnsEmpty()
    {
        var r = Record(missing: ObBoxMissingReason.OverlapTrimmed);
        Assert.Equal("", ObBoxDebugLabelEngine.FormatOverlapRival(in r));
    }

    [Fact]
    public void TryBuildFromBar_LookbackExceeded_ReturnsReason()
    {
        var (_, fail) = ObDrawEngine.TryBuildFromBar(
            barIndex: 300,
            obBar: 50,
            obType: -1,
            source: 0,
            owner: 0,
            number: 1,
            lookbackLimit: 200,
            _ => (1, 1.1, 1.0, 1.05),
            _ => default);
        Assert.Equal(ObBoxMissingReason.LookbackExceeded, fail);
    }
}
