using System;
using System.Collections.Generic;
using System.Globalization;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>Effective zone colour for gate filtering.</summary>
public enum ZoneEffectiveColor
{
    Green,
    Red,
}

public enum ZoneSourceKind
{
    KeyLevel,
    OrderBlock,
    BrokenKeyLevel,
}

public enum BrokenSwingType
{
    SwingLow,
    SwingHigh,
}

/// <summary>Configuration for Entry-SL zone confluence gate.</summary>
public sealed class EntrySlZoneGateConfig
{
    public HashSet<int> GatedSlotIndices { get; init; } = new();
    public IReadOnlyList<string> TfTokens { get; init; } = Array.Empty<string>();
    public bool IncludeKeyLevels { get; init; } = true;
    public bool IncludeOrderBlocks { get; init; } = true;
    public bool IncludeBrokenKeyLevels { get; init; } = true;
    public double OverlapTolerancePips { get; init; }
    public bool RequireSameColor { get; init; } = true;
    public bool ExcludeSetupSwingBKeyLevel { get; init; } = true;
    /// <summary>When true, M5 confluence counts Order Blocks only (not KeyLevel / BrokenKeyLevel).</summary>
    public bool M5ConfluenceObOnly { get; init; } = true;
    public double PipSize { get; init; }

    public bool AppliesToSlot(int slotIndex) => GatedSlotIndices.Contains(slotIndex);

    /// <summary>
    /// Parse mask tokens: named families <c>condM5</c>, <c>ngM15</c> or explicit R numbers.
    /// </summary>
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
                case "condm5":
                    slots.Add(0); // R1 SELL M5
                    slots.Add(1); // R2 BUY M5
                    continue;
                case "ngm15":
                    slots.Add(4); // R5 BUY NG M15
                    slots.Add(5); // R6 SELL NG M15
                    continue;
            }

            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) && r >= 1 && r <= 6)
            {
                slots.Add(r - 1);
                continue;
            }

            warn?.Invoke($"[L6BT] ZONE gate: unknown mask token '{token}' — ignored");
        }

        return slots;
    }

    public static IReadOnlyList<string> ParseTfTokens(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();

        var list = new List<string>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Length > 0)
                list.Add(part);
        }

        return list;
    }
}
