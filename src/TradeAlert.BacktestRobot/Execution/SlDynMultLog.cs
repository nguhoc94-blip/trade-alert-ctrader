using System.Globalization;

namespace TradeAlert.BacktestRobot.Execution;

public static class SlDynMultLog
{
    public static string FormatPlanLog(
        in TradePlan plan,
        int ruleSlot,
        double keyWidth,
        in AtrSlWidthAdjustResult slAdjust,
        double baseMult,
        double adjustFactor,
        double riskDistance)
    {
        var dir = plan.IsBuy ? "BUY" : "SELL";
        return $"[L6BT] SL DYN-MULT rule=R{ruleSlot + 1} dir={dir} " +
               $"keyWidth={Fmt(keyWidth)} baseMult={Fmt(baseMult)} " +
               $"atrTf={slAdjust.AtrTf} atr={Fmt(slAdjust.CurrentAtr)} " +
               $"atrBaseline={Fmt(slAdjust.BaselineAtr)} atrRatio={Fmt(slAdjust.AtrRatio)} " +
               $"adjustFactor={Fmt(adjustFactor)} dynMultRaw={Fmt(slAdjust.DynamicSlWidthMultRaw)} " +
               $"dynMultFinal={Fmt(slAdjust.DynamicSlWidthMult)} riskDistance={Fmt(riskDistance)} " +
               $"entry={Fmt(plan.EntryLimit)} finalSL={Fmt(plan.StopLoss)}";
    }

    static string Fmt(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
}
