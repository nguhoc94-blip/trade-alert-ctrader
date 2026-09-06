using System;
using System.Collections.Generic;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Series;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>
/// R1/R2 on M15: swing B stays on chart TF (M5 fallback optional); swing C resolves on M5;
/// D scans M5/M15/H1/H4; entry OB uses chart TF + M5 auxiliary via cross-TF context.
/// </summary>
public static class R1R2M5Structure
{
    /// <summary>True when R1/R2 should resolve swing C on the M5 engine (chart remains M15).</summary>
    public static bool UsesM5SwingC(int slotIndex, in TradeMapperConfig cfg) =>
        SwingBResolver.IsR1R2Slot(slotIndex)
        && string.Equals(cfg.ChartTfToken, "15", StringComparison.Ordinal)
        && cfg.M5FallbackState is not null
        && cfg.M5FallbackBuffer is not null
        && cfg.ChartSeriesBuffer is not null;

    /// <summary>M5 bar used as "after B" anchor when resolving swing C on M5.</summary>
    public static int ResolveM5SwingBBarForC(in SwingBResult b, in TradeMapperConfig cfg)
    {
        if (b.Source == SwingBSource.M5AnchoredFallback)
            return b.StructurePivotBar;

        if (cfg.M5FallbackBuffer is null || cfg.ChartSeriesBuffer is null)
            return b.PivotBar;

        if (SwingBResolver.TryFindLatestM5BarInChartBar(
                b.PivotBar, cfg.ChartSeriesBuffer, cfg.M5FallbackBuffer, cfg.ChartTfToken, out var m5Bar))
            return m5Bar;

        return b.PivotBar;
    }

    public static SwingCEdgeResult AnchorEdgePivotToChart(SwingCEdgeResult edge, in TradeMapperConfig cfg)
    {
        if (cfg.M5FallbackBuffer is null || cfg.ChartSeriesBuffer is null)
            return edge;

        if (!SwingBResolver.TryAnchorM5PivotBarToChart(
                edge.PivotBar, cfg.M5FallbackBuffer, cfg.ChartSeriesBuffer, cfg.ChartTfToken, out var chartBar))
            return edge;

        return new SwingCEdgeResult
        {
            PivotIndex = edge.PivotIndex,
            PivotBar = chartBar,
            PivotType = edge.PivotType,
            TakeProfit = edge.TakeProfit,
            EdgeTop = edge.EdgeTop,
            EdgeBottom = edge.EdgeBottom,
            EdgeKind = edge.EdgeKind,
            IsOb = edge.IsOb,
            TfToken = cfg.ChartTfToken,
        };
    }

    public static PineStateEngine ResolveMonitorState(
        in PendingOrderContext ctx,
        PineStateEngine chartState,
        PineStateEngine? m5State) =>
        ctx.UsesM5SwingC && m5State is not null ? m5State : chartState;

    public static int ResolveMonitorSwingBBar(in PendingOrderContext ctx) =>
        ctx.UsesM5SwingC && ctx.StructureSwingBBar > 0 ? ctx.StructureSwingBBar : ctx.SwingBPivotBar;

    public static bool IsSwingBBroken(
        bool swingBFromM5Anchor,
        int structureSwingBBar,
        int chartSwingBPivotBar,
        PineStateEngine chartState,
        PineStateEngine? m5State)
    {
        if (swingBFromM5Anchor && structureSwingBBar > 0 && m5State is not null)
            return SwingBrokenChecker.IsBroken(m5State, structureSwingBBar);
        return chartSwingBPivotBar > 0 && SwingBrokenChecker.IsBroken(chartState, chartSwingBPivotBar);
    }
}
