using System;
using System.Collections.Generic;
using System.Globalization;
using cAlgo.API;
using TradeAlert.Core.Models.Alerts;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>Chart labels at compound FIRE / deferred swing-C search (debug overlay).</summary>
public sealed class CompoundFireChartDebugLabels
{
    public const string ObjectPrefix = "L6DBG|";

    readonly Chart _chart;
    readonly Bars _bars;
    readonly double _labelOffsetPrice;
    readonly Dictionary<string, HashSet<string>> _swingCCandidateKeysByFire = new();

    public CompoundFireChartDebugLabels(Chart chart, Bars bars, double pipSize)
    {
        _chart = chart;
        _bars = bars;
        _labelOffsetPrice = Math.Max(pipSize, 0.00001) * 10;
    }

    public static string SwingCCandidatePrefix(in CompoundFireEvent ev) =>
        $"{ObjectPrefix}CCAND|R{ev.SlotIndex}|c{ev.ChartBarIndex}|e{ev.EventBarIndex}";

    public static string SwingCCandidateKey(in CompoundFireEvent ev, int pivotBar) =>
        $"{SwingCCandidatePrefix(in ev)}|p{pivotBar}";

    public static string SwingCPickedKey(in CompoundFireEvent ev) =>
        $"{ObjectPrefix}CPICK|R{ev.SlotIndex}|c{ev.ChartBarIndex}|e{ev.EventBarIndex}";

    public static string FireLabelKey(in CompoundFireEvent ev) =>
        $"{ObjectPrefix}FIRE|R{ev.SlotIndex}|c{ev.ChartBarIndex}|e{ev.EventBarIndex}";

    public static string WaitCKey(in CompoundFireEvent ev) =>
        $"{ObjectPrefix}WAITC|R{ev.SlotIndex}|c{ev.ChartBarIndex}|e{ev.EventBarIndex}";

    public void DrawFire(in CompoundFireEvent ev, string legSnapshot, int barIndex)
    {
        var isBuy = ev.Direction == SignalDirection.Buy;
        var text = BuildHeader("FIRE", in ev, barIndex)
                   + "\n" + FormatLegLine(legSnapshot);
        DrawLabel(FireLabelKey(in ev), barIndex, isBuy, text, isBuy ? Color.LimeGreen : Color.OrangeRed);
        DrawDirectionIcon(FireLabelKey(in ev) + "|ico", barIndex, isBuy);
    }

    public void DrawWaitC(
        in CompoundFireEvent ev,
        string legSnapshot,
        int barIndex,
        int swingBPivotBar,
        string? reason,
        string? statusLine = null,
        string? detailLine = null)
    {
        var isBuy = ev.Direction == SignalDirection.Buy;
        var text = BuildWaitCText(in ev, barIndex, swingBPivotBar, heldBars: 0, statusLine, detailLine, reason, legSnapshot);
        DrawLabel(WaitCKey(in ev), barIndex, isBuy, text, Color.Gold);
    }

    /// <summary>Refresh WAIT-C label each bar while deferred (held bars + diagnostic).</summary>
    public void UpdateWaitC(
        in CompoundFireEvent ev,
        int fireBarIndex,
        int heldBars,
        int swingBPivotBar,
        string statusLine,
        string? detailLine = null)
    {
        var isBuy = ev.Direction == SignalDirection.Buy;
        var text = BuildWaitCText(in ev, fireBarIndex, swingBPivotBar, heldBars, statusLine, detailLine, reason: null, legSnapshot: null);
        DrawLabel(WaitCKey(in ev), fireBarIndex, isBuy, text, Color.Gold);
    }

