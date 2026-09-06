namespace TradeAlert.Core.Models.Alerts;

/// <summary>
/// Ledger row keyed by source event identity:
/// symbol|rule|dir|sourceTf|sourceBar.
/// </summary>
public sealed class CompoundFireStageRecord
{
    public string Symbol { get; set; } = "";
    public string RuleId { get; set; } = "";
    public int SlotIndex { get; set; }
    public string RuleName { get; set; } = "";
    public SignalDirection Direction { get; set; }
    public string ChartTfToken { get; set; } = "";

    public string SourceTfToken { get; set; } = "";
    public int SourceBarIndex { get; set; }
    public System.DateTime SourceEventOpenTimeUtc { get; set; }

    /// <summary>Legacy alias — same as <see cref="SourceBarIndex"/>.</summary>
    public int EventBarIndex => SourceBarIndex;

    public string SourceEventKey =>
        CompoundSourceEventKey.Build(Symbol, RuleId, Direction, SourceTfToken, SourceBarIndex);

    public int StagedAtChartBarIndex { get; set; }
    public int StagedAtMinTfBarIndex { get; set; }
    public System.DateTime StagedAtChartBarOpenTime { get; set; }
    public System.DateTime StagedAtMinTfBarOpenTime { get; set; }

    public CompoundFireStageStatus Status { get; set; }

    public int EmittedAtChartBarIndex { get; set; } = -1;
    public System.DateTime EmittedAtChartBarOpenTime { get; set; }
}
