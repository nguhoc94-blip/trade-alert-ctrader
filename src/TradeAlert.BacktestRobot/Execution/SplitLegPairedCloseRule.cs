using System;
using System.Globalization;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>
/// SplitTpAtCAndD sibling actions when one leg (|C or |D) closes:
///   • <b>StopLoss</b> → cancel pending sibling; in backtest also force-close filled sibling.
///   • <b>TakeProfit</b> → same when both legs share the same TP price (D-near-C).
///   • Live (RealTime): filled siblings rely on broker SL/TP; pending cancel still applies.
/// </summary>
public static class SplitLegPairedCloseRule
{
    public const string CloseReason = "split-leg-paired-close";

    public static bool IsSplitLegLabel(string? label) =>
        TryGetSiblingLabel(label, out _);

    /// <summary>Maps |C ↔ |D on the same swing-B setup label.</summary>
    public static bool TryGetSiblingLabel(string? label, out string sibling)
    {
        sibling = "";
        if (string.IsNullOrEmpty(label))
            return false;

        if (label.EndsWith("|C", StringComparison.Ordinal))
        {
            sibling = label.Substring(0, label.Length - 2) + "|D";
            return true;
        }

        if (label.EndsWith("|D", StringComparison.Ordinal))
        {
            sibling = label.Substring(0, label.Length - 2) + "|C";
            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether closing with <paramref name="triggerReason"/> should act on the sibling
    /// (cancel pending and/or backtest force-close filled leg).
    /// </summary>
    public static bool ShouldPairedClose(
        string triggerReason,
        double closedLegTp,
        double siblingLegTp,
        double tickSize)
    {
        if (string.Equals(triggerReason, "StopLoss", StringComparison.Ordinal))
            return true;

        if (string.Equals(triggerReason, "TakeProfit", StringComparison.Ordinal))
            return closedLegTp > 0 && siblingLegTp > 0 && PricesEqual(closedLegTp, siblingLegTp, tickSize);

        return false;
    }

    static bool PricesEqual(double a, double b, double tickSize)
    {
        var tol = tickSize > 0 ? tickSize / 2.0 : 1e-9;
        return Math.Abs(a - b) <= tol;
    }

    public static string FormatCloseLog(string triggerLabel, string siblingLabel, string triggerReason) =>
        $"[L6BT] SPLIT-LEG PAIRED-CLOSE trigger={triggerLabel} reason={triggerReason} -> CLOSE {siblingLabel} ({CloseReason})";

    public static string FormatCancelLog(string triggerLabel, string siblingLabel, string triggerReason) =>
        $"[L6BT] SPLIT-LEG PAIRED-CLOSE trigger={triggerLabel} reason={triggerReason} -> CANCEL {siblingLabel} ({CloseReason})";

    public static string FormatSkipLog(
        string triggerLabel,
        string siblingLabel,
        string triggerReason,
        double closedLegTp,
        double siblingLegTp) =>
        $"[L6BT] SPLIT-LEG PAIRED-CLOSE SKIP trigger={triggerLabel} reason={triggerReason} " +
        $"closedTp={Fmt(closedLegTp)} siblingTp={Fmt(siblingLegTp)} (TP mismatch — sibling kept)";
    
    static string Fmt(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
}