    static string BuildWaitCText(
        in CompoundFireEvent ev,
        int barIndex,
        int swingBPivotBar,
        int heldBars,
        string? statusLine,
        string? detailLine,
        string? reason,
        string? legSnapshot)
    {
        var bPart = swingBPivotBar >= 0 ? $"B={swingBPivotBar}" : "B=?";
        var heldPart = heldBars > 0 ? $" held={heldBars}bar" : "";
        var line2 = !string.IsNullOrWhiteSpace(statusLine)
            ? statusLine!
            : swingBPivotBar >= 0 ? $"{bPart} find C..." : "find C...";
        if (!string.IsNullOrWhiteSpace(reason) && string.IsNullOrWhiteSpace(statusLine))
            line2 += " | " + Truncate(reason, 48);

        var text = BuildHeader("WAIT-C", in ev, barIndex)
                   + $"\n{line2}{heldPart}";
        if (!string.IsNullOrWhiteSpace(detailLine))
            text += "\n" + Truncate(detailLine, 64);
        if (!string.IsNullOrWhiteSpace(legSnapshot))
            text += "\n" + FormatLegLine(legSnapshot);
        return text;
    }

    /// <summary>Mark deferred wait as dropped (RR fail, B broken, etc.).</summary>
    public void MarkDeferredDrop(in CompoundFireEvent ev, int fireBarIndex, bool isBuy, string reason)
    {
        var key = WaitCKey(in ev);
        if (_chart.FindObject(key) is ChartText existing)
        {
            existing.Text = BuildHeader("WAIT-C✗", in ev, fireBarIndex)
                            + "\n" + Truncate(reason, 64);
            existing.Color = Color.DimGray;
            return;
        }

        DrawLabel(key, fireBarIndex, isBuy, BuildHeader("WAIT-C✗", in ev, fireBarIndex) + "\n" + Truncate(reason, 64), Color.DimGray);
    }

    /// <summary>Swing-B keylevel band — entry may oscillate / move inside this zone (RR move).</summary>
    public void DrawEntryOscillationZone(
        in CompoundFireEvent ev,
        int fireBarIndex,
        double keyLow,
        double keyHigh,
        bool isBuy)
    {
        if (keyLow <= 0 || keyHigh <= 0 || keyHigh <= keyLow || fireBarIndex < 0)
            return;

        var key = $"{ObjectPrefix}ENT|R{ev.SlotIndex}|c{fireBarIndex}|e{ev.EventBarIndex}";
        var barTo = Math.Min(fireBarIndex + 120, _bars.Count - 1);

        if (_chart.FindObject(key) is ChartRectangle existing)
        {
            existing.Y1 = keyLow;
            existing.Y2 = keyHigh;
            return;
        }

        var fill = isBuy
            ? Color.FromArgb(50, 80, 180, 255)
            : Color.FromArgb(50, 255, 160, 80);
        var rect = _chart.DrawRectangle(key, fireBarIndex, keyLow, barTo, keyHigh, fill);
        rect.IsFilled = true;
        rect.Thickness = 1;
    }

    public void MarkCReady(
        in CompoundFireEvent ev,
        int fireBarIndex,
        int swingCPivotBar,
        int heldBars,
        IReadOnlyList<TradePlan> plans)
    {
        var key = WaitCKey(in ev);
        if (_chart.FindObject(key) is ChartText existing)
        {
            var isBuy = ev.Direction == SignalDirection.Buy;
            existing.Text = BuildCReadyText(in ev, fireBarIndex, swingCPivotBar, heldBars, plans);
            existing.Color = isBuy ? Color.DeepSkyBlue : Color.MediumOrchid;
        }
    }

    /// <summary>
    /// Draw/update one label per swing-C pivot candidate strictly after B; refreshed each bar while deferred.
    /// </summary>
    public void SyncSwingCCandidateLabels(
        in CompoundFireEvent ev,
        bool isBuy,
        IReadOnlyList<SwingCPivotAudit.Row> rows,
        int watchBar)
    {
        var fireKey = WaitCKey(in ev);
        if (!_swingCCandidateKeysByFire.TryGetValue(fireKey, out var tracked))
        {
            tracked = new HashSet<string>(StringComparer.Ordinal);
            _swingCCandidateKeysByFire[fireKey] = tracked;
        }

        var nextKeys = new HashSet<string>(StringComparer.Ordinal);
        var firstValidBar = ResolveFirstValidBar(rows);

        foreach (var row in rows)
        {
            var key = SwingCCandidateKey(in ev, row.Bar);
            nextKeys.Add(key);
            var isWouldPick = firstValidBar == row.Bar;
            var text = FormatSwingCCandidateLabel(in ev, row, watchBar, isWouldPick);
            var color = row.ValidCCandidate
                ? isWouldPick ? Color.DeepSkyBlue : Color.Cyan
                : Color.DimGray;
            DrawPivotLabel(key, row.Bar, row.Price, isHighPivot: isBuy, text, color);
        }

        foreach (var stale in tracked)
        {
            if (nextKeys.Contains(stale))
                continue;
            if (_chart.FindObject(stale) != null)
                _chart.RemoveObject(stale);
        }

        tracked.Clear();
        foreach (var key in nextKeys)
            tracked.Add(key);
    }

