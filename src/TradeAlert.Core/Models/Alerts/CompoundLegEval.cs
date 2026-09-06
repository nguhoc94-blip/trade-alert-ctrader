namespace TradeAlert.Core.Models.Alerts;

public sealed class CompoundLegEval
{
    public string TfToken { get; init; } = "";
    public string ConditionName { get; init; } = "";
    public bool IsActive { get; init; }
    public string Debug { get; init; } = "";
}
