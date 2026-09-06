using System;
using TradeAlert.Core.Models.Alerts;
using Xunit;

namespace TradeAlert.Core.Tests.Alerts;

public sealed class CompoundFireStageLedgerTests
{
    static CompoundFireStageRecord Pending(int slot, int sourceBar, int stagedChartBar, string sourceTf = "5") =>
        new()
        {
            Symbol = "EURUSD",
            RuleId = $"R{slot + 1}",
            SlotIndex = slot,
            RuleName = $"R{slot + 1}",
            Direction = SignalDirection.Buy,
            ChartTfToken = "15",
            SourceTfToken = sourceTf,
            SourceBarIndex = sourceBar,
            SourceEventOpenTimeUtc = new DateTime(2024, 6, 6, 17, 45, 0),
            StagedAtChartBarIndex = stagedChartBar,
            StagedAtMinTfBarIndex = sourceBar,
            StagedAtChartBarOpenTime = new DateTime(2024, 6, 6, 17, 30, 0),
            StagedAtMinTfBarOpenTime = new DateTime(2024, 6, 6, 17, 45, 0),
        };

    [Fact]
    public void UpsertPending_replaces_same_source_event_key()
    {
        var ledger = new CompoundFireStageLedger();
        ledger.UpsertPending(Pending(1, sourceBar: 613, stagedChartBar: 337));
        ledger.UpsertPending(Pending(1, sourceBar: 613, stagedChartBar: 337, sourceTf: "5"));

        Assert.Equal(1, ledger.Count);
        Assert.Equal(613, ledger.Snapshot()[0].StagedAtMinTfBarIndex);
        Assert.Equal(CompoundFireStageStatus.Pending, ledger.Snapshot()[0].Status);
    }

    [Fact]
    public void GetPendingForConfirm_includes_later_chart_bars_within_window()
    {
        var ledger = new CompoundFireStageLedger();
        ledger.UpsertPending(Pending(1, sourceBar: 613, stagedChartBar: 337));

        Assert.Single(ledger.GetPendingForConfirm(currentChartBarIndex: 337, eventValidBars: 3));
        Assert.Single(ledger.GetPendingForConfirm(currentChartBarIndex: 338, eventValidBars: 3));
        Assert.Single(ledger.GetPendingForConfirm(currentChartBarIndex: 340, eventValidBars: 3));
        Assert.Empty(ledger.GetPendingForConfirm(currentChartBarIndex: 341, eventValidBars: 3));
    }

    [Fact]
    public void MarkSourceEmitted_blocks_restage_via_source_key()
    {
        var ledger = new CompoundFireStageLedger();
        var record = Pending(1, sourceBar: 613, stagedChartBar: 337);
        record.SourceTfToken = "5";
        ledger.UpsertPending(record);

        var emitted = Pending(1, sourceBar: 613, stagedChartBar: 337, sourceTf: "5");
        emitted.EmittedAtChartBarIndex = 338;
        emitted.EmittedAtChartBarOpenTime = new DateTime(2024, 6, 6, 18, 0, 0);
        ledger.MarkSourceEmitted(emitted);

        var key = CompoundSourceEventKey.Build("EURUSD", "R2", SignalDirection.Buy, "5", 613);
        Assert.True(ledger.IsSourceEventEmitted(key));
        Assert.Empty(ledger.GetPendingForConfirm(339, 3));

        ledger.UpsertPending(Pending(1, sourceBar: 613, stagedChartBar: 339, sourceTf: "5"));
        Assert.Equal(1, ledger.Count);
        Assert.Equal(CompoundFireStageStatus.Emitted, ledger.Snapshot()[0].Status);
        Assert.Equal(338, ledger.Snapshot()[0].EmittedAtChartBarIndex);
    }

    [Fact]
    public void ExpireOutsideWindow_marks_stale_pending_expired()
    {
        var ledger = new CompoundFireStageLedger();
        ledger.UpsertPending(Pending(1, sourceBar: 613, stagedChartBar: 330));

        ledger.ExpireOutsideWindow(currentChartBarIndex: 337, eventValidBars: 3);

        Assert.Equal(CompoundFireStageStatus.Expired, ledger.Snapshot()[0].Status);
        Assert.Empty(ledger.GetPendingForConfirm(337, 3));
    }

    [Theory]
    [InlineData(337, 337, 3, true)]
    [InlineData(337, 340, 3, true)]
    [InlineData(337, 341, 3, false)]
    [InlineData(337, 336, 3, false)]
    public void IsWithinConfirmWindow(int staged, int current, int window, bool expected) =>
        Assert.Equal(expected, CompoundFireStageLedger.IsWithinConfirmWindow(staged, current, window));
}
