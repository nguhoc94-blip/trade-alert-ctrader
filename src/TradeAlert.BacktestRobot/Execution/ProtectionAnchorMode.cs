using System;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>How SL/TP protection is anchored relative to a limit order's fill.</summary>
public enum ProtectionAnchorMode
{
    /// <summary>
    /// Keep cTrader's native pip-based protection (relative to the actual fill price).
    /// Audit logs the expected fill-relative SL/TP and compares to the broker values.
    /// </summary>
    ActualFillRelative,

    /// <summary>
    /// After the position opens, modify it to the planned absolute SL/TP prices so the
    /// protection matches the pre-fill plan regardless of fill slippage.
    /// </summary>
    PlannedAbsolute,
}

public static class ProtectionAnchorModeParser
{
    public static ProtectionAnchorMode Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return ProtectionAnchorMode.ActualFillRelative;

        return raw.Trim().ToLowerInvariant() switch
        {
            "plannedabsolute" => ProtectionAnchorMode.PlannedAbsolute,
            "planned" => ProtectionAnchorMode.PlannedAbsolute,
            "absolute" => ProtectionAnchorMode.PlannedAbsolute,
            _ => ProtectionAnchorMode.ActualFillRelative,
        };
    }
}
