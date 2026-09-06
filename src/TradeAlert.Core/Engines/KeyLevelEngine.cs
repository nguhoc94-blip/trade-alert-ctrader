using System;
using TradeAlert.Core.Drawing;
using TradeAlert.Core.Models;
using TradeAlert.Core.Models.KeyLevel;

namespace TradeAlert.Core.Engines;

/// <summary>Pure helpers từ <c>lib_keylevel_box.pine</c> — không <c>ta.atr</c> nội bộ khi ATR rule bật.</summary>
public static class KeyLevelEngine
{
    /// <summary>Pine <c>_effKeyLookback = isGapMerge ? 0 : keylevelLookback</c> (f_push CREATE KEYLEVEL).</summary>
    public static int EffectiveKeylevelLookback(bool isGapMerge, bool useGapMerge, int keylevelLookback) =>
        isGapMerge && useGapMerge ? 0 : keylevelLookback;

    public static bool HasClearBodyRaw(
        int lag,
        double avgB,
        double minBodyMult,
        bool useAtrRule,
        int atrLen,
        double atrMult,
        Func<int, (double open, double high, double low, double close)> ohlcAtLag,
        double? atrValueWhenRuleEnabled)
    {
        if (useAtrRule && atrValueWhenRuleEnabled is null)
        {
            throw new ArgumentException(
                "Loop3: useAtrRule=true requires explicit injected ATR (no silent ta.atr in Core).",
                nameof(atrValueWhenRuleEnabled));
        }

        _ = atrLen;
        var (o, _, _, c) = ohlcAtLag(lag);
        var body = Math.Abs(c - o);
        if (!useAtrRule)
            return body >= avgB * minBodyMult;

        var atrv = atrValueWhenRuleEnabled!.Value;
        return body >= avgB * minBodyMult || body >= atrv * atrMult;
    }

    public static ReferenceCandlePick FindReferenceCandleFromPivot(
        int barIndex,
        int pivotBar,
        string pivotKind,
        int lookback,
        double avgB,
        double minBodyMult,
        bool useAtrRule,
        int atrLen,
        double atrMult,
        Func<int, (double open, double high, double low, double close)> ohlcAtLag,
        double? atrValueWhenRuleEnabled)
    {
        var pivotLag = barIndex - pivotBar;
        int? bestLag = null;
        double? bestVal = null;

        for (var lag = pivotLag; lag <= pivotLag + lookback; lag++)
        {
            if (lag < 0 || lag > barIndex)
                continue;

            var (o, h, l, c) = ohlcAtLag(lag);
            var colorOk = pivotKind == "H" ? c > o : c < o;
            if (!colorOk ||
                !HasClearBodyRaw(lag, avgB, minBodyMult, useAtrRule, atrLen, atrMult, ohlcAtLag,
                    atrValueWhenRuleEnabled))
                continue;

            var v = pivotKind == "H" ? Math.Max(o, c) : Math.Min(o, c);
            if (bestVal is null || (pivotKind == "H" ? v > bestVal : v < bestVal))
            {
                bestVal = v;
                bestLag = lag;
            }
        }

        var bl = bestLag ?? pivotLag;
        var (ob, _, _, oc) = ohlcAtLag(bl);
        return new ReferenceCandlePick(
            bl,
            Math.Max(ob, oc),
            Math.Min(ob, oc),
            Math.Abs(oc - ob));
    }

