using TradeAlert.Core.Models.Alerts;

namespace TradeAlert.Indicator;

/// <summary>Sink rỗng — khi chỉ mong đánh giá mà không in (hoặc sẽ bật Alerts sau).</summary>
public sealed class VoidAlertFireSink : IAlertFireSink
{
    public static readonly VoidAlertFireSink Instance = new();

    VoidAlertFireSink()
    {
    }

    public void Enqueue(in AlertEvent _)
    {
    }
}
