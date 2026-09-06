namespace TradeAlert.Core.Models.KeyLevel;

/// <summary>Ảnh chụp một pivot theo index trong <see cref="PivotStateStore"/>.</summary>
public readonly record struct PivotSnapshot(
    int Index,
    double Price,
    int BarIndex,
    int Type,
    KeyBoxRef? KeyBox,
    int TimeMs,
    int SubType,
    int Flag);
