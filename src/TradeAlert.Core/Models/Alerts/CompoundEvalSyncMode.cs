namespace TradeAlert.Core.Models.Alerts;

/// <summary>
/// Compound AND evaluation timing — mirrors TradingView multi-condition alerts vs legacy Pine EVENT window.
/// </summary>
public enum CompoundEvalSyncMode
{
    /// <summary>
    /// All conditions must be true at the same eval point; cadence = smallest TF in rule;
    /// EVENT valid only on the bar it fired (no memory window).
    /// </summary>
    TradingView = 0,

    /// <summary>
    /// EVENT stays active for <c>EVENT valid window</c> bars after fire (legacy compound panel).
    /// </summary>
    PineEventWindow = 1,
}
