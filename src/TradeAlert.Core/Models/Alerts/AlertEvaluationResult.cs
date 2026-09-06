namespace TradeAlert.Core.Models.Alerts;

public readonly struct AlertEvaluationResult
{
    public bool Fired { get; init; }
    public string ReasonCode { get; init; }
    public string ReasonText { get; init; }
    public bool StateDependencyMissing { get; init; }

    public static AlertEvaluationResult NotFired(string code, string text, bool dependencyMissing = true) =>
        new()
        {
            Fired = false,
            ReasonCode = code,
            ReasonText = text,
            StateDependencyMissing = dependencyMissing
        };
}
