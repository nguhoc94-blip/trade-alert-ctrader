namespace TradeAlert.Core.Models.KeyLevel;

/// <summary>Immutable identity cho OB (Option B+ snapshot trong Pine chính).</summary>
public readonly record struct ObIdentitySnapshot(
    int PoolIndex,
    int ObPivotType,
    int ObPivotHighId,
    int ObPivotLowId,
    int ObNumber,
    int ObType,
    int ObBar);