    /// <summary>Mark the confirmed swing-C pivot on chart and remove transient candidate labels.</summary>
    public void MarkSwingCPickedAtPivot(
        in CompoundFireEvent ev,
        int fireBarIndex,
        int pickedBar,
        int heldBars,
        bool isBuy,
        SwingCPivotAudit.Row? pickedRow = null)
    {
        ClearSwingCCandidateLabels(in ev);

        if (pickedBar <= 0)
            return;

        var price = pickedRow?.Price ?? ResolveBarPivotPrice(pickedBar, isBuy);
        var text = FormatSwingCPickedLabel(in ev, fireBarIndex, pickedBar, heldBars, pickedRow);
        var color = isBuy ? Color.DeepSkyBlue : Color.MediumOrchid;
        DrawPivotLabel(SwingCPickedKey(in ev), pickedBar, price, isHighPivot: isBuy, text, color);
    }

    public void ClearSwingCCandidateLabels(in CompoundFireEvent ev)
    {
        var fireKey = WaitCKey(in ev);
        if (!_swingCCandidateKeysByFire.TryGetValue(fireKey, out var tracked))
            return;

        foreach (var key in tracked)
        {
            if (_chart.FindObject(key) != null)
                _chart.RemoveObject(key);
        }

        tracked.Clear();
    }

    public static string FormatSwingCCandidateLabel(
        in CompoundFireEvent ev,
        SwingCPivotAudit.Row row,
        int watchBar,
        bool isWouldPick)
    {
        var pickTag = isWouldPick ? "\n<<wouldPick" : "";
        return $"R{ev.SlotIndex + 1} C? bar={row.Bar}\n{row.FlagLabel} validC={(row.ValidCCandidate ? 'Y' : 'N')}\nwatch@{watchBar}{pickTag}";
    }

    public static string FormatSwingCPickedLabel(
        in CompoundFireEvent ev,
        int fireBarIndex,
        int pickedBar,
        int heldBars,
        SwingCPivotAudit.Row? pickedRow)
    {
        var text = BuildHeader("C✓ PICKED", in ev, fireBarIndex)
                   + $"\nC@{pickedBar} held={heldBars}bar";
        if (pickedRow is { } row)
            text += $"\n{row.FlagLabel} validC=Y price={FmtPrice(row.Price)}";
        return text;
    }

    static int ResolveFirstValidBar(IReadOnlyList<SwingCPivotAudit.Row> rows)
    {
        var firstValidBar = -1;
        foreach (var row in rows)
        {
            if (row.ValidCCandidate && (firstValidBar < 0 || row.Bar < firstValidBar))
                firstValidBar = row.Bar;
        }

        return firstValidBar;
    }

    double ResolveBarPivotPrice(int barIndex, bool isHighPivot)
    {
        if (barIndex < 0 || barIndex >= _bars.Count)
            return 0;
        return isHighPivot ? _bars.HighPrices[barIndex] : _bars.LowPrices[barIndex];
    }

