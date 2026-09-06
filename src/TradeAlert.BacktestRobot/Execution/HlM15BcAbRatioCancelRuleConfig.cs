using System;
using System.Collections.Generic;
using System.Globalization;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>Configuration for HL M15 AB/BC ratio pending cancellation (R3/R4).</summary>
public sealed class HlM15BcAbRatioCancelRuleConfig
{
    public const int R3BuyHlM15Slot = 2;
    public const int R4SellHlM15Slot = 3;

    public HashSet<int> RuleSlotIndices { get; init; } = new();
    public double BcAbRatioThreshold { get; init; } = 2.6;
    public bool VerboseSkipLog { get; init; }

    public bool AppliesToSlot(int slotIndex) => RuleSlotIndices.Contains(slotIndex);

    /// <summary>Parse mask: <c>hlM15</c>, explicit R numbers, or <c>all</c>.</summary>
    public static HashSet<int> ParseRulesMask(string mask, Action<string>? warn = null)
    {
        var slots = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(mask))
            return slots;

        foreach (var raw in mask.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var token = raw.Trim();
            if (token.Length == 0)
                continue;

            switch (token.ToLowerInvariant())
            {
                case "hlm15":
                    slots.Add(R3BuyHlM15Slot);
                    slots.Add(R4SellHlM15Slot);
                    continue;
                case "all":
                    for (var i = 0; i < SlotAuditTracker.SlotCount; i++)
                        slots.Add(i);
                    continue;
            }

            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) && r >= 1 && r <= 6)
            {
                slots.Add(r - 1);
                continue;
            }

            warn?.Invoke($"[L6BT] HL M15 BC/AB cancel: unknown mask token '{token}' — ignored");
        }

        return slots;
    }
}
