namespace TradeAlert.Core.Models.Alerts;

/// <summary>
/// Extension helpers for <see cref="AlertConditionId"/>.
/// C# enums cannot contain methods, so helpers live here.
/// </summary>
public static class AlertConditionIdExtensions
{
    /// <summary>
    /// Returns the trade direction encoded in <paramref name="id"/>.
    /// Derived from the enum value itself — not from any display-name string.
    /// </summary>
    public static SignalDirection GetDirection(this AlertConditionId id) => id switch
    {
        AlertConditionId.CondBuyEventM5
            or AlertConditionId.CondBuyEventHLM15
            or AlertConditionId.CondBuyEventNGM15
            or AlertConditionId.CondBuyEventHLNGM15
            or AlertConditionId.CanBuyTouchM5
            or AlertConditionId.CanBuyReal
            or AlertConditionId.CanBuyRealAndM15CloseNow => SignalDirection.Buy,

        AlertConditionId.CondSellEventM5
            or AlertConditionId.CondSellEventHLM15
            or AlertConditionId.CondSellEventNGM15
            or AlertConditionId.CondSellEventHLNGM15
            or AlertConditionId.CanSellTouchM5
            or AlertConditionId.CanSellReal
            or AlertConditionId.CanSellRealAndM15CloseNow => SignalDirection.Sell,

        _ => SignalDirection.Unknown,
    };
}
