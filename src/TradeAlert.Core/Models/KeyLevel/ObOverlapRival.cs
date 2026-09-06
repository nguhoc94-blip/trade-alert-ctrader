namespace TradeAlert.Core.Models.KeyLevel;

/// <summary>OB được giữ box trong nhóm overlap (bar cao nhất).</summary>
public readonly record struct ObOverlapRival(
    int Bar,
    int Source,
    int Owner,
    int Number,
    int PivotType,
    int PivotHighId,
    int PivotLowId)
{
    public static ObOverlapRival None => new(-1, 0, -1, 0, 0, 0, 0);

    public bool HasValue => Bar >= 0;
}
