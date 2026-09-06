using System;
using TradeAlert.Core.Series;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>
/// Adjusts keylevel SL width multiplier by ATR ratio:
/// dynamic = Base * (1 + factor * (currentAtr/baselineAtr - 1)), clamped.
/// </summary>
public static class AtrSlWidthMultiplierCalculator
{
    public static double ComputeDynamicMultRaw(double baseMult, double atrRatio, double adjustFactor) =>
        baseMult * (1.0 + adjustFactor * (atrRatio - 1.0));

    public static double ClampMult(double raw, double min, double max) =>
        Math.Max(min, Math.Min(max, raw));

    public static AtrSlWidthAdjustResult Resolve(
        in AtrSlWidthAdjustConfig cfg,
        SeriesBuffer? series)
    {
        var baseMult = cfg.BaseSlWidthMult <= 0 ? 2.0 : cfg.BaseSlWidthMult;
        var tf = string.IsNullOrWhiteSpace(cfg.AtrAdjustTf) ? "15" : cfg.AtrAdjustTf.Trim();

        if (series is null)
            return Unavailable(cfg, tf, "ATR series unavailable");

        var evalBar = series.CurrentEvaluationBarIndex;
        if (cfg.AtrAdjustUseClosedBar)
            evalBar = Math.Max(0, evalBar - 1);

        if (!WilderAtrCalculator.TryComputeSeries(series, cfg.AtrAdjustPeriod, evalBar, out var atrSeries))
            return Unavailable(cfg, tf, "ATR series insufficient");

        if (!WilderAtrCalculator.TryGetAtrAtBar(atrSeries, evalBar, out var currentAtr))
            return Unavailable(cfg, tf, "current ATR unavailable");

        if (!WilderAtrCalculator.TrySmaOfAtr(atrSeries, evalBar, cfg.AtrBaselinePeriod, out var baselineAtr))
            return Unavailable(cfg, tf, "baseline ATR unavailable");

        var atrRatio = currentAtr / baselineAtr;
        var raw = ComputeDynamicMultRaw(baseMult, atrRatio, cfg.AtrAdjustmentFactor);
        var final = ClampMult(raw, cfg.MinDynamicSlWidthMult, cfg.MaxDynamicSlWidthMult);

        return new AtrSlWidthAdjustResult
        {
            Success = true,
            UsedFallback = false,
            DynamicSlWidthMult = final,
            DynamicSlWidthMultRaw = raw,
            CurrentAtr = currentAtr,
            BaselineAtr = baselineAtr,
            AtrRatio = atrRatio,
            AtrTf = tf,
        };
    }

    static AtrSlWidthAdjustResult Unavailable(in AtrSlWidthAdjustConfig cfg, string tf, string reason)
    {
        var baseMult = cfg.BaseSlWidthMult <= 0 ? 2.0 : cfg.BaseSlWidthMult;
        if (cfg.AtrAdjustFallbackToBase)
        {
            return new AtrSlWidthAdjustResult
            {
                Success = true,
                UsedFallback = true,
                DynamicSlWidthMult = baseMult,
                DynamicSlWidthMultRaw = baseMult,
                AtrRatio = 1.0,
                AtrTf = tf,
            };
        }

        return new AtrSlWidthAdjustResult
        {
            Success = false,
            SkipReason = "ATR adjustment unavailable",
            AtrTf = tf,
        };
    }
}
