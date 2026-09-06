using TradeAlert.Core.Models.Alerts;

namespace TradeAlert.Indicator;

public sealed class ListAlertFireSink : IAlertFireSink
{
    public List<AlertEvent> Events { get; } = new();

    public void Enqueue(in AlertEvent alertEvent) => Events.Add(alertEvent);
}
