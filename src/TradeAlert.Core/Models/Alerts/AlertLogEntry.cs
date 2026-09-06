using System;

namespace TradeAlert.Core.Models.Alerts;

public sealed class AlertLogEntry
{
    public DateTime FireTimestampChartLocal { get; init; }
    public string Symbol { get; init; } = "";
    public string Timeframe { get; init; } = "";
    public string AlertName { get; init; } = "";
    public string AlertType { get; init; } = "";
    public string ConditionId { get; init; } = "";
    public DateTime SourceBarTimeChartLocal { get; init; }
    public long SourceBarIndex { get; init; }
    public bool IsCurrentBar { get; init; }
    public bool IsBarClosed { get; init; }
    public bool IsRealtime { get; init; }
    public bool IsBarCloseTiming { get; init; }
    public int? EvaluationOffset { get; init; }
    public string ReasonCode { get; init; } = "";
    public string ReasonText { get; init; } = "";
    public AlertStateSnapshotRef? StateSnapshotRef { get; init; }
    public string DuplicateKeyCanonical { get; init; } = "";
    public string PineConditionSymbol { get; init; } = "";
    public string CSharpTarget { get; init; } = "";
}
