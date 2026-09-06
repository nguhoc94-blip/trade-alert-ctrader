using System;

namespace TradeAlert.Core.Models.Alerts;

public sealed class AlertEvent
{
    public AlertConditionId ConditionId { get; init; }
    public DateTime FireTimeChartLocal { get; init; }
    public int SourceBarIndex { get; init; }
    public DateTime SourceBarOpenTimeChartLocal { get; init; }
    public string SourceTimeframeToken { get; init; } = "";
    public string Symbol { get; init; } = "";
}
