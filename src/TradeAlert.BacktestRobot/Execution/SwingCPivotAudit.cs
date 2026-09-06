using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.Alerts;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>
/// Logs every chart-TF pivot of the swing-C type strictly after pinned swing B,
/// so deferred-split / fire decisions can be compared to the C pivot actually picked.
/// </summary>
public static class SwingCPivotAudit
{
    public readonly struct Row
    {
        public int PivotIndex { get; init; }
        public int Bar { get; init; }
        public double Price { get; init; }
        public int Flag { get; init; }
        public string FlagLabel { get; init; }
        public bool ValidCCandidate { get; init; }
    }

    public static IReadOnlyList<Row> CollectAfterB(
        PineStateEngine state,
        SwingBResult? pinnedB,
        SignalDirection direction,
        int slotIndex,
        in TradeMapperConfig cfg)
    {
        if (pinnedB is null || pinnedB.PivotBar <= 0)
            return Array.Empty<Row>();

        var isBuy = direction == SignalDirection.Buy;
        var usesM5SwingC = R1R2M5Structure.UsesM5SwingC(slotIndex, in cfg);
        var swingState = usesM5SwingC && cfg.M5FallbackState is not null ? cfg.M5FallbackState : state;
        var swingBBar = usesM5SwingC
            ? R1R2M5Structure.ResolveM5SwingBBarForC(in pinnedB, in cfg)
            : pinnedB.PivotBar;
        var wantType = isBuy ? SwingBResolver.TypeHigh : SwingBResolver.TypeLow;
        var pivots = swingState.Pivots;

        var rows = new List<Row>();
        for (var i = 0; i < pivots.Count; i++)
        {
            var snap = pivots.GetSnapshot(i);
            if (snap.BarIndex <= swingBBar || snap.Type != wantType)
                continue;

            var flag = pivots.GetFlag(i);
            rows.Add(new Row
            {
                PivotIndex = i,
                Bar = snap.BarIndex,
                Price = snap.Price,
                Flag = flag,
                FlagLabel = SwingCWaitDiagnostic.FormatPivotFlagPublic(flag),
                ValidCCandidate = SwingCEdgeTakeProfitResolver.IsValidCEntryCandidate(pivots, i),
            });
        }

        rows.Sort((a, b) => a.Bar.CompareTo(b.Bar));
        return rows;
    }

    public static string FormatLogBlock(
        PineStateEngine state,
        SwingBResult? pinnedB,
        SignalDirection direction,
        int slotIndex,
        in TradeMapperConfig cfg,
        int ruleSlot,
        int fireBar,
        int chartBar,
        string phase,
        int? pickedCBar = null,
        string? buildReason = null)
    {
        if (pinnedB is null || pinnedB.PivotBar <= 0)
        {
            return $"[L6BT] SWING-C-AUDIT R{ruleSlot + 1} {Dir(direction)} chart={chartBar} fire={fireBar} " +
                   $"phase={phase} B=missing picked={FmtPicked(pickedCBar)}";
        }

        var isBuy = direction == SignalDirection.Buy;
        var usesM5SwingC = R1R2M5Structure.UsesM5SwingC(slotIndex, in cfg);
        var swingState = usesM5SwingC && cfg.M5FallbackState is not null ? cfg.M5FallbackState : state;
        var swingBBar = usesM5SwingC
            ? R1R2M5Structure.ResolveM5SwingBBarForC(in pinnedB, in cfg)
            : pinnedB.PivotBar;
        var tfNote = usesM5SwingC ? " swingTF=M5" : " swingTF=chart";
        var rows = CollectAfterB(state, pinnedB, direction, slotIndex, in cfg);
        var probe = SwingCWaitDiagnostic.ProbeAfterBPublic(swingState.Pivots, swingBBar, isBuy);

        var awaiting = SwingCWaitDiagnostic.IsAwaitingSwingCConfirmation(
            state, pinnedB, direction, slotIndex, in cfg);

        var sb = new StringBuilder();
        sb.Append("[L6BT] SWING-C-AUDIT R").Append(ruleSlot + 1).Append(' ').Append(Dir(direction))
            .Append(" chart=").Append(chartBar)
            .Append(" fire=").Append(fireBar)
            .Append(" B@").Append(swingBBar)
            .Append(tfNote)
            .Append(" phase=").Append(phase)
            .Append(" awaiting=").Append(awaiting ? 'Y' : 'N')
            .Append(" picked=").Append(FmtPicked(pickedCBar))
            .Append(" nearestAny=").Append(probe.NearestAnyBar >= 0
                ? $"bar={probe.NearestAnyBar} {probe.NearestAnyFlagLabel}"
                : "none")
            .Append(" nearestValid=").Append(probe.ValidConfirmedBar >= 0
                ? $"bar={probe.ValidConfirmedBar}"
                : "none");

        if (!string.IsNullOrWhiteSpace(buildReason))
            sb.Append(" why=\"").Append(Truncate(buildReason, 72)).Append('"');

        if (rows.Count == 0)
        {
            sb.Append('\n').Append("  (no ").Append(isBuy ? "HIGH" : "LOW")
                .Append(" pivot after B@").Append(swingBBar).Append(')');
            return sb.ToString();
        }

        var firstValidBar = -1;
        foreach (var row in rows)
        {
            if (row.ValidCCandidate && (firstValidBar < 0 || row.Bar < firstValidBar))
                firstValidBar = row.Bar;
        }

        foreach (var row in rows)
        {
            sb.Append('\n').Append("  bar=").Append(row.Bar)
                .Append(" idx=").Append(row.PivotIndex)
                .Append(" price=").Append(FmtPrice(row.Price))
                .Append(" flag=").Append(row.FlagLabel).Append('(').Append(row.Flag).Append(')')
                .Append(" validC=").Append(row.ValidCCandidate ? 'Y' : 'N');

            if (pickedCBar.HasValue && row.Bar == pickedCBar.Value)
                sb.Append(" <<PICKED");
            else if (firstValidBar == row.Bar && !pickedCBar.HasValue)
                sb.Append(" <<wouldPick");
        }

        return sb.ToString();
    }

    static string Dir(SignalDirection direction) =>
        direction == SignalDirection.Buy ? "BUY"
        : direction == SignalDirection.Sell ? "SELL"
        : direction.ToString();

    static string FmtPicked(int? bar) =>
        bar is > 0 ? bar.Value.ToString(CultureInfo.InvariantCulture) : "none";

    static string FmtPrice(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

    static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..(maxLen - 1)] + "…";
}