    /// <summary>
    /// Append SL / RR TPC / RR TPD to the FIRE or C-OK label after orders are placed
    /// (immediate fire path uses FIRE; deferred split uses C-OK via <see cref="MarkCReady"/>).
    /// </summary>
    public void TryAnnotatePlanMetrics(in CompoundFireEvent ev, IReadOnlyList<TradePlan> plans)
    {
        if (plans.Count == 0)
            return;

        var metricsBlock = FormatPlanMetricsBlock(plans);
        if (string.IsNullOrEmpty(metricsBlock))
            return;

        var waitKey = WaitCKey(in ev);
        if (_chart.FindObject(waitKey) is ChartText waitLabel)
        {
            if (!waitLabel.Text.Contains("entry=", StringComparison.Ordinal))
                waitLabel.Text += $"\n{metricsBlock}";
            return;
        }

        var fireKey = FireLabelKey(in ev);
        if (_chart.FindObject(fireKey) is ChartText fireLabel
            && !fireLabel.Text.Contains("entry=", StringComparison.Ordinal))
        {
            fireLabel.Text += $"\n{metricsBlock}";
        }
    }

    static string BuildCReadyText(
        in CompoundFireEvent ev,
        int fireBarIndex,
        int swingCPivotBar,
        int heldBars,
        IReadOnlyList<TradePlan> plans)
    {
        var text = BuildHeader("C-OK", in ev, fireBarIndex)
                   + $"\nC={swingCPivotBar} held={heldBars}bar";
        var metricsBlock = FormatPlanMetricsBlock(plans);
        if (!string.IsNullOrEmpty(metricsBlock))
            text += $"\n{metricsBlock}";
        return text;
    }

    /// <summary>Entry/SL/TP prices + SL pips + RR for C/D legs.</summary>
    public static string FormatPlanMetricsBlock(IReadOnlyList<TradePlan> plans)
    {
        ExtractSplitPlanDetails(
            plans,
            out var entry,
            out var slPrice,
            out var tpC,
            out var tpD,
            out var slPips,
            out var rrTpC,
            out var rrTpD);
        if (entry <= 0 && slPips <= 0)
            return "";

        var lines = new List<string>();
        if (entry > 0)
        {
            var priceLine = $"entry={FmtPrice(entry)} sl={FmtPrice(slPrice)}";
            if (tpC > 0)
                priceLine += $" tpC={FmtPrice(tpC)}";
            if (tpD > 0)
                priceLine += $" tpD={FmtPrice(tpD)}";
            lines.Add(priceLine);
        }

        if (slPips > 0)
        {
            var rrLine = $"SL={Fmt1(slPips)}pip RRc={Fmt2(rrTpC)}";
            if (plans.Count > 1 || tpD > 0)
                rrLine += $" RRd={Fmt2(rrTpD)}";
            lines.Add(rrLine);
        }

        // H4 SL / RR guard states embedded in TradePlan.Reason via slFloorNote.
        if (plans.Count > 0)
        {
            var h4Line = ExtractH4GuardLine(plans[0].Reason);
            if (!string.IsNullOrEmpty(h4Line))
                lines.Add(h4Line);
        }

        return string.Join("\n", lines);
    }

    /// <inheritdoc cref="FormatPlanMetricsBlock"/>
    public static string FormatPlanMetricsLine(IReadOnlyList<TradePlan> plans) =>
        FormatPlanMetricsBlock(plans);

    public static void ExtractSplitPlanMetrics(
        IReadOnlyList<TradePlan> plans,
        out double slPips,
        out double rrTpC,
        out double rrTpD)
    {
        ExtractSplitPlanDetails(plans, out _, out _, out _, out _, out slPips, out rrTpC, out rrTpD);
    }

    public static void ExtractSplitPlanDetails(
        IReadOnlyList<TradePlan> plans,
        out double entry,
        out double slPrice,
        out double tpC,
        out double tpD,
        out double slPips,
        out double rrTpC,
        out double rrTpD)
    {
        entry = 0;
        slPrice = 0;
        tpC = 0;
        tpD = 0;
        slPips = 0;
        rrTpC = 0;
        rrTpD = 0;
        if (plans.Count == 0)
            return;

        TradePlan? cLeg = null;
        TradePlan? dLeg = null;
        foreach (var plan in plans)
        {
            if (string.Equals(plan.TpLegTag, "C", StringComparison.Ordinal))
                cLeg = plan;
            else if (string.Equals(plan.TpLegTag, "D", StringComparison.Ordinal))
                dLeg = plan;
        }

        cLeg ??= plans[0];
        if (plans.Count > 1)
            dLeg ??= plans[1];

        entry = cLeg.EntryLimit;
        slPrice = cLeg.StopLoss;
        tpC = cLeg.TakeProfit;
        if (dLeg != null)
            tpD = dLeg.TakeProfit;

        slPips = cLeg.StopLossPips;
        rrTpC = TradePlanRrLog.AppliedRr(cLeg.StopLossPips, cLeg.TakeProfitPips);
        if (dLeg != null)
            rrTpD = TradePlanRrLog.AppliedRr(dLeg.StopLossPips, dLeg.TakeProfitPips);
    }

