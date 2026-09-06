using System.Collections.Generic;
using TradeAlert.Core.Models.Alerts;
using Xunit;

namespace TradeAlert.Core.Tests.Alerts;

public sealed class CompoundEvalSyncTests
{
    [Theory]
    [InlineData("5", 5)]
    [InlineData("15", 15)]
    [InlineData("60", 60)]
    [InlineData("240", 240)]
    [InlineData("1440", 1440)]
    public void TryGetTfMinutes_maps_known_tokens(string token, int expected)
    {
        Assert.True(CompoundEvalSync.TryGetTfMinutes(token, out var minutes));
        Assert.Equal(expected, minutes);
    }

    [Fact]
    public void TryGetMinTfFromRules_picks_smallest_tf()
    {
        var rules = new[]
        {
            new CompoundAlertRule
            {
                SlotIndex = 1,
                RuleName = "R1",
                Entries = new List<CompoundConditionEntry>
                {
                    new("60", AlertConditionId.CanBuyRealAndM15CloseNow),
                    new("5", AlertConditionId.CanBuyRealAndM15CloseNow),
                },
            },
        };

        Assert.True(CompoundEvalSync.TryGetMinTfFromRules(rules, out var minMinutes, out var minToken));
        Assert.Equal(5, minMinutes);
        Assert.Equal("5", minToken);
    }

    [Theory]
    [InlineData("5", 5, true, false, 10, 0, true)]
    [InlineData("5", 5, false, false, 10, 0, false)]
    [InlineData("5", 15, true, false, 30, 0, true)]
    [InlineData("5", 15, true, false, 10, 0, false)]
    [InlineData("15", 5, true, false, 10, 0, false)]
    [InlineData("15", 5, true, false, 10, 2, true)]
    public void IsEvalSyncPoint_respects_chart_vs_min_tf(
        string chartTf,
        int minTfMinutes,
        bool isBarClosed,
        bool isNew,
        int barOpenMinute,
        int minTfBarsClosedThisPass,
        bool expected)
    {
        var actual = CompoundEvalSync.IsEvalSyncPoint(
            chartTf,
            minTfMinutes,
            isBarClosed,
            isNew,
            barOpenMinute,
            minTfBarsClosedThisPass);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(CompoundEvalSyncMode.TradingView, 10, 10, 3, true)]
    [InlineData(CompoundEvalSyncMode.TradingView, 10, 9, 3, false)]
    [InlineData(CompoundEvalSyncMode.PineEventWindow, 10, 8, 3, true)]
    [InlineData(CompoundEvalSyncMode.PineEventWindow, 10, 6, 3, false)]
    public void IsEventActiveOnCurrentBar_respects_sync_mode(
        CompoundEvalSyncMode mode,
        int currentBarIndex,
        int lastFiredBarIndex,
        int eventValidBars,
        bool expected)
    {
        var actual = CompoundEvalSync.IsEventActiveOnCurrentBar(
            mode, lastFiredBarIndex, currentBarIndex, eventValidBars);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IsEvalSyncPointForSourceCandidate_valid_after_source_close()
    {
        var candidate = new CompoundSourceEventCandidate
        {
            SourceTfToken = "5",
            SourceBarIndex = 100,
        };

        Assert.True(CompoundEvalSync.IsEvalSyncPointForSourceCandidate(
            "15", candidate, candidateClosedThisPass: true, isBackfillCompleted: true));

        Assert.False(CompoundEvalSync.IsEvalSyncPointForSourceCandidate(
            "15", candidate, candidateClosedThisPass: false, isBackfillCompleted: true));
    }
}
