namespace TradeAlert.Core.Models.Alerts;

public sealed class AlertDefinition
{
    public AlertConditionId Id { get; init; }
    public string Title { get; init; } = "";
    public string? MessageTemplateRef { get; init; }
    public AlertTimingClass TimingClass { get; init; }
    public string PineConditionSymbol { get; init; } = "";
    public string CSharpTargetHint { get; init; } = "";
    public string DuplicateKeyFieldDescriptor { get; init; } = "";
    public string LogFieldDescriptor { get; init; } = "";
}
