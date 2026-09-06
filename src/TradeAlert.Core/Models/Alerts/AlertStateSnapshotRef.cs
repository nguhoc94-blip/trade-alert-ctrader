namespace TradeAlert.Core.Models.Alerts;

/// <summary>Tham chiếu snapshot state cho log — không handle UI.</summary>
public readonly struct AlertStateSnapshotRef
{
    public int? PrimaryPivotIndex { get; init; }
    public int? ObPoolIndex { get; init; }
    public long StateEpoch { get; init; }
}
