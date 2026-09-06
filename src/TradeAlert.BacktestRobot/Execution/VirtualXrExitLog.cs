using System.Globalization;

namespace TradeAlert.BacktestRobot.Execution;

public static class VirtualXrExitLog
{
    public static string FormatExitPlan(
        string label,
        bool enabled,
        double triggerR,
        double entry,
        double finalSl,
        double riskDistance,
        double virtualXrPrice,
        VirtualXrFillMode fillMode,
        bool brokerTpEnabled) =>
        $"[L6BT] XR EXIT PLAN label={label} enabled={enabled} xR={Fmt2(triggerR)} " +
        $"actualEntry={Fmt(entry)} finalSL={Fmt(finalSl)} riskDistance={Fmt(riskDistance)} " +
        $"virtualXrPrice={Fmt(virtualXrPrice)} fillMode={fillMode} brokerTpEnabled={brokerTpEnabled}";

    public static string FormatTouchNoConfirm(
        string label,
        double triggerR,
        double virtualXrPrice,
        in VirtualXrBarOhlc bar) =>
        $"[L6BT] XR TOUCH NO CONFIRM label={label} xR={Fmt2(triggerR)} virtualXrPrice={Fmt(virtualXrPrice)} " +
        $"barTime={FmtTime(bar.OpenTime)} high={Fmt(bar.High)} low={Fmt(bar.Low)} close={Fmt(bar.Close)}";

    public static string FormatExitSignal(
        string label,
        double triggerR,
        double virtualXrPrice,
        in VirtualXrBarOhlc bar,
        VirtualXrFillMode fillMode,
        double effectiveClose) =>
        $"[L6BT] XR EXIT SIGNAL label={label} xR={Fmt2(triggerR)} virtualXrPrice={Fmt(virtualXrPrice)} " +
        $"barTime={FmtTime(bar.OpenTime)} high={Fmt(bar.High)} low={Fmt(bar.Low)} close={Fmt(bar.Close)} " +
        $"fillMode={fillMode} effectiveClose={Fmt(effectiveClose)}";

    static string Fmt(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
    static string Fmt2(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    static string FmtTime(System.DateTime t) => t.ToString("O", CultureInfo.InvariantCulture);
}
