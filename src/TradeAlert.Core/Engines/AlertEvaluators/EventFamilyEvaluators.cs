using System.Collections.Generic;
using TradeAlert.Core.Models.Alerts;

namespace TradeAlert.Core.Engines.AlertEvaluators;

/// <summary>
/// EVENT M5/M15 family — ports Pine f_detect_events_raw (lib_alerts new line 269-359).
///
/// Three event types per pivot entry:
///   Event A (HL): pivot transitions to BROKEN(2).
///              M5:  only from ACTIVE(1) or BREAK_PENDING(3) → flagPrev ∈ {1,3} and flagNow==2
///              M15: any prior state → flagPrev != 2 and flagNow==2
///   Event B (NG): any state → MAIN_BROKEN(-1). flagPrev != -1 and flagNow == -1.
///   Event C (NG): key-level stop: flagNow==2, hasKeyBox, keyStopBar==barIndex, extPrev=T, extNow=F.
///              Direction swap: typ==-1 → BUY; typ==1 → SELL.
/// </summary>
public static class EventFamilyEvaluators
{
    public static AlertEvaluationResult Evaluate(in AlertEvaluationContext ctx)
    {
        if (!ctx.EventRawDetectionAvailable || ctx.PivotEntries == null || ctx.EventDedup == null)
        {
            return AlertEvaluationResult.NotFired(
                AlertReasonCodes.DepEventDetectorUnavailable,
                "Pivot/dedup payload not available for this evaluation.",
                dependencyMissing: true);
        }

        if (!ctx.IsM5JustClosed && !ctx.IsM15JustClosed)
        {
            return AlertEvaluationResult.NotFired(
                AlertReasonCodes.EventNoBarClose,
                "No M5 or M15 bar close on this bar; event not evaluated.",
                dependencyMissing: false);
        }

        return AlertEvaluationResult.NotFired(
            AlertReasonCodes.EventEvaluated,
            "Event evaluation dispatched — see condition-specific result.",
            dependencyMissing: false);
    }

    public static AlertEvaluationResult EvaluateById(
        AlertConditionId id,
        in AlertEvaluationContext ctx)
    {
        if (!ctx.EventRawDetectionAvailable || ctx.PivotEntries == null || ctx.EventDedup == null)
        {
            return AlertEvaluationResult.NotFired(
                AlertReasonCodes.DepEventDetectorUnavailable,
                "Pivot/dedup payload not available for this evaluation.",
                dependencyMissing: true);
        }

        bool isM15 = id is AlertConditionId.CondBuyEventHLM15
                         or AlertConditionId.CondSellEventHLM15
                         or AlertConditionId.CondBuyEventNGM15
                         or AlertConditionId.CondSellEventNGM15
                         or AlertConditionId.CondBuyEventHLNGM15
                         or AlertConditionId.CondSellEventHLNGM15;

        bool barClosed = isM15 ? ctx.IsM15JustClosed : ctx.IsM5JustClosed;
        if (!barClosed)
        {
            return AlertEvaluationResult.NotFired(
                AlertReasonCodes.EventNoBarClose,
                $"No {(isM15 ? "M15" : "M5")} bar close; event not evaluated.",
                dependencyMissing: false);
        }

        var counts = ResolveRawCounts(in ctx, isM15, ctx.AlertTrackingDebug, ctx.EventDetectionCache);
        var oppositeKlBuy = OppositeKlGate.HasOppositeNonBrokenKl(ctx.PivotEntries, isBuy: true);
        var oppositeKlSell = OppositeKlGate.HasOppositeNonBrokenKl(ctx.PivotEntries, isBuy: false);
        var hlGatedBuy = counts.BuyHL > 0 && oppositeKlBuy;
        var hlGatedSell = counts.SellHL > 0 && oppositeKlSell;

        bool fired = id switch
        {
            AlertConditionId.CondBuyEventM5      => counts.BuyHL > 0 || counts.BuyNG > 0,
            AlertConditionId.CondSellEventM5     => counts.SellHL > 0 || counts.SellNG > 0,
            AlertConditionId.CondBuyEventHLM15   => counts.BuyHL > 0,
            AlertConditionId.CondSellEventHLM15  => counts.SellHL > 0,
            AlertConditionId.CondBuyEventNGM15   => counts.BuyNG > 0,
            AlertConditionId.CondSellEventNGM15  => counts.SellNG > 0,
            AlertConditionId.CondBuyEventHLNGM15 => counts.BuyNG > 0 || hlGatedBuy,
            AlertConditionId.CondSellEventHLNGM15 => counts.SellNG > 0 || hlGatedSell,
            _ => false
        };

        if (fired)
        {
            string detail = id switch
            {
                AlertConditionId.CondBuyEventM5      => $"buyHL={counts.BuyHL} buyNG={counts.BuyNG}",
                AlertConditionId.CondSellEventM5     => $"sellHL={counts.SellHL} sellNG={counts.SellNG}",
                AlertConditionId.CondBuyEventHLM15   => $"buyHL={counts.BuyHL}",
                AlertConditionId.CondSellEventHLM15  => $"sellHL={counts.SellHL}",
                AlertConditionId.CondBuyEventNGM15   => $"buyNG={counts.BuyNG}",
                AlertConditionId.CondSellEventNGM15  => $"sellNG={counts.SellNG}",
                AlertConditionId.CondBuyEventHLNGM15 =>
                    $"buyNG={counts.BuyNG} buyHL={counts.BuyHL} hlGate={hlGatedBuy} oppositeKl={oppositeKlBuy}",
                AlertConditionId.CondSellEventHLNGM15 =>
                    $"sellNG={counts.SellNG} sellHL={counts.SellHL} hlGate={hlGatedSell} oppositeKl={oppositeKlSell}",
                _ => ""
            };
            return new AlertEvaluationResult
            {
                Fired = true,
                ReasonCode = AlertReasonCodes.EventFired,
                ReasonText = $"EVENT fired bar={ctx.SourceBarIndex} tf={ctx.ChartTimeframeToken} {detail}",
                StateDependencyMissing = false
            };
        }

        return AlertEvaluationResult.NotFired(
            AlertReasonCodes.EventNoTransition,
            $"No pivot transition detected bar={ctx.SourceBarIndex} tf={ctx.ChartTimeframeToken} pivots={ctx.PivotEntries.Count}",
            dependencyMissing: false);
    }

