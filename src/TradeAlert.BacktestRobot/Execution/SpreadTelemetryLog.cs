using System.Globalization;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>Spread source used when sizing and adjusting plan levels.</summary>
public static class SpreadTelemetrySources
{
    public const string Symbol = "Symbol";
    /// <summary>Forex/non-metal: override value is cTrader pips.</summary>
    public const string OverridePips = "OverridePips";
    /// <summary>Gold/silver: override value is price distance (cTrader UI parity, e.g. 0.4).</summary>
    public const string OverrideGoldPrice = "OverrideGoldPrice";

    /// <summary>Legacy alias kept for planned snapshots stored before the gold-price split.</summary>
    public const string Override = OverridePips;
}

/// <summary>Log lines for spread at startup, plan build, and position fill.</summary>
public static class SpreadTelemetryLog
{
    public static string FormatStartup(
        double spreadPipsOverride,
        string spreadSource,
        double spreadPips,
        double spreadPrice,
        double bid,
        double ask,
        bool enableMinSlConstraint,
        bool minSlBacktestMode,
        double minSlSpreadMultiple,
        double minSlPipsForex)
    {
        var mode = enableMinSlConstraint
            ? (minSlBacktestMode ? "clamp" : "skip")
            : "off";
        return "[L6BT] Spread telemetry | " +
               $"override={spreadPipsOverride.ToString("0.##", CultureInfo.InvariantCulture)} " +
               $"source={spreadSource} spreadPips={spreadPips.ToString("0.##", CultureInfo.InvariantCulture)} " +
               $"spreadPrice={spreadPrice.ToString("0.#####", CultureInfo.InvariantCulture)} " +
               $"bid={bid.ToString("0.#####", CultureInfo.InvariantCulture)} ask={ask.ToString("0.#####", CultureInfo.InvariantCulture)} | " +
               $"minSlConstraint={mode} spread×{minSlSpreadMultiple.ToString("0.#", CultureInfo.InvariantCulture)} " +
               $"forexMinPips={minSlPipsForex.ToString("0.#", CultureInfo.InvariantCulture)}";
    }

    public static string FormatPlan(
        in TradePlan plan,
        int ruleSlot,
        double bid,
        double ask)
    {
        var floorNote = plan.SlFloorApplied
            ? $" floorApplied=true slBeforeFloor={Fmt1(plan.SlPipsBeforeFloor)} minSlFloor={Fmt1(plan.MinSlFloorPips)}"
            : plan.MinSlFloorPips > 0
                ? $" floorApplied=false slBeforeFloor={Fmt1(plan.SlPipsBeforeFloor)} minSlFloor={Fmt1(plan.MinSlFloorPips)}"
                : "";
        return "[L6BT] SPREAD PLAN " +
               $"rule=R{ruleSlot + 1} label={plan.Label} " +
               $"spreadPips={Fmt1(plan.SpreadPipsAtPlan)} source={plan.SpreadSourceAtPlan} " +
               $"bid={Fmt(bid)} ask={Fmt(ask)} " +
               $"slPips={Fmt1(plan.StopLossPips)}{floorNote}";
    }

    public static string FormatFill(
        string label,
        double planSpreadPips,
        string planSpreadSource,
        double fillSpreadPips,
        double bid,
        double ask,
        double entryPrice)
    {
        var delta = fillSpreadPips - planSpreadPips;
        return "[L6BT] SPREAD FILL " +
               $"label={label} planSpread={Fmt1(planSpreadPips)}({planSpreadSource}) " +
               $"fillSpread={Fmt1(fillSpreadPips)} delta={Fmt1(delta)} " +
               $"bid={Fmt(bid)} ask={Fmt(ask)} entry={Fmt(entryPrice)}";
    }

    static string Fmt(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
    static string Fmt1(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
