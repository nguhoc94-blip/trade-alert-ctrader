using System;
using System.Collections.Generic;
using System.Globalization;
using TradeAlert.Core.Models.Alerts;

namespace TradeAlert.Core.Engines.AlertEvaluators;

/// <summary>
/// Pine <c>debugAlertTracking</c> — chart labels FIRE/SKIP per pivot (lib_alerts <c>f_detect_events_raw</c>).
/// </summary>
public static class EventAlertTrackingDebug
{
    public static EventAlertTrackingLabel CreatePivotLabel(
        in AlertEvaluationContext ctx,
        bool isM15,
        EventAlertTrackingKind kind,
        string eventTag,
        string direction,
        bool fired,
        string swingKey,
        int flagPrev,
        int flagNow,
        bool anchorAtHigh,
        string? extra = null)
    {
        var tf = isM15 ? "M15" : "M5";
        var status = fired ? "FIRE" : "SKIP";
        var text = $"{eventTag} RAW {tf} {direction}\n{status}\n{swingKey}\n{flagPrev}→{flagNow}";
        if (extra != null)
            text += "\n" + extra;

        var price = anchorAtHigh ? ctx.SourceBarHigh : ctx.SourceBarLow;

        return new EventAlertTrackingLabel
        {
            SourceBarIndex = ctx.SourceBarIndex,
            SourceBarOpenTime = ctx.SourceBarOpenTimeChartLocal,
            TfToken = ctx.ChartTimeframeToken,
            Price = price,
            Text = text,
            Kind = kind,
            IsM15 = isM15,
            Fired = fired,
        };
    }

    public static void AppendCondSummaryLabels(
        in AlertEvaluationContext ctx,
        bool isM15,
        in EventRawCounts counts,
        IList<EventAlertTrackingLabel> sink)
    {
        if (isM15)
        {
            var pivots = ctx.PivotEntries ?? Array.Empty<PivotEntry>();
            var oppositeKlBuy = OppositeKlGate.HasOppositeNonBrokenKl(pivots, isBuy: true);
            var oppositeKlSell = OppositeKlGate.HasOppositeNonBrokenKl(pivots, isBuy: false);
            var hlGatedBuy = counts.BuyHL > 0 && oppositeKlBuy;
            var hlGatedSell = counts.SellHL > 0 && oppositeKlSell;

            if (counts.BuyHL > 0)
                sink.Add(SummaryLabel(ctx, isM15, "condBuyEventHLM15", counts.BuyHL, anchorAtHigh: true));
            if (counts.SellHL > 0)
                sink.Add(SummaryLabel(ctx, isM15, "condSellEventHLM15", counts.SellHL, anchorAtHigh: true));
            if (counts.BuyNG > 0)
                sink.Add(SummaryLabel(ctx, isM15, "condBuyEventNGM15", counts.BuyNG, anchorAtHigh: false));
            if (counts.SellNG > 0)
                sink.Add(SummaryLabel(ctx, isM15, "condSellEventNGM15", counts.SellNG, anchorAtHigh: false));
            if (counts.BuyNG > 0 || hlGatedBuy)
            {
                var hits = counts.BuyNG + (hlGatedBuy ? counts.BuyHL : 0);
                sink.Add(SummaryLabel(ctx, isM15, "condBuyEventHLNGM15", hits, anchorAtHigh: !hlGatedBuy || counts.BuyNG > 0));
            }
            if (counts.SellNG > 0 || hlGatedSell)
            {
                var hits = counts.SellNG + (hlGatedSell ? counts.SellHL : 0);
                sink.Add(SummaryLabel(ctx, isM15, "condSellEventHLNGM15", hits, anchorAtHigh: !hlGatedSell || counts.SellNG > 0));
            }
        }
        else
        {
            var buyM5 = counts.BuyHL + counts.BuyNG;
            var sellM5 = counts.SellHL + counts.SellNG;
            if (buyM5 > 0)
                sink.Add(SummaryLabel(ctx, isM15, "condBuyEventM5", buyM5, anchorAtHigh: true));
            if (sellM5 > 0)
                sink.Add(SummaryLabel(ctx, isM15, "condSellEventM5", sellM5, anchorAtHigh: true));
        }
    }

    static EventAlertTrackingLabel SummaryLabel(
        in AlertEvaluationContext ctx,
        bool isM15,
        string pineName,
        int hits,
        bool anchorAtHigh) =>
        new()
        {
            SourceBarIndex = ctx.SourceBarIndex,
            SourceBarOpenTime = ctx.SourceBarOpenTimeChartLocal,
            TfToken = ctx.ChartTimeframeToken,
            Price = anchorAtHigh ? ctx.SourceBarHigh : ctx.SourceBarLow,
            Text = $"🎯 {pineName}\nFIRE\nhits={hits.ToString(CultureInfo.InvariantCulture)}",
            Kind = EventAlertTrackingKind.CondSummary,
            IsM15 = isM15,
            Fired = true,
        };

    /// <summary>Pine label colors — FIRE vs SKIP, event family.</summary>
    public static (byte R, byte G, byte B) ResolveRgb(in EventAlertTrackingLabel label)
    {
        if (!label.Fired)
            return (128, 128, 128);

        if (label.Kind == EventAlertTrackingKind.CondSummary)
            return (255, 215, 0);

        return label.Kind switch
        {
            EventAlertTrackingKind.EventA when label.IsM15 => (30, 144, 255),
            EventAlertTrackingKind.EventA => (50, 205, 50),
            EventAlertTrackingKind.EventB when label.IsM15 => (220, 20, 60),
            EventAlertTrackingKind.EventB => (255, 140, 0),
            EventAlertTrackingKind.EventC when label.IsM15 => (255, 0, 255),
            EventAlertTrackingKind.EventC => (148, 0, 211),
            _ => (200, 200, 200),
        };
    }
}
