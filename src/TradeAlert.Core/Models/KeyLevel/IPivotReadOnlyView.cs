namespace TradeAlert.Core.Models.KeyLevel;

/// <summary>Facade đọc pivot cho MicroSwing / consumer — không mutate.</summary>
public interface IPivotReadOnlyView
{
    int Count { get; }
    int GetBarIndex(int i);
    double GetPrice(int i);
    int GetType(int i);
    bool TryGetKeyBoxRef(int i, out KeyBoxRef? keyBoxRef);
}
