using System;
using System.Collections.Generic;
using System.Globalization;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>Configuration for post-B push swing vs opposite obstacle zone pending cancellation.</summary>
public sealed class PostBPushObstacleCancelRuleConfig
{
    public HashSet<int> RuleSlotIndices { get; init; } = new();
    public IReadOnlyList<string> TfTokens { get; init; } = Array.Empty<string>();
    public bool IncludeKeyLevels { get; init; } = true;
    public bool IncludeBrokenKeyLevels { get; init; } = true;
    public bool IncludeOrderBlocks { get; init; } = true;
    public double TolerancePips { get; init; }
    public double PipSize { get; init; }

    public bool AppliesToSlot(int slotIndex) => RuleSlotIndices.Contains(slotIndex);

    /// <summary>Parse mask tokens: <c>all</c>, named families, or explicit R numbers.</summary>
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
                case "all":
                    for (var i = 0; i < SlotAuditTracker.SlotCount; i++)
                        slots.Add(i);
                    continue;
                case "condm5":
                    slots.Add(0);
                    slots.Add(1);
                    continue;
                case "ngm15":
                    slots.Add(4);
                    slots.Add(5);
                    continue;
            }

            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) && r >= 1 && r <= 6)
            {
                slots.Add(r - 1);
                continue;
            }

            warn?.Invoke($"[L6BT] post-B-push cancel: unknown mask token '{token}' — ignored");
        }

        return slots;
    }

    public EntrySlZoneGateConfig ToZoneCollectConfig() => new()
    {
        IncludeKeyLevels = IncludeKeyLevels,
        IncludeOrderBlocks = IncludeOrderBlocks,
        IncludeBrokenKeyLevels = IncludeBrokenKeyLevels,
        PipSize = PipSize,
    };
}
