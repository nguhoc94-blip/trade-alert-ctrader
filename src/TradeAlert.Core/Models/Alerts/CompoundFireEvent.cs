namespace TradeAlert.Core.Models.Alerts;

/// <summary>
/// Immutable DTO emitted by <c>CompoundFireOrchestrator</c> when a compound R1-R6 rule fires.
/// No cAlgo dependencies — usable by both the indicator host and the scanner cBot.
/// </summary>
public readonly record struct CompoundFireEvent(
    /// <summary>0-based slot index. R1 = SlotIndex 0, R6 = SlotIndex 5.</summary>
    int SlotIndex,
    /// <summary>Display name from <see cref="CompoundAlertPresets"/> or custom override.</summary>
    string RuleName,
    /// <summary>
    /// Trade direction derived from the first EVENT condition's <see cref="AlertConditionId"/>
    /// via <see cref="AlertConditionIdExtensions.GetDirection"/>. Never inferred from RuleName string.
    /// </summary>
    SignalDirection Direction,
    /// <summary>Chart timeframe token, e.g. "15" for M15, "60" for H1.</summary>
    string ChartTfToken,
    /// <summary>Chart bar index at which the rule fired (compound confirm bar).</summary>
    int ChartBarIndex,
    /// <summary>Bar open time (chart-local, normalized).</summary>
    System.DateTime BarOpenTime,
    /// <summary>Close price at <see cref="ChartBarIndex"/>.</summary>
    double Price,
    /// <summary>
    /// Bar index of the underlying HTF event (swing break) that triggered the first EVENT
    /// condition. Used as part of the email dedup key to distinguish different events that
    /// happen to share the same bar open time.
    /// </summary>
    int EventBarIndex,
    /// <summary>Timeframe token of the primary source event bar (e.g. "5" for R1/R2, "15" for R3-R6).</summary>
    string SourceTfToken = "5",
    /// <summary>
    /// Compound source event key (symbol|rule|dir|sourceTf|sourceBar). Non-empty when emitted by
    /// <see cref="CompoundWindowMode.AnchorLatchWindow"/> or <see cref="CompoundWindowMode.SetupAtCondFire"/>.
    /// Used by the bot to look up frozen trade geometry (pinned B, D-fire) in SetupAtCondFire mode.
    /// </summary>
    string SourceEventKey = "",
    /// <summary>
    /// Chart bar index where trade geometry (B/D) was frozen — equals the chart bar that contained
    /// the cond-fire source bar. In <see cref="CompoundWindowMode.SetupAtCondFire"/> this may differ
    /// from <see cref="ChartBarIndex"/> when state legs confirm on a later bar.
    /// -1 means "same as <see cref="ChartBarIndex"/>".
    /// </summary>
    int SetupBarIndex = -1
);
