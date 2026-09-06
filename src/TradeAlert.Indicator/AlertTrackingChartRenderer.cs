using System;
using System.Collections.Generic;
using cAlgo.API;
using TradeAlert.Core.Engines.AlertEvaluators;
using TradeAlert.Core.Models;

namespace TradeAlert.Indicator;

/// <summary>Draws Pine-style alert tracking labels on the host chart.</summary>
public sealed class AlertTrackingChartRenderer
{
    readonly Chart _chart;
    readonly Bars _bars;
    readonly string _chartTfToken;
    int _labelSeq;

    const int MaxCondFireMarkers = 30;
    readonly Queue<string> _condFireNames = new();

    public AlertTrackingChartRenderer(Chart chart, Bars bars, string chartTfToken)
    {
        _chart = chart;
        _bars = bars;
        _chartTfToken = chartTfToken;
    }

    public void DrawLabels(IReadOnlyList<EventAlertTrackingLabel> labels)
    {
        foreach (var label in labels)
            DrawLabel(label);
    }

    void DrawLabel(in EventAlertTrackingLabel label)
    {
        var (r, g, b) = EventAlertTrackingDebug.ResolveRgb(label);
        var color = Color.FromArgb(220, r, g, b);
        var name = $"Loop6AlertTrack_{label.TfToken}_{label.SourceBarOpenTime.Ticks}_{++_labelSeq}";

        if (string.Equals(label.TfToken, _chartTfToken, StringComparison.Ordinal))
        {
            var idx = label.SourceBarIndex;
            if (idx < 0 || idx >= _bars.Count)
                return;

            _chart.DrawText(name, label.Text, idx, label.Price, color);
            return;
        }

        // M5 (or other TF) on higher chart: anchor at source bar open time, not parent HTF bar index.
        var timeUtc = ChartTimePolicy.ChartLocalUnspecifiedToUtcInstant(label.SourceBarOpenTime);
        _chart.DrawText(name, label.Text, timeUtc, label.Price, color);
    }

    /// <summary>
    /// Draw ▲ (BUY) or ▼ (SELL) at the M5 bar time when condBuyEventM5/condSellEventM5 fires.
    /// Keeps only the 30 most recent markers.
    /// </summary>
    public void DrawM5CondFireMarker(bool isBuy, DateTime barOpenLocal, double price)
    {
        var timeUtc = ChartTimePolicy.ChartLocalUnspecifiedToUtcInstant(barOpenLocal);
        var name = $"Loop6M5Cond_{(isBuy ? "B" : "S")}_{barOpenLocal.Ticks}_{++_labelSeq}";
        var color = isBuy
            ? Color.FromArgb(230, 0, 220, 100)
            : Color.FromArgb(230, 255, 70, 50);
        var text = isBuy ? "▲condBuyM5" : "▼condSellM5";

        _chart.DrawText(name, text, timeUtc, price, color);

        _condFireNames.Enqueue(name);
        while (_condFireNames.Count > MaxCondFireMarkers)
            _chart.RemoveObject(_condFireNames.Dequeue());
    }
}
