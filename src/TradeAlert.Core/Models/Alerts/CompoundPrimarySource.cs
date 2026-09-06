namespace TradeAlert.Core.Models.Alerts;

/// <summary>Primary event timeframe and condition for a compound rule.</summary>
public sealed class CompoundPrimarySource
{
    public string TfToken { get; init; } = "";
    public AlertConditionId PrimaryEventCondition { get; init; }
    public bool UsedChartTfFallback { get; init; }
}
