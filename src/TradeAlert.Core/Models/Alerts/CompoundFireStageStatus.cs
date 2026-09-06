namespace TradeAlert.Core.Models.Alerts;

/// <summary>Lifecycle of a cross-TF compound stage ledger entry.</summary>
public enum CompoundFireStageStatus
{
    /// <summary>Min-TF leg satisfied; full rule not yet emitted.</summary>
    Pending,

    /// <summary>Full rule emitted — keyed by slot + event bar.</summary>
    Emitted,

    /// <summary>No longer eligible for confirm (outside event window).</summary>
    Expired,
}
