using System;
using System.Collections.Generic;
using TradeAlert.Core.Models.Alerts;
using Xunit;

namespace TradeAlert.Core.Tests.Alerts;

public sealed class CompoundEventAnchorLedgerTests
{
    static CompoundEventAnchor PendingAnchor(
        string key,
        int sourceBar,
        int stagedChartBar,
        int expiresChartBar,
        params (string leg, CompoundLegLatchState state)[] legs)
    {
        var legStates = new Dictionary<string, CompoundLegLatchState>(StringComparer.Ordinal);
        if (legs.Length == 0)
            legStates["15:CondBuyStateM15"] = CompoundLegLatchState.Pending;
        else
        {
            foreach (var (leg, state) in legs)
                legStates[leg] = state;
        }

        return new CompoundEventAnchor
        {
            SourceEventKey = key,
            Symbol = "EURUSD",
            RuleId = "R2",
            SlotIndex = 1,
            Direction = SignalDirection.Buy,
            SourceTfToken = "5",
            SourceBarIndex = sourceBar,
            SourceBarOpenTimeUtc = new DateTime(2024, 6, 6, 17, 45, 0, DateTimeKind.Utc),
            SourceBarCloseTimeUtc = new DateTime(2024, 6, 6, 17, 50, 0, DateTimeKind.Utc),
            StagedAtChartBarIndex = stagedChartBar,
            ExpiresAtChartBarIndex = expiresChartBar,
            PrimaryEventCondition = AlertConditionId.CondBuyEventM5,
            Status = CompoundEventAnchorStatus.Pending,
            LegLatchStates = legStates,
        };
    }

    [Fact]
    public void UpsertPending_preserves_existing_latched_ok_legs()
    {
        var ledger = new CompoundEventAnchorLedger();
        var key = "EURUSD|R2|BUY|M5|100";
        ledger.UpsertPending(PendingAnchor(key, 100, 500, 503,
            ("15:CondBuyStateM15", CompoundLegLatchState.LatchedOk),
            ("60:CondBuyStateH1", CompoundLegLatchState.Pending)));

        var retry = PendingAnchor(key, 100, 500, 503,
            ("15:CondBuyStateM15", CompoundLegLatchState.Pending),
            ("60:CondBuyStateH1", CompoundLegLatchState.LatchedOk));
        ledger.UpsertPending(retry);

        Assert.True(ledger.TryGet(key, out var anchor));
        Assert.Equal(CompoundLegLatchState.LatchedOk, anchor!.LegLatchStates["15:CondBuyStateM15"]);
        Assert.Equal(CompoundLegLatchState.LatchedOk, anchor.LegLatchStates["60:CondBuyStateH1"]);
        Assert.Equal(1, ledger.Count);
    }

    [Fact]
    public void GetPendingForConfirm_respects_staged_and_expires_chart_bars()
    {
        var ledger = new CompoundEventAnchorLedger();
        var key = "EURUSD|R2|BUY|M5|100";
        ledger.UpsertPending(PendingAnchor(key, 100, stagedChartBar: 500, expiresChartBar: 503));

        Assert.Single(ledger.GetPendingForConfirm(500));
        Assert.Single(ledger.GetPendingForConfirm(503));
        Assert.Empty(ledger.GetPendingForConfirm(499));
        Assert.Empty(ledger.GetPendingForConfirm(504));
    }

    [Fact]
    public void ExpireOutsideWindow_marks_pending_legs_expired()
    {
        var ledger = new CompoundEventAnchorLedger();
        var key = "EURUSD|R2|BUY|M5|100";
        ledger.UpsertPending(PendingAnchor(key, 100, 500, 503,
            ("15:CondBuyStateM15", CompoundLegLatchState.LatchedOk),
            ("60:CondBuyStateH1", CompoundLegLatchState.Pending)));

        var expired = ledger.ExpireOutsideWindow(504);
        Assert.Single(expired);
        Assert.True(ledger.TryGet(key, out var anchor));
        Assert.Equal(CompoundEventAnchorStatus.Expired, anchor!.Status);
        Assert.Equal(CompoundLegLatchState.LatchedOk, anchor.LegLatchStates["15:CondBuyStateM15"]);
        Assert.Equal(CompoundLegLatchState.Expired, anchor.LegLatchStates["60:CondBuyStateH1"]);
    }

