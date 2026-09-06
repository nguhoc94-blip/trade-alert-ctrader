namespace TradeAlert.Core.Models.Alerts;

/// <summary>15 id: 12 Pine arlert new + PhaKhungLon + bot-only HLNG M15 (R5/R6 gated HL).</summary>
public enum AlertConditionId
{
    CondBuyEventM5 = 0,
    CondSellEventM5,
    CondBuyEventHLM15,
    CondSellEventHLM15,
    CondBuyEventNGM15,
    CondSellEventNGM15,
    /// <summary>Bot R5: buyNG OR (buyHL when opposite RED non-broken KL exists on M15).</summary>
    CondBuyEventHLNGM15,
    /// <summary>Bot R6: sellNG OR (sellHL when opposite GREEN non-broken KL exists on M15).</summary>
    CondSellEventHLNGM15,
    CanBuyTouchM5,
    CanSellTouchM5,
    CanBuyReal,
    CanSellReal,
    CanBuyRealAndM15CloseNow,
    CanSellRealAndM15CloseNow,
    /// <summary>Pine <c>condPhaKhungLon</c> — mọi TF, mỗi bar confirmed; Event A (BROKEN) || B (MAIN BROKEN) || C (key stop).</summary>
    CondPhaKhungLon,
}
