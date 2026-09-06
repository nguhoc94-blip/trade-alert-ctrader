using System;
using System.Globalization;

namespace TradeAlert.BacktestRobot.Execution;

public enum BBrokenOpenPositionActionMode
{
    CloseImmediately,
    MoveTpToEntry,
}

public enum BBrokenTpRejectFallbackMode
{
    CloseImmediately,
    LogOnly,
}

public sealed class BBrokenHandlerConfig
{
    public bool EnableMoveTpToEntryOnBBroken { get; init; } = true;
    public BBrokenOpenPositionActionMode OpenPositionMode { get; init; } = BBrokenOpenPositionActionMode.MoveTpToEntry;
    public BBrokenTpRejectFallbackMode MoveTpRejectMode { get; init; } = BBrokenTpRejectFallbackMode.CloseImmediately;
    public double TickSize { get; init; }
}

public sealed class BBrokenPendingResult
{
    public bool ShouldCancelPending { get; init; }
    public string Label { get; init; } = "";
}

public sealed class BBrokenOpenPositionAction
{
    public bool ShouldCloseImmediately { get; init; }
    public bool ShouldMoveTpToEntry { get; init; }
    public double EntryPrice { get; init; }
    public double OldTakeProfit { get; init; }
    public double NewTakeProfit { get; init; }
    public string Label { get; init; } = "";
    public bool AlreadyAtEntry { get; init; }
}

/// <summary>1-Broken exit routing: cancel pending vs move open-position TP to entry.</summary>
public static class BBrokenPositionHandler
{
    public static BBrokenPendingResult EvaluatePending(string label, bool hasPendingOrder) =>
        new()
        {
            Label = label,
            ShouldCancelPending = hasPendingOrder,
        };

    public static BBrokenOpenPositionAction? EvaluateOpenPosition(
        TrackedSetup setup,
        bool hasOpenPosition,
        double? positionTakeProfit,
        in BBrokenHandlerConfig cfg)
    {
        if (!hasOpenPosition)
            return null;

        var label = setup.Context.Label;
        var entry = setup.Context.EntryPrice;
        var oldTp = setup.CurrentTakeProfit > 0
            ? setup.CurrentTakeProfit
            : setup.Context.OriginalTakeProfit;

        if (positionTakeProfit is > 0)
            oldTp = positionTakeProfit.Value;

        var useMoveTp = cfg.EnableMoveTpToEntryOnBBroken
                        && cfg.OpenPositionMode == BBrokenOpenPositionActionMode.MoveTpToEntry;

        if (!useMoveTp)
        {
            return new BBrokenOpenPositionAction
            {
                Label = label,
                ShouldCloseImmediately = true,
                EntryPrice = entry,
                OldTakeProfit = oldTp,
                NewTakeProfit = entry,
            };
        }

        if (setup.TpMovedToEntryOnBBroken || IsTpAtEntry(oldTp, entry, cfg.TickSize))
        {
            return new BBrokenOpenPositionAction
            {
                Label = label,
                AlreadyAtEntry = true,
                EntryPrice = entry,
                OldTakeProfit = oldTp,
                NewTakeProfit = entry,
            };
        }

        return new BBrokenOpenPositionAction
        {
            Label = label,
            ShouldMoveTpToEntry = true,
            EntryPrice = entry,
            OldTakeProfit = oldTp,
            NewTakeProfit = entry,
        };
    }

    public static bool IsTpAtEntry(double tp, double entry, double tickSize)
    {
        var tol = tickSize > 0 ? tickSize / 2.0 : 1e-9;
        return Math.Abs(tp - entry) <= tol;
    }

    public static string FormatPendingCancelLog(string label) =>
        $"[L6BT] CANCEL {label} (1-Broken pending)";

    public static string FormatMoveTpLog(in BBrokenOpenPositionAction action, bool success) =>
        $"[L6BT] 1-Broken MOVE-TP-ENTRY label={action.Label} " +
        $"entry={Fmt(action.EntryPrice)} oldTP={Fmt(action.OldTakeProfit)} newTP={Fmt(action.NewTakeProfit)} " +
        $"result={(success ? "ok" : "rejected")}";

    public static string FormatMoveTpRejectLog(
        in BBrokenOpenPositionAction action,
        string reason,
        BBrokenTpRejectFallbackMode fallback) =>
        $"[L6BT] 1-Broken MOVE-TP-ENTRY REJECT label={action.Label} " +
        $"reason={reason} fallback={fallback}";

    static string Fmt(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
}