    /// <summary>
    /// Geometry giống Pine <c>build_keylevel_box</c>; <paramref name="atrValue"/> thay <c>ta.atr(atr_len)</c>.
    /// Trả <c>null</c> nếu <c>pivot_lag &lt; 0</c> hoặc ngoài <paramref name="searchRangeBars"/>.
    /// </summary>
    public static KeyBoxSpec? BuildKeylevelBox(
        int barIndex,
        int pivotBar,
        string pivotKind,
        double pivotPrice,
        int refLag,
        double refBodyLen,
        int searchRangeBars,
        int atrLen,
        double atrValue,
        uint highColorArgb,
        uint lowColorArgb,
        int opacity,
        double minTick,
        Func<int, (double open, double high, double low, double close)> ohlcAtLag,
        Func<int, DateTime> chartLocalOpenTimeAtLag,
        IDrawingCommandSink? drawingSink = null)
    {
        _ = atrLen;
        var pivotLag = barIndex - pivotBar;
        if (pivotLag < 0)
            return null;

        var (_, ph, pl, _) = ohlcAtLag(pivotLag);
        var rawWick = pivotKind == "H" ? ph : pl;
        var wasNeutralized = Math.Abs(pivotPrice - rawWick) > minTick;
        var extreme = pivotPrice;

        if (wasNeutralized && pivotLag > 1)
        {
            var maxScan = Math.Min(pivotLag - 1, 3);
            for (var i = 1; i <= maxScan; i++)
            {
                var scanLag = pivotLag - i;
                if (scanLag < 1)
                    continue;

                if (pivotKind == "H")
                {
                    var scanHigh = ohlcAtLag(scanLag).high;
                    if (scanHigh > extreme)
                        extreme = scanHigh;
                }
                else
                {
                    var scanLow = ohlcAtLag(scanLag).low;
                    if (scanLow < extreme)
                        extreme = scanLow;
                }
            }
        }

        var refClose = ohlcAtLag(refLag).close;
        var boxTop = pivotKind == "H" ? extreme : refClose;
        var boxBot = pivotKind == "H" ? refClose : extreme;

        var atrv = atrValue;
        var curThick = Math.Abs(boxTop - boxBot);
        var minThick = atrv * 0.15;

        if (curThick < minThick)
        {
            var bodyRatio = atrv > 0 ? refBodyLen / atrv : 0.0;
            var isImpulseBody = bodyRatio > 1.2;
            var desiredThick = refBodyLen > 0
                ? (isImpulseBody ? refBodyLen / 3 : refBodyLen / 2)
                : atrv * 0.1;
            if (pivotKind == "H")
                boxBot = boxTop - desiredThick;
            else
                boxTop = boxBot + desiredThick;
        }

        if (barIndex - pivotBar > searchRangeBars)
            return null;

        var leftTime = chartLocalOpenTimeAtLag(pivotLag);
        var rightTime = chartLocalOpenTimeAtLag(pivotLag);
        ChartTimePolicy.EnsureChartLocalUnspecified(leftTime);
        ChartTimePolicy.EnsureChartLocalUnspecified(rightTime);

        var spec = new KeyBoxSpec
        {
            LeftTimeChartLocal = leftTime,
            RightTimeChartLocal = rightTime,
            Top = boxTop,
            Bottom = boxBot,
            HighColorArgb = highColorArgb,
            LowColorArgb = lowColorArgb,
            Opacity = opacity,
            ExtendRight = true
        };

        drawingSink?.Enqueue(new DrawingCommand
        {
            Kind           = DrawingCommandKind.EnqueueKeyBoxSpec,
            KeyBoxSpec     = spec,
            LabelKeyStr    = KeyLevelVisual.ChartObjectName(spec),
            ColorArgb       = pivotKind == "H" ? highColorArgb : lowColorArgb,
            BorderColorArgb = pivotKind == "H"
                ? Loop6StylePalette.KeyHighBorderOpaque()
                : Loop6StylePalette.KeyLowBorderOpaque(),
        });

        return spec;
    }

    public static bool IsValidObCandle(
        int lag,
        double minBodyRatio,
        double maxDojiBodyRatio,
        Func<int, (double open, double high, double low, double close)> ohlcAtLag)
    {
        var (o, h, l, c) = ohlcAtLag(lag);
        var body = Math.Abs(c - o);
        var range1 = h - l;
        if (range1 <= 0)
            return false;

        var bodyRatio = body / range1;
        var upperWick = h - Math.Max(o, c);
        var lowerWick = Math.Min(o, c) - l;
        var strongBody = bodyRatio >= minBodyRatio;
        var isDoji = bodyRatio <= maxDojiBodyRatio;
        var strongCloseBull = c > o && lowerWick <= body * 0.5;
        var strongCloseBear = c < o && upperWick <= body * 0.5;
        var potentialDoji = isDoji && (strongCloseBull || strongCloseBear);
        return strongBody || potentialDoji;
    }
}
