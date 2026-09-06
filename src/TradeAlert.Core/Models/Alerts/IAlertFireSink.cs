namespace TradeAlert.Core.Models.Alerts;

public interface IAlertFireSink
{
    void Enqueue(in AlertEvent alertEvent);
}
