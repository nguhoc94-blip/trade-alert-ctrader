using System;

namespace TradeAlert.BacktestRobot.Execution;

public readonly struct VirtualXrBarSignal
{
    public bool Touched { get; init; }
    public bool Confirmed { get; init; }
}

public readonly struct VirtualXrBarOhlc
{
    public DateTime OpenTime { get; init; }
    public double Open { get; init; }
    public double High { get; init; }
    public double Low { get; init; }
    public double Close { get; init; }
}

/// <summary>Computes virtual xR trigger prices and evaluates bar-close confirmation.</summary>
public static class VirtualXrBarCloseEvaluator
{
    public static double ComputeVirtualXrPrice(double entry, double stopLoss, double triggerR, bool isBuy)
    {
        var riskDistance = Math.Abs(entry - stopLoss);
        return isBuy
            ? entry + triggerR * riskDistance
            : entry - triggerR * riskDistance;
    }

    public static VirtualXrBarSignal EvaluateBar(in VirtualXrBarOhlc bar, double virtualXrPrice, bool isBuy)
    {
        if (virtualXrPrice <= 0)
            return default;

        if (isBuy)
        {
            var touched = bar.High >= virtualXrPrice;
            var confirmed = bar.Close >= virtualXrPrice;
            return new VirtualXrBarSignal { Touched = touched, Confirmed = touched && confirmed };
        }

        var sellTouched = bar.Low <= virtualXrPrice;
        var sellConfirmed = bar.Close <= virtualXrPrice;
        return new VirtualXrBarSignal { Touched = sellTouched, Confirmed = sellTouched && sellConfirmed };
    }

    public static double EffectiveCloseForFillMode(
        VirtualXrFillMode fillMode,
        in VirtualXrBarOhlc signalBar,
        double nextBarOpen,
        double actualMarketClose)
    {
        return fillMode switch
        {
            VirtualXrFillMode.NextBarOpen => nextBarOpen > 0 ? nextBarOpen : actualMarketClose,
            VirtualXrFillMode.ActualMarketClose => actualMarketClose,
            _ => signalBar.Close,
        };
    }
}