    /// <summary>Run once per bar — optional Pine-style tracking labels.</summary>
    public static EventRawCounts RunRawDetection(
        in AlertEvaluationContext ctx,
        bool isM15,
        IList<EventAlertTrackingLabel>? trackingSink)
    {
        var dedup = ctx.EventDedup!;
        var (buyHLSet_A, buyNGSet_B, buyNGSet_C, sellHLSet_A, sellNGSet_B, sellNGSet_C) =
            isM15
                ? (dedup.TriggeredA_Buy_M15, dedup.TriggeredB_Buy_M15, dedup.TriggeredC_Buy_M15,
                   dedup.TriggeredA_Sell_M15, dedup.TriggeredB_Sell_M15, dedup.TriggeredC_Sell_M15)
                : (dedup.TriggeredA_Buy_M5, dedup.TriggeredB_Buy_M5, dedup.TriggeredC_Buy_M5,
                   dedup.TriggeredA_Sell_M5, dedup.TriggeredB_Sell_M5, dedup.TriggeredC_Sell_M5);

        int buyHL = 0, sellHL = 0, buyNG = 0, sellNG = 0;

        foreach (var piv in ctx.PivotEntries!)
        {
            int flagNow  = piv.FlagNow;
            int flagPrev = piv.FlagPrev;
            int typ      = piv.Type;
            string sid   = piv.SwingIdStr;

            bool eventA = isM15
                ? (flagPrev != 2 && flagNow == 2)
                : ((flagPrev == 1 || flagPrev == 3) && flagNow == 2);

            if (eventA)
            {
                var key = sid + "_2";
                if (typ == 1)
                {
                    var fire = buyHLSet_A.Add(key);
                    if (trackingSink != null)
                    {
                        trackingSink.Add(EventAlertTrackingDebug.CreatePivotLabel(
                            in ctx, isM15, EventAlertTrackingKind.EventA, "EvA", "BUY", fire, key, flagPrev, flagNow,
                            anchorAtHigh: true));
                    }

                    if (fire) buyHL++;
                }
                else if (typ == -1)
                {
                    var fire = sellHLSet_A.Add(key);
                    if (trackingSink != null)
                    {
                        trackingSink.Add(EventAlertTrackingDebug.CreatePivotLabel(
                            in ctx, isM15, EventAlertTrackingKind.EventA, "EvA", "SELL", fire, key, flagPrev, flagNow,
                            anchorAtHigh: true));
                    }

                    if (fire) sellHL++;
                }
            }

            if (flagPrev != -1 && flagNow == -1)
            {
                var key = sid + "_-1";
                if (typ == 1)
                {
                    var fire = buyNGSet_B.Add(key);
                    if (trackingSink != null)
                    {
                        trackingSink.Add(EventAlertTrackingDebug.CreatePivotLabel(
                            in ctx, isM15, EventAlertTrackingKind.EventB, "EvB", "BUY", fire, key, flagPrev, flagNow,
                            anchorAtHigh: true));
                    }

                    if (fire) buyNG++;
                }
                else if (typ == -1)
                {
                    var fire = sellNGSet_B.Add(key);
                    if (trackingSink != null)
                    {
                        trackingSink.Add(EventAlertTrackingDebug.CreatePivotLabel(
                            in ctx, isM15, EventAlertTrackingKind.EventB, "EvB", "SELL", fire, key, flagPrev, flagNow,
                            anchorAtHigh: true));
                    }

                    if (fire) sellNG++;
                }
            }

            if (flagNow == 2 && piv.HasKeyBox
                && piv.KeyStopBar == EventCKeyStopMatchBar(in ctx)
                && piv.KeyExtendingPrev && !piv.KeyExtending)
            {
                var key = sid + "_2";
                if (typ == -1)
                {
                    var fire = buyNGSet_C.Add(key);
                    if (trackingSink != null)
                    {
                        trackingSink.Add(EventAlertTrackingDebug.CreatePivotLabel(
                            in ctx, isM15, EventAlertTrackingKind.EventC, "EvC", "BUY", fire, key, flagPrev, flagNow,
                            anchorAtHigh: false,
                            extra: $"extPrev={piv.KeyExtendingPrev}→{piv.KeyExtending}"));
                    }

                    if (fire) buyNG++;
                }
                else if (typ == 1)
                {
                    var fire = sellNGSet_C.Add(key);
                    if (trackingSink != null)
                    {
                        trackingSink.Add(EventAlertTrackingDebug.CreatePivotLabel(
                            in ctx, isM15, EventAlertTrackingKind.EventC, "EvC", "SELL", fire, key, flagPrev, flagNow,
                            anchorAtHigh: false,
                            extra: $"extPrev={piv.KeyExtendingPrev}→{piv.KeyExtending}"));
                    }

                    if (fire) sellNG++;
                }
            }
        }

        var counts = new EventRawCounts
        {
            BuyHL = buyHL,
            SellHL = sellHL,
            BuyNG = buyNG,
            SellNG = sellNG,
        };

        if (trackingSink != null)
            EventAlertTrackingDebug.AppendCondSummaryLabels(in ctx, isM15, in counts, trackingSink);

        return counts;
    }

