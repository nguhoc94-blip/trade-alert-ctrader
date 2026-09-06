using System;
using TradeAlert.Core.Models.Alerts;
using Xunit;

namespace TradeAlert.Core.Tests.Alerts;

public sealed class CompoundSourceEmitDedupTests
{
    static CompoundFireStageRecord EmittedRecord(
        string symbol,
        string ruleId,
        SignalDirection dir,
        string sourceTf,
        int sourceBar) =>
        new()
        {
            Symbol = symbol,
            RuleId = ruleId,
            Direction = dir,
            SourceTfToken = sourceTf,
            SourceBarIndex = sourceBar,
            SourceEventOpenTimeUtc = new DateTime(2024, 6, 18, 10, 45, 0),
            SlotIndex = ruleId == "R2" ? 1 : 2,
            Status = CompoundFireStageStatus.Emitted,
            EmittedAtChartBarIndex = 234,
        };

    [Theory]
    [InlineData("R1", "5", 10050)]
    [InlineData("R2", "5", 10050)]
    [InlineData("R3", "15", 534)]
    [InlineData("R4", "15", 781)]
    [InlineData("R5", "15", 600)]
    [InlineData("R6", "15", 601)]
    public void StageLedger_source_key_emits_once_per_source_bar(string ruleId, string sourceTf, int sourceBar)
    {
        var ledger = new CompoundFireStageLedger();
        var key = CompoundSourceEventKey.Build("EURUSD", ruleId, SignalDirection.Buy, sourceTf, sourceBar);
        var record = EmittedRecord("EURUSD", ruleId, SignalDirection.Buy, sourceTf, sourceBar);

        ledger.MarkSourceEmitted(record);
        ledger.MarkSourceEmitted(record);

        Assert.True(ledger.IsSourceEventEmitted(key));
        Assert.Equal(1, ledger.Count);
    }

    [Fact]
    public void StageLedger_different_rules_same_source_bar_are_independent()
    {
        var ledger = new CompoundFireStageLedger();
        ledger.MarkSourceEmitted(EmittedRecord("EURUSD", "R3", SignalDirection.Buy, "15", 534));
        ledger.MarkSourceEmitted(EmittedRecord("EURUSD", "R5", SignalDirection.Buy, "15", 534));

        Assert.Equal(2, ledger.Count);
    }

    [Fact]
    public void CandidateBarDedupeKey_collapses_duplicate_M15_collection()
    {
        var c1 = new CompoundSourceEventCandidate
        {
            Symbol = "EURUSD",
            SourceTfToken = "15",
            SourceBarIndex = 534,
        };
        var c2 = new CompoundSourceEventCandidate
        {
            Symbol = "EURUSD",
            SourceTfToken = "15",
            SourceBarIndex = 534,
        };

        Assert.Equal(
            CompoundSourceEventKey.CandidateBarDedupeKey(in c1),
            CompoundSourceEventKey.CandidateBarDedupeKey(in c2));
    }
}
