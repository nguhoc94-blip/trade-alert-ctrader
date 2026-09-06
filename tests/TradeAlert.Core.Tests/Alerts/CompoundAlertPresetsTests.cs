using TradeAlert.Core.Models.Alerts;
using Xunit;

namespace TradeAlert.Core.Tests.Alerts;

public sealed class CompoundAlertPresetsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Preset_rules_parse_with_five_conditions(int index)
    {
        var text = CompoundAlertPresets.RuleText(index);
        Assert.False(string.IsNullOrWhiteSpace(text));

        var ok = CompoundRuleParser.TryParse(
            text, index, out var rule, out var err,
            CompoundAlertPresets.DisplayName(index));

        Assert.True(ok, err);
        Assert.Equal(5, rule.Entries.Count);
        Assert.Equal(index + 1, rule.SlotIndex);
        Assert.Equal(CompoundAlertPresets.DisplayName(index), rule.RuleName);
    }

    [Fact]
    public void R1_R2_use_Real_on_H1_and_H4()
    {
        Assert.Contains("H1:canSellReal", CompoundAlertPresets.R1SellM5);
        Assert.Contains("H4:canSellReal", CompoundAlertPresets.R1SellM5);
        Assert.Contains("H1:canBuyReal", CompoundAlertPresets.R2BuyM5);
        Assert.Contains("H4:canBuyReal", CompoundAlertPresets.R2BuyM5);
        Assert.DoesNotContain("canBuyTouchM5", CompoundAlertPresets.R1SellM5);
        Assert.DoesNotContain("canSellTouchM5", CompoundAlertPresets.R1SellM5);
        Assert.DoesNotContain("canBuyTouchM5", CompoundAlertPresets.R2BuyM5);
        Assert.DoesNotContain("canSellTouchM5", CompoundAlertPresets.R2BuyM5);
    }

    [Fact]
    public void R3_R4_use_Real_on_H1_and_H4()
    {
        Assert.Contains("H1:canBuyReal", CompoundAlertPresets.R3BuyHlM15);
        Assert.Contains("H4:canBuyReal", CompoundAlertPresets.R3BuyHlM15);
        Assert.Contains("H1:canSellReal", CompoundAlertPresets.R4SellHlM15);
        Assert.Contains("H4:canSellReal", CompoundAlertPresets.R4SellHlM15);
        Assert.DoesNotContain("canBuyTouchM5", CompoundAlertPresets.R3BuyHlM15);
        Assert.DoesNotContain("canSellTouchM5", CompoundAlertPresets.R4SellHlM15);
    }

    [Fact]
    public void R5_R6_use_NG_event_only()
    {
        Assert.Contains("condBuyEventNGM15", CompoundAlertPresets.R5BuyNgM15);
        Assert.Contains("condSellEventNGM15", CompoundAlertPresets.R6SellNgM15);
        Assert.DoesNotContain("HLNG", CompoundAlertPresets.R5BuyNgM15);
        Assert.DoesNotContain("HLNG", CompoundAlertPresets.R6SellNgM15);
    }

    [Fact]
    public void R5_R6_use_Real_on_all_TFs()
    {
        Assert.Contains("M5:canBuyReal", CompoundAlertPresets.R5BuyNgM15);
        Assert.Contains("M15:canBuyReal", CompoundAlertPresets.R5BuyNgM15);
        Assert.Contains("H1:canBuyReal", CompoundAlertPresets.R5BuyNgM15);
        Assert.Contains("H4:canBuyReal", CompoundAlertPresets.R5BuyNgM15);
        Assert.Contains("M5:canSellReal", CompoundAlertPresets.R6SellNgM15);
        Assert.Contains("M15:canSellReal", CompoundAlertPresets.R6SellNgM15);
        Assert.Contains("H1:canSellReal", CompoundAlertPresets.R6SellNgM15);
        Assert.Contains("H4:canSellReal", CompoundAlertPresets.R6SellNgM15);
        Assert.DoesNotContain("TouchM5", CompoundAlertPresets.R5BuyNgM15);
        Assert.DoesNotContain("TouchM5", CompoundAlertPresets.R6SellNgM15);
    }

    [Fact]
    public void NormalizeNgEventTrigger_migrates_HLNG_to_NG()
    {
        var migrated = CompoundAlertPresets.NormalizeNgEventTrigger(
            "M15:condBuyEventHLNGM15+M5:canBuyReal");
        Assert.Contains("condBuyEventNGM15", migrated);
        Assert.DoesNotContain("HLNG", migrated);
    }

    [Fact]
    public void NormalizeNgEventTrigger_migrates_TouchM5_to_Real()
    {
        var migrated = CompoundAlertPresets.NormalizeNgEventTrigger(
            "M15:condBuyEventNGM15+M5:canBuyReal+H4:canBuyTouchM5");
        Assert.Contains("H4:canBuyReal", migrated);
        Assert.DoesNotContain("TouchM5", migrated);
    }

    [Fact]
    public void Preset_touch_on_htf_has_no_compat_warning()
    {
        var entry = new CompoundConditionEntry("60", AlertConditionId.CanSellTouchM5);
        Assert.Null(CompoundRuleParser.ValidateTfConditionCompat(entry));
    }
}