    /// <summary>Ensure detection runs once per bar; emit tracking only on first run when debug enabled.</summary>
    public static EventRawCounts ResolveRawCounts(
        in AlertEvaluationContext ctx,
        bool isM15,
        bool alertTrackingDebug,
        EventDetectionSessionCache? cache)
    {
        if (cache != null
            && cache.TryGet(ctx.SourceBarIndex, ctx.ChartTimeframeToken, isM15, out var cached)
            && (!alertTrackingDebug
                || cache.HasTrackingLabels(ctx.SourceBarIndex, ctx.ChartTimeframeToken, isM15)))
            return cached;

        List<EventAlertTrackingLabel>? sink = alertTrackingDebug ? new List<EventAlertTrackingLabel>() : null;
        var counts = RunRawDetection(in ctx, isM15, sink);
        cache?.Store(ctx.SourceBarIndex, ctx.ChartTimeframeToken, isM15, in counts, sink);
        return counts;
    }

    /// <summary>Pine <c>f_detect_events_raw</c>: Event C when <c>keyStopBar == bar_index - 1</c>.</summary>
    static int EventCKeyStopMatchBar(in AlertEvaluationContext ctx) =>
        (ctx.PineBarIndex ?? ctx.SourceBarIndex) - 1;

    public static AlertEvaluationResult EvaluatePhaKhungLon(in AlertEvaluationContext ctx)
    {
        if (!ctx.EventRawDetectionAvailable || ctx.PivotEntries == null || ctx.EventDedup == null)
        {
            return AlertEvaluationResult.NotFired(
                AlertReasonCodes.DepEventDetectorUnavailable,
                "Pivot/dedup payload not available for PhaKhungLon evaluation.",
                dependencyMissing: true);
        }

        if (!ctx.IsBarClosed)
        {
            return AlertEvaluationResult.NotFired(
                AlertReasonCodes.PhaKhungLonNoBarClose,
                "PhaKhungLon: bar not confirmed (isBarClosed=false).",
                dependencyMissing: false);
        }

        var dedup = ctx.EventDedup;
        int aHits = 0, bHits = 0, cHits = 0;

        foreach (var piv in ctx.PivotEntries)
        {
            int flagNow = piv.FlagNow;
            string sid = piv.SwingIdStr;

            if (flagNow == 2)
            {
                var key = sid + "_2";
                if (dedup.TriggeredA_PKL.Add(key)) aHits++;
            }

            if (flagNow == -1)
            {
                var key = sid + "_-1";
                if (dedup.TriggeredB_PKL.Add(key)) bHits++;
            }

            if (flagNow == 2 && piv.HasKeyBox && !piv.KeyExtending
                && piv.KeyStopBar == ctx.SourceBarIndex)
            {
                var key = sid + "_C" + ctx.SourceBarIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (dedup.TriggeredC_PKL.Add(key)) cHits++;
            }
        }

        if (aHits + bHits + cHits > 0)
        {
            return new AlertEvaluationResult
            {
                Fired = true,
                ReasonCode = AlertReasonCodes.PhaKhungLonFired,
                ReasonText = $"PhaKhungLon bar={ctx.SourceBarIndex} tf={ctx.ChartTimeframeToken} A={aHits} B={bHits} C={cHits}",
                StateDependencyMissing = false
            };
        }

        return AlertEvaluationResult.NotFired(
            AlertReasonCodes.PhaKhungLonNoTransition,
            $"PhaKhungLon: no new transition bar={ctx.SourceBarIndex} tf={ctx.ChartTimeframeToken} pivots={ctx.PivotEntries.Count}",
            dependencyMissing: false);
    }
}
