using System.Collections.Generic;
using TradeAlert.Core.Models.Alerts;
using Xunit;

namespace TradeAlert.Core.Tests.Alerts;

public sealed class CompoundRuleSourceResolverTests
{
    [Theory]
    [InlineData(0, "5")]
    [InlineData(1, "5")]
    [InlineData(2, "15")]
    [InlineData(3, "15")]
    [InlineData(4, "15")]
    [InlineData(5, "15")]
    public void Resolve_preset_rules_primary_source_tf(int presetIndex, string expectedTf)
    {
        CompoundRuleParser.TryParse(
            CompoundAlertPresets.RuleText(presetIndex),
            presetIndex,
            out var rule,
            out _,
            CompoundAlertPresets.DisplayName(presetIndex));

        var primary = CompoundRuleSourceResolver.Resolve(rule);
        Assert.Equal(expectedTf, primary.TfToken);
        Assert.False(primary.UsedChartTfFallback);
    }

    [Fact]
    public void Resolve_picks_lowest_tf_when_multiple_event_entries()
    {
        var rule = new CompoundAlertRule
        {
            SlotIndex = 1,
            RuleName = "R-test",
            Entries = new List<CompoundConditionEntry>
            {
                new("15", AlertConditionId.CondBuyEventHLM15),
                new("5", AlertConditionId.CondBuyEventM5),
            },
        };

        var primary = CompoundRuleSourceResolver.Resolve(rule);
        Assert.Equal("5", primary.TfToken);
        Assert.Equal(AlertConditionId.CondBuyEventM5, primary.PrimaryEventCondition);
    }

    [Fact]
    public void SourceEventKey_uses_source_bar_index()
    {
        var signal = new CompoundSignal
        {
            Symbol = "EURUSD",
            RuleId = "R2",
            Direction = SignalDirection.Buy,
            SourceTfToken = "5",
            SourceEventBarIndex = 10050,
        };

        Assert.Equal("EURUSD|R2|BUY|M5|10050", signal.SourceEventKey);
    }
}
