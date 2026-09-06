namespace TradeAlert.Core.Models.Alerts;

/// <summary>Why a compound rule did not FIRE at a chart sync point (for chart debug labels).</summary>
public readonly struct CompoundFireMissRecord
{
    public int SlotIndex { get; init; }
    public string Direction { get; init; }
    /// <summary>SYNC-MISS or CONFIRM-MISS.</summary>
    public string Tag { get; init; }
    /// <summary>Inactive legs, e.g. <c>5:canBuyReal,15:M15EvA</c>.</summary>
    public string Blockers { get; init; }
    public int EventBarIndex { get; init; }
}
