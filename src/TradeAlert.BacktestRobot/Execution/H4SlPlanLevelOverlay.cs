using System;
using System.Collections.Generic;
using cAlgo.API;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>
/// Horizontal price lines for entry / SL / TP C / TP D on the chart (H4 SL debug).
/// Each trade keeps its own level set; lines extend while any leg is live and freeze when all legs end.
/// </summary>
public sealed class H4SlPlanLevelOverlay
{
    public const string Prefix = "L6H4DBG|";

    readonly Chart _chart;
    readonly Bars _bars;
    readonly Dictionary<string, PlanLevelRecord> _records = new(StringComparer.Ordinal);

    static readonly string[] LevelTags = { "ENTRY", "SL", "TP_C", "TP_D" };

    sealed class PlanLevelRecord
    {
        public string TradeKey { get; init; } = "";
        public HashSet<string> LegLabels { get; init; } = new(StringComparer.Ordinal);
        public H4SlPlanDebugSnapshot Snap { get; init; }
        public int BarFrom { get; init; }
        /// <summary>When set, line end bar is fixed and no longer extends.</summary>
        public int? BarToFrozen { get; set; }
        public bool EverLive { get; set; }
    }

    public H4SlPlanLevelOverlay(Chart chart, Bars bars)
    {
        _chart = chart;
        _bars = bars;
    }

    /// <param name="freezeImmediately">When true (dry-run), lines do not extend past the registration bar.</param>
    public void Register(IReadOnlyList<string> legLabels, in H4SlPlanDebugSnapshot snap, bool freezeImmediately = false)
    {
        if (!snap.HasData || snap.ChartBar < 0 || _bars.Count == 0 || legLabels.Count == 0)
            return;

        var tradeKey = BuildTradeKey(legLabels[0], snap.ChartBar);
        if (_records.ContainsKey(tradeKey))
            return;

        var barFrom = Math.Min(snap.ChartBar, _bars.Count - 1);
        var barTo = Math.Min(Math.Max(barFrom, _bars.Count - 1), _bars.Count - 1);
        var labels = new HashSet<string>(legLabels, StringComparer.Ordinal);
        _records[tradeKey] = new PlanLevelRecord
        {
            TradeKey = tradeKey,
            LegLabels = labels,
            Snap = snap,
            BarFrom = barFrom,
            BarToFrozen = freezeImmediately ? barTo : null,
        };

        Redraw(tradeKey, barTo);
    }

    /// <summary>Extend active trades; freeze and stop extending when no leg is live.</summary>
    public void Tick(int currentBar, Func<string, bool> isLegLive)
    {
        if (_bars.Count == 0 || _records.Count == 0)
            return;

        currentBar = Math.Min(Math.Max(currentBar, 0), _bars.Count - 1);
        foreach (var tradeKey in new List<string>(_records.Keys))
        {
            var rec = _records[tradeKey];
            if (rec.BarToFrozen.HasValue)
                continue;

            var anyLive = false;
            foreach (var label in rec.LegLabels)
            {
                if (isLegLive(label))
                {
                    anyLive = true;
                    break;
                }
            }

            if (anyLive)
            {
                rec.EverLive = true;
                Redraw(tradeKey, EndBarFor(rec, currentBar));
            }
            else if (rec.EverLive)
            {
                rec.BarToFrozen = currentBar;
                Redraw(tradeKey, currentBar);
            }
            else
                Redraw(tradeKey, EndBarFor(rec, currentBar));
        }
    }

    public void Clear()
    {
        foreach (var tradeKey in new List<string>(_records.Keys))
            RemoveTrade(tradeKey);
        _records.Clear();
    }

    static string BuildTradeKey(string primaryLabel, int chartBar)
    {
        var idx = primaryLabel.LastIndexOf('|');
        var baseLabel = idx >= 0 ? primaryLabel[..idx] : primaryLabel;
        return SanitizeKey($"{baseLabel}@{chartBar}");
    }

    static string SanitizeKey(string key) =>
        key.Replace('|', '_').Replace(' ', '_');

    static int EndBarFor(PlanLevelRecord rec, int candidateBar)
    {
        var barFrom = rec.BarFrom;
        var barTo = Math.Max(barFrom, candidateBar);
        return Math.Min(barTo, int.MaxValue);
    }

    void Redraw(string tradeKey, int barTo)
    {
        if (!_records.TryGetValue(tradeKey, out var rec))
            return;

        var barFrom = rec.BarFrom;
        if (barFrom < 0 || barTo < barFrom || _bars.Count == 0)
            return;

        barFrom = Math.Min(barFrom, _bars.Count - 1);
        barTo = Math.Min(barTo, _bars.Count - 1);

        var snap = rec.Snap;
        DrawLevel(tradeKey, "ENTRY", snap.Entry, barFrom, barTo, Color.FromArgb(220, 80, 180, 255));
        DrawLevel(tradeKey, "SL", snap.StopLoss, barFrom, barTo, Color.FromArgb(220, 255, 90, 90));
        if (snap.TpC > 0)
            DrawLevel(tradeKey, "TP_C", snap.TpC, barFrom, barTo, Color.FromArgb(220, 100, 230, 120));
        else
            RemoveLevel(tradeKey, "TP_C");

        if (snap.TpD > 0)
            DrawLevel(tradeKey, "TP_D", snap.TpD, barFrom, barTo, Color.FromArgb(220, 200, 140, 255));
        else
            RemoveLevel(tradeKey, "TP_D");
    }

    void RemoveTrade(string tradeKey)
    {
        foreach (var tag in LevelTags)
            RemoveLevel(tradeKey, tag);
    }

    void DrawLevel(string tradeKey, string tag, double price, int barFrom, int barTo, Color color)
    {
        if (price <= 0 || barFrom < 0 || barTo < barFrom)
            return;

        var key = Prefix + tradeKey + "|" + tag;
        if (_chart.FindObject(key) is ChartTrendLine existing)
        {
            existing.Time1 = _bars.OpenTimes[barFrom];
            existing.Time2 = _bars.OpenTimes[barTo];
            existing.Y1 = price;
            existing.Y2 = price;
            existing.Color = color;
            return;
        }

        var line = _chart.DrawTrendLine(key, barFrom, price, barTo, price, color);
        line.Thickness = 2;
        line.LineStyle = tag == "SL" ? LineStyle.Solid : LineStyle.DotsVeryRare;
        line.IsInteractive = false;
    }

    void RemoveLevel(string tradeKey, string tag)
    {
        var key = Prefix + tradeKey + "|" + tag;
        if (_chart.FindObject(key) != null)
            _chart.RemoveObject(key);
    }
}
