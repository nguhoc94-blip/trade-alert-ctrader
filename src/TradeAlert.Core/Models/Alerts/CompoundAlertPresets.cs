namespace TradeAlert.Core.Models.Alerts;

/// <summary>Default MTF compound rules R1–R6 (TradingView preset parity).</summary>
public static class CompoundAlertPresets
{
    public const int Count = 6;

    /// <summary>R1 — SELL M5 (M5/M15 Real; H1/H4 Real).</summary>
    public const string R1SellM5 =
        "M5:condSellEventM5+M5:canSellReal+M15:canSellReal+H1:canSellReal+H4:canSellReal";

    /// <summary>R2 — BUY M5 (M5/M15 Real; H1/H4 Real).</summary>
    public const string R2BuyM5 =
        "M5:condBuyEventM5+M5:canBuyReal+M15:canBuyReal+H1:canBuyReal+H4:canBuyReal";

    /// <summary>R3 — BUY HL M15 (M5/M15 Real; H1/H4 Real).</summary>
    public const string R3BuyHlM15 =
        "M15:condBuyEventHLM15+M5:canBuyReal+M15:canBuyReal+H1:canBuyReal+H4:canBuyReal";

    /// <summary>R4 — SELL HL M15 (M5/M15 Real; H1/H4 Real).</summary>
    public const string R4SellHlM15 =
        "M15:condSellEventHLM15+M5:canSellReal+M15:canSellReal+H1:canSellReal+H4:canSellReal";

    /// <summary>R5 — BUY NG M15 (M5/M15/H1/H4 Real).</summary>
    public const string R5BuyNgM15 =
        "M15:condBuyEventNGM15+M5:canBuyReal+M15:canBuyReal+H1:canBuyReal+H4:canBuyReal";

    /// <summary>R6 — SELL NG M15 (M5/M15/H1/H4 Real).</summary>
    public const string R6SellNgM15 =
        "M15:condSellEventNGM15+M5:canSellReal+M15:canSellReal+H1:canSellReal+H4:canSellReal";

    public static readonly string[] RuleTexts =
    {
        R1SellM5,
        R2BuyM5,
        R3BuyHlM15,
        R4SellHlM15,
        R5BuyNgM15,
        R6SellNgM15,
    };

    public static readonly string[] DisplayNames =
    {
        "SELL M5",
        "BUY M5",
        "BUY HL M15",
        "SELL HL M15",
        "BUY NG M15",
        "SELL NG M15",
    };

    /// <summary>Migrate saved R5/R6 instances (HLNG→NG, TouchM5→Real on HTF legs).</summary>
    public static string NormalizeNgEventTrigger(string ruleText)
    {
        if (string.IsNullOrWhiteSpace(ruleText))
            return ruleText;

        return ruleText
            .Replace("condBuyEventHLNGM15", "condBuyEventNGM15", StringComparison.Ordinal)
            .Replace("condSellEventHLNGM15", "condSellEventNGM15", StringComparison.Ordinal)
            .Replace("canBuyTouchM5", "canBuyReal", StringComparison.Ordinal)
            .Replace("canSellTouchM5", "canSellReal", StringComparison.Ordinal);
    }

    public static string RuleText(int index) =>
        index >= 0 && index < Count ? RuleTexts[index] : "";

    public static string DisplayName(int index) =>
        index >= 0 && index < Count ? DisplayNames[index] : $"R{index + 1}";
}
