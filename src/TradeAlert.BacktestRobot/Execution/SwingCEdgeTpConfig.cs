using System;
using System.Collections.Generic;
using System.Globalization;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>How the TP is determined when swing-C edge TP is enabled.</summary>
public enum SwingCTpMode
{
    UpdateOnCConfirm = 0,
    NoTpUntilC = 1,
    NextKeyLevelAfterC = 2,
    SplitTpAtCAndD = 3,
}

public sealed class SwingCEdgeTpConfig
{
    public bool UseSwingCEdgeTakeProfit { get; init; } = true;
    public bool SwingCEdgeTpFallbackToRR { get; init; } = true;
    public bool SkipIfSwingCRrBelowBase { get; init; }
    public bool MoveEntryIfSwingCRrBelowBase { get; init; }
    public bool UpdateTpWhenSwingCConfirms { get; init; } = true;
    public SwingCTpMode TpMode { get; init; } = SwingCTpMode.UpdateOnCConfirm;

    /// <summary>
    /// SplitTpAtCAndD: when RR(TP D) − RR(TP C) ≤ this value (raw D edge vs final C TP), collapse D leg TP to C.
    /// Does not change the near-D pending-cancel zone geometry. 0 = disabled.
    /// </summary>
    public double SplitDLegMinIncrementalRr { get; init; } = 0.5;

    public bool WantsLiveTpUpdate =>
        UseSwingCEdgeTakeProfit && (UpdateTpWhenSwingCConfirms
            || TpMode == SwingCTpMode.NoTpUntilC
            || TpMode == SwingCTpMode.NextKeyLevelAfterC)
        && TpMode != SwingCTpMode.SplitTpAtCAndD;

    public HashSet<int> RuleSlotIndices { get; init; } = new();

    public bool AppliesToSlot(int slotIndex) =>
        UseSwingCEdgeTakeProfit && RuleSlotIndices.Contains(slotIndex);

    public static HashSet<int> ParseRulesMask(string mask, Action<string>? warn = null) =>
        PostBPushObstacleCancelRuleConfig.ParseRulesMask(mask, warn);

    public static SwingCTpMode ParseTpMode(string raw) =>
        raw.Trim() switch
        {
            var s when string.Equals(s, "NoTpUntilC", StringComparison.OrdinalIgnoreCase)
                => SwingCTpMode.NoTpUntilC,
            var s when string.Equals(s, "NoTpUntil2", StringComparison.OrdinalIgnoreCase)
                => SwingCTpMode.NoTpUntilC,
            var s when string.Equals(s, "NextKeyLevelAfterC", StringComparison.OrdinalIgnoreCase)
                => SwingCTpMode.NextKeyLevelAfterC,
            var s when string.Equals(s, "NextKeyLevelAfter2", StringComparison.OrdinalIgnoreCase)
                => SwingCTpMode.NextKeyLevelAfterC,
            var s when string.Equals(s, "SplitTpAtCAndD", StringComparison.OrdinalIgnoreCase)
                => SwingCTpMode.SplitTpAtCAndD,
            var s when string.Equals(s, "SplitTpAt2And3", StringComparison.OrdinalIgnoreCase)
                => SwingCTpMode.SplitTpAtCAndD,
            _ => SwingCTpMode.UpdateOnCConfirm,
        };
}
