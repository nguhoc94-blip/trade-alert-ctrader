namespace TradeAlert.Core.Models.KeyLevel;

/// <summary>Thay Pine <c>box[]</c>: spec + id vẽ ngoài Core (không handle UI cTrader).</summary>
public sealed class KeyBoxRef
{
    public KeyBoxSpec Spec { get; init; } = null!;
    public string? ExternalDrawingKey { get; init; }
}