    static string Fmt1(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
    static string Fmt2(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    static string FmtPrice(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

    /// <summary>
    /// Trích tất cả <c>[H4…]</c> bracket notes từ <paramref name="reason"/> (plan.Reason)
    /// để hiển thị H4 SL resolve info và RR guard state trên debug label.
    /// </summary>
    static string ExtractH4GuardLine(string reason)
    {
        if (string.IsNullOrEmpty(reason) || !reason.Contains("[H4", StringComparison.Ordinal))
            return "";

        var sb = new System.Text.StringBuilder();
        var i = 0;
        while (i < reason.Length)
        {
            var start = reason.IndexOf("[H4", i, StringComparison.Ordinal);
            if (start < 0) break;
            var end = reason.IndexOf(']', start);
            if (end < 0) break;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(reason, start, end - start + 1);
            i = end + 1;
        }
        return sb.ToString();
    }

    public void DrawSkip(in CompoundFireEvent ev, string legSnapshot, int barIndex, string reason)
    {
        var isBuy = ev.Direction == SignalDirection.Buy;
        var text = BuildHeader("SKIP", in ev, barIndex)
                   + "\n" + Truncate(reason, 64)
                   + "\n" + FormatLegLine(legSnapshot);
        var key = $"{ObjectPrefix}SKIP|R{ev.SlotIndex}|c{barIndex}|e{ev.EventBarIndex}";
        DrawLabel(key, barIndex, isBuy, text, Color.Gray);
    }

    static string BuildHeader(string tag, in CompoundFireEvent ev, int barIndex)
    {
        var dir = ev.Direction == SignalDirection.Buy ? "BUY"
            : ev.Direction == SignalDirection.Sell ? "SELL"
            : ev.Direction.ToString();
        return $"R{ev.SlotIndex + 1} {dir} {tag} ch={barIndex} m5={ev.EventBarIndex}";
    }

    static string FormatLegLine(string legSnapshot) =>
        string.IsNullOrWhiteSpace(legSnapshot) ? "" : Truncate(legSnapshot, 120);

    void DrawLabel(string key, int barIndex, bool isBuy, string text, Color color)
    {
        if (barIndex < 0 || barIndex >= _bars.Count)
            return;

        var hi = _bars.HighPrices[barIndex];
        var lo = _bars.LowPrices[barIndex];
        var price = isBuy ? lo - _labelOffsetPrice : hi + _labelOffsetPrice;
        DrawTextAt(key, barIndex, price, text, color);
    }

    void DrawPivotLabel(string key, int barIndex, double pivotPrice, bool isHighPivot, string text, Color color)
    {
        if (barIndex < 0 || barIndex >= _bars.Count || pivotPrice <= 0)
            return;

        var price = isHighPivot ? pivotPrice + _labelOffsetPrice : pivotPrice - _labelOffsetPrice;
        DrawTextAt(key, barIndex, price, text, color);
    }

    void DrawTextAt(string key, int barIndex, double price, string text, Color color)
    {
        if (_chart.FindObject(key) is ChartText existing)
        {
            existing.Text = text;
            existing.Color = color;
            return;
        }

        _chart.DrawText(key, text, barIndex, price, color);
    }

    void DrawDirectionIcon(string key, int barIndex, bool isBuy)
    {
        if (barIndex < 0 || barIndex >= _bars.Count)
            return;

        var price = isBuy ? _bars.LowPrices[barIndex] : _bars.HighPrices[barIndex];
        var icon = isBuy ? ChartIconType.UpArrow : ChartIconType.DownArrow;
        var color = isBuy ? Color.LimeGreen : Color.OrangeRed;

        if (_chart.FindObject(key) is ChartIcon existing)
        {
            existing.IconType = icon;
            existing.Color = color;
            return;
        }

        _chart.DrawIcon(key, icon, barIndex, price, color);
    }

    // ── Cancel state ──────────────────────────────────────────────────────────

    /// <summary>
    /// Draw a cancel marker at <paramref name="cancelBarIndex"/> and update the original FIRE label
    /// (identified by <paramref name="fireLabelKey"/>) to reflect the canceled state.
    /// </summary>
    public void DrawCancel(
        string fireLabelKey,
        int fireBarIndex,
        int cancelBarIndex,
        bool isBuy,
        string reason)
    {
        // Dim the original FIRE label to strikethrough-gray.
        if (_chart.FindObject(fireLabelKey) is ChartText existing)
        {
            existing.Color = Color.DimGray;
            existing.Text  = existing.Text.Replace("FIRE", "FIRE✗").Replace("WAIT-C", "WAIT-C✗");
        }

        // Cancel tag at the cancel bar.
        var key = $"{ObjectPrefix}CXL|{fireLabelKey}@{cancelBarIndex}";
        var text = $"CANCEL @{cancelBarIndex}\n{Truncate(reason, 48)}";
        DrawLabel(key, cancelBarIndex, isBuy, text, Color.DimGray);
    }

    // ── Near-D cancel zone ────────────────────────────────────────────────────

    public static string NearDCancelZoneScopeKey(in CompoundFireEvent ev) =>
        $"R{ev.SlotIndex}|c{ev.ChartBarIndex}|e{ev.EventBarIndex}";

    /// <summary>
    /// Draw near-D cancel zone (D body) + buffered touch band locked at fire time.
    /// 0 values mean "not set" — nothing is drawn.
    /// </summary>
    public void DrawNearDCancelZoneDebug(
        string scopeKey,
        int fireBarIndex,
        double zoneLow,
        double zoneHigh,
        bool isBuy,
        bool isOb,
        string? tfToken = null)
    {
        if (zoneLow <= 0 || zoneHigh <= 0 || zoneHigh <= zoneLow || fireBarIndex < 0)
            return;

        var barTo = Math.Min(fireBarIndex + 80, _bars.Count - 1);
        var buffer = NearDZoneCancelRule.ZoneTouchBuffer(zoneHigh, zoneLow, isOb);
        var (bufLo, bufHi) = NearDZoneCancelRule.BufferTouchBand(zoneLow, zoneHigh, isBuy, isOb);

        var zoneKey = $"{ObjectPrefix}ZD|{scopeKey}";
        var zoneColor = isBuy
            ? Color.FromArgb(60, 220, 50, 50)
            : Color.FromArgb(60, 50, 200, 80);
        UpsertFilledRectangle(zoneKey, fireBarIndex, barTo, zoneLow, zoneHigh, zoneColor, thickness: 1);

        if (buffer > 0 && bufHi > bufLo)
        {
            var bufKey = $"{ObjectPrefix}ZDBUF|{scopeKey}";
            var bufColor = isOb
                ? Color.FromArgb(100, 255, 180, 60)
                : Color.FromArgb(100, 180, 255, 120);
            UpsertFilledRectangle(bufKey, fireBarIndex, barTo, bufLo, bufHi, bufColor, thickness: 1);

            var touchKey = $"{ObjectPrefix}ZDBUF|{scopeKey}|line";
            UpsertTrendLine(touchKey, fireBarIndex, barTo, isBuy ? bufLo : bufHi,
                isBuy ? Color.Khaki : Color.Gold, thickness: 2);
        }

        var labelKey = $"{ObjectPrefix}ZDLBL|{scopeKey}";
        var labelBar = Math.Min(fireBarIndex + 2, _bars.Count - 1);
        var labelPrice = isBuy ? bufLo : bufHi;
        var labelText = FormatNearDZoneDebugLabel(zoneLow, zoneHigh, buffer, isOb, tfToken, isBuy);
        DrawPivotLabel(labelKey, labelBar, labelPrice, isHighPivot: !isBuy, labelText, Color.Gold);
    }

    /// <inheritdoc cref="DrawNearDCancelZoneDebug"/>
    public void DrawNearDZone(
        string planLabel,
        int fireBarIndex,
        double zoneLow,
        double zoneHigh,
        bool isBuy) =>
        DrawNearDCancelZoneDebug(planLabel, fireBarIndex, zoneLow, zoneHigh, isBuy, isOb: false);

    public static string FormatNearDZoneDebugLabel(
        double zoneLow,
        double zoneHigh,
        double buffer,
        bool isOb,
        string? tfToken,
        bool isBuy)
    {
        var kind = isOb ? "OB" : "Key";
        var tf = string.IsNullOrWhiteSpace(tfToken) ? "?" : FormatNearDTfLabel(tfToken);
        var edge = isBuy ? "bot" : "top";
        return $"near-D {tf}/{kind}\n[{FmtPrice(zoneLow)}..{FmtPrice(zoneHigh)}]\nbuf@{edge}={FmtPrice(buffer)}";
    }

    static string FormatNearDTfLabel(string tfToken) => tfToken switch
    {
        "240"  => "H4",
        "60"   => "H1",
        "15"   => "M15",
        "5"    => "M5",
        "1440" => "D1",
        "1D"   => "D1",
        "D"    => "D1",
        _      => tfToken,
    };

    void UpsertFilledRectangle(
        string key,
        int barFrom,
        int barTo,
        double yLow,
        double yHigh,
        Color fill,
        int thickness)
    {
        if (_chart.FindObject(key) is ChartRectangle existing)
        {
            existing.Y1 = yLow;
            existing.Y2 = yHigh;
            existing.Color = fill;
            return;
        }

        var rect = _chart.DrawRectangle(key, barFrom, yLow, barTo, yHigh, fill);
        rect.IsFilled = true;
        rect.Thickness = thickness;
    }

    void UpsertTrendLine(
        string key,
        int barFrom,
        int barTo,
        double price,
        Color color,
        int thickness)
    {
        if (_chart.FindObject(key) is ChartTrendLine existing)
        {
            existing.Y1 = price;
            existing.Y2 = price;
            existing.Color = color;
            return;
        }

        var line = _chart.DrawTrendLine(key, barFrom, price, barTo, price, color);
        line.Thickness = thickness;
    }

    // ── Entry–SL support zone (zone gate result) ──────────────────────────────

    /// <summary>
    /// Draw the risk band [entry..SL] and, if the gate found a supporting zone, highlight it.
    /// </summary>
    public void DrawEntrySlZone(
        string planLabel,
        int fireBarIndex,
        double riskLow,
        double riskHigh,
        double? zoneLow,
        double? zoneHigh,
        bool passed,
        bool isBuy)
    {
        if (fireBarIndex < 0 || riskLow <= 0 || riskHigh <= 0)
            return;

        var barSpan  = Math.Min(fireBarIndex + 5, _bars.Count - 1);
        var passColor = isBuy ? Color.FromArgb(80, 50, 200, 80) : Color.FromArgb(80, 220, 50, 50);
        var failColor = Color.FromArgb(60, 180, 180, 0);

        // Risk band outline.
        var rskKey = $"{ObjectPrefix}RSK|{planLabel}";
        if (_chart.FindObject(rskKey) == null)
        {
            var rskRect = _chart.DrawRectangle(rskKey, fireBarIndex, riskLow, barSpan, riskHigh,
                passed ? passColor : failColor);
            rskRect.IsFilled  = false;
            rskRect.Thickness = 2;
        }

        // Matched zone fill (only when gate passed and zone is known).
        if (passed && zoneLow.HasValue && zoneHigh.HasValue)
        {
            var zKey = $"{ObjectPrefix}ESZ|{planLabel}";
            if (_chart.FindObject(zKey) == null)
            {
                var zRect = _chart.DrawRectangle(zKey, fireBarIndex, zoneLow.Value, barSpan, zoneHigh.Value,
                    passColor);
                zRect.IsFilled  = true;
                zRect.Thickness = 1;
            }
        }
    }

    static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..(maxLen - 1)] + "…";
}
