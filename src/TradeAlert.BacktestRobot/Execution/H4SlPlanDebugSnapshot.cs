using System;
using System.Collections.Generic;
using TradeAlert.Core.Models.Alerts;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>Last resolved trade plan levels for H4 SL debug panel + chart overlay.</summary>
public readonly struct H4SlPlanDebugSnapshot
{
    public bool HasData { get; init; }
    public string DirectionLabel { get; init; }
    public int ChartBar { get; init; }
    public int SlotIndex { get; init; }
    public double Entry { get; init; }
    public double StopLoss { get; init; }
    public double TpC { get; init; }
    public double TpD { get; init; }
    public double SlPips { get; init; }
    public double TpCPips { get; init; }
    public double TpDPips { get; init; }
    public bool IsSplit { get; init; }

    public static H4SlPlanDebugSnapshot FromPlans(IReadOnlyList<TradePlan> plans, in CompoundFireEvent ev)
    {
        if (plans.Count == 0)
            return default;

        var p0 = plans[0];
        var tpC = 0.0;
        var tpD = 0.0;
        var tpCPips = 0.0;
        var tpDPips = 0.0;

        foreach (var p in plans)
        {
            if (string.Equals(p.TpLegTag, "C", StringComparison.Ordinal))
            {
                tpC = p.TakeProfit;
                tpCPips = p.TakeProfitPips;
            }
            else if (string.Equals(p.TpLegTag, "D", StringComparison.Ordinal))
            {
                tpD = p.TakeProfit;
                tpDPips = p.TakeProfitPips;
            }
        }

        var isSplit = plans.Count > 1;
        if (tpC <= 0 && p0.TpCLegPrice > 0)
        {
            tpC = p0.TpCLegPrice;
            tpCPips = p0.TakeProfitPips;
        }

        if (tpC <= 0 && !isSplit)
        {
            tpC = p0.TakeProfit;
            tpCPips = p0.TakeProfitPips;
        }

        return new H4SlPlanDebugSnapshot
        {
            HasData        = true,
            DirectionLabel = p0.IsBuy ? "BUY" : "SELL",
            ChartBar       = ev.ChartBarIndex,
            SlotIndex      = ev.SlotIndex,
            Entry          = p0.EntryLimit,
            StopLoss       = p0.StopLoss,
            TpC            = tpC,
            TpD            = tpD,
            SlPips         = p0.StopLossPips,
            TpCPips        = tpCPips,
            TpDPips        = tpDPips,
            IsSplit        = isSplit,
        };
    }
}