    [Fact]
    public void MarkEmitted_blocks_upsert_and_active()
    {
        var ledger = new CompoundEventAnchorLedger();
        var key = "EURUSD|R2|BUY|M5|100";
        ledger.UpsertPending(PendingAnchor(key, 100, 500, 503));
        ledger.MarkEmitted(key, emittedAtChartBarIndex: 501);

        Assert.True(ledger.IsEmitted(key));
        Assert.True(ledger.HasActive(key));

        ledger.UpsertPending(PendingAnchor(key, 100, 502, 505));
        Assert.True(ledger.TryGet(key, out var anchor));
        Assert.Equal(CompoundEventAnchorStatus.Emitted, anchor!.Status);
        Assert.Equal(501, anchor.EmittedAtChartBarIndex);
    }

    [Fact]
    public void ComputeExpiresAtSourceBar_adds_event_valid_bars()
    {
        Assert.Equal(103, CompoundEventAnchorLedger.ComputeExpiresAtSourceBar(100, eventValidBars: 3));
    }

    [Fact]
    public void UsesSourceBarConfirmWindow_true_for_R1_R2_M5_only()
    {
        Assert.True(CompoundEventAnchorLedger.UsesSourceBarConfirmWindow("5", slotIndex: 0));
        Assert.True(CompoundEventAnchorLedger.UsesSourceBarConfirmWindow("5", slotIndex: 1));
        Assert.False(CompoundEventAnchorLedger.UsesSourceBarConfirmWindow("5", slotIndex: 2));
        Assert.False(CompoundEventAnchorLedger.UsesSourceBarConfirmWindow("15", slotIndex: 0));
    }

    [Fact]
    public void GetPendingForConfirm_R1_R2_uses_M5_source_bar_window()
    {
        var ledger = new CompoundEventAnchorLedger();
        var key = "USDJPY|R2|BUY|M5|100";
        var anchor = PendingAnchor(key, 100, stagedChartBar: 500, expiresChartBar: -1);
        anchor.ExpiresAtSourceBarIndex = 103;
        ledger.UpsertPending(anchor);

        int SourceBar(CompoundEventAnchor _) => 102;
        Assert.Single(ledger.GetPendingForConfirm(500, SourceBar));
        Assert.Single(ledger.GetPendingForConfirm(500, _ => 103));
        Assert.Empty(ledger.GetPendingForConfirm(500, _ => 104));
        Assert.Empty(ledger.GetPendingForConfirm(499, _ => 102));
    }

    [Fact]
    public void ExpireOutsideWindow_R1_R2_expires_by_M5_source_bar()
    {
        var ledger = new CompoundEventAnchorLedger();
        var key = "USDJPY|R2|BUY|M5|100";
        var anchor = PendingAnchor(key, 100, stagedChartBar: 500, expiresChartBar: -1);
        anchor.ExpiresAtSourceBarIndex = 103;
        ledger.UpsertPending(anchor);

        Assert.Empty(ledger.ExpireOutsideWindow(500, _ => 103));
        var expired = ledger.ExpireOutsideWindow(500, _ => 104);
        Assert.Single(expired);
        Assert.Equal(CompoundEventAnchorStatus.Expired, expired[0].Status);
    }

    [Fact]
    public void ComputeExpiresAtChartBar_adds_event_valid_bars()
    {
        Assert.Equal(503, CompoundEventAnchorLedger.ComputeExpiresAtChartBar(500, eventValidBars: 3));
    }
}
