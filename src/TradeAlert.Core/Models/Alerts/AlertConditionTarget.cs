namespace TradeAlert.Core.Models.Alerts;

/// <summary>Liên kết condition → nhóm evaluator (documentation + routing).</summary>
public sealed class AlertConditionTarget
{
    public AlertConditionId Id { get; init; }
    public string EvaluatorCategory { get; init; } = "";
}
