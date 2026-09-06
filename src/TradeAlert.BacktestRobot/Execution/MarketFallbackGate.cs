using System.Globalization;
using TradeAlert.BacktestRobot.Execution.Kl;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>
/// Re-validates limit plans at the actual MARKET fill (bid/ask) before placement:
/// geometry, min-SL, spread, RR at TP, and lot sizing from fill→SL distance.
/// </summary>
public static class MarketFallbackGate
{
    public static double MarketEntryPrice(in TradePlan plan, double bid, double ask) =>
        plan.IsBuy ? ask : bid;

    /// <summary>
    /// Full validation at market fill. Replaces the older AllowsMarketFallback-only check.
    /// </summary>
    public static bool TryValidateMarketFill(
        in TradePlan plan,
        double bid,
        double ask,
        in MarketFillValidationParams p,
        out MarketFillValidationResult result)
    {
        var fill = MarketEntryPrice(in plan, bid, ask);
        var pip = p.PipSize;
        var sl = plan.StopLoss;
        var tp = plan.TakeProfit;

        if (plan.IsBuy)
        {
            if (!(sl < fill && fill < tp))
            {
                result = Reject(fill, $"inconsistent BUY levels fill={Fmt(fill)} sl={Fmt(sl)} tp={Fmt(tp)}");
                return false;
            }
        }
        else
        {
            if (!(tp < fill && fill < sl))
            {
                result = Reject(fill, $"inconsistent SELL levels fill={Fmt(fill)} sl={Fmt(sl)} tp={Fmt(tp)}");
                return false;
            }
        }

        var slPips = pip > 0 ? Math.Round(Math.Abs(fill - sl) / pip, 2) : 0;
        var tpPips = pip > 0 ? Math.Round(Math.Abs(tp - fill) / pip, 2) : 0;

        if (p.EnableSpreadGate && p.MaxSpreadGate > 0)
        {
            var spreadVal = p.SpreadGateValue;
            var unit = p.SpreadGateIsPriceMode ? "price" : "pip";
            if (spreadVal > p.MaxSpreadGate)
            {
                result = Reject(fill,
                    $"spread gate {unit}={spreadVal.ToString("0.####", CultureInfo.InvariantCulture)} " +
                    $"> max={p.MaxSpreadGate.ToString("0.####", CultureInfo.InvariantCulture)} at market fill");
                return false;
            }
        }

        if (p.EnableMinSlConstraint && plan.MinSlFloorPips > 0 && slPips < plan.MinSlFloorPips)
        {
            result = Reject(fill,
                $"min-SL at fill {slPips.ToString("0.##", CultureInfo.InvariantCulture)}p " +
                $"< floor {plan.MinSlFloorPips.ToString("0.##", CultureInfo.InvariantCulture)}p " +
                $"(planned {plan.StopLossPips.ToString("0.##", CultureInfo.InvariantCulture)}p @ limit {Fmt(plan.EntryLimit)})");
            return false;
        }

        if (slPips <= 0)
        {
            result = Reject(fill, "zero SL distance at market fill");
            return false;
        }

        var rrAtFill = SwingCEdgeTakeProfitResolver.ComputeEdgeRr(tp, fill, sl, plan.IsBuy);
        var baseRr = p.BaseRewardRisk <= 0 ? 2.0 : p.BaseRewardRisk;

        if (RequiresRrCheck(in plan, p)
            && SwingCEdgeTakeProfitResolver.IsEdgeRrBelowBase(tp, fill, sl, plan.IsBuy, baseRr))
        {
            result = Reject(fill,
                $"RR at fill {rrAtFill.ToString("0.##", CultureInfo.InvariantCulture)} " +
                $"< base {baseRr.ToString("0.##", CultureInfo.InvariantCulture)} " +
                $"(tp={Fmt(tp)} fill={Fmt(fill)})");
            return false;
        }

        var lot = RecalcLotFtmo(fill, sl, tp, in plan, in p);
        if (lot is not > 0)
        {
            result = Reject(fill, "lot recalc at market fill failed (conv/balance?)");
            return false;
        }

        result = new MarketFillValidationResult
        {
            Allowed = true,
            FillPrice = fill,
            SlPipsAtFill = slPips,
            TpPipsAtFill = tpPips,
            RrAtFill = rrAtFill,
            LotFtmoAtFill = lot,
        };
        return true;
    }

    static bool RequiresRrCheck(in TradePlan plan, in MarketFillValidationParams _) =>
        plan.TakeProfit > 0;

    static double? RecalcLotFtmo(double fill, double sl, double tp, in TradePlan plan, in MarketFillValidationParams p)
    {
        var klInput = new KlEntryLotInput
        {
            AssetType = p.AssetType,
            CustomContractSize = p.CustomContractSize <= 0 ? 100_000 : (int)p.CustomContractSize,
            AccountBalance = p.AccountBalanceFtmo,
            AccountBalanceFtmo = p.AccountBalanceFtmo,
            RiskPercent = p.RiskPercentForLeg,
            ManualConvUsdPerQuote = p.ConvUsdPerQuote,
            EntryPrice = fill,
            StopPrice = sl,
            Tp1 = tp,
            Tp2 = 0,
            SpreadPips = p.SpreadPips,
            RoundPrecision = p.RoundPrecision <= 0 ? 1000 : p.RoundPrecision,
            SymbolName = p.SymbolName,
            TickSize = p.TickSize,
            PipSize = p.PipSize,
            IsCryptoSymbol = p.IsCryptoSymbol,
            QuoteCurrency = string.IsNullOrWhiteSpace(p.QuoteCurrency) ? "USD" : p.QuoteCurrency,
            BaseAssetName = p.BaseAssetName ?? "",
            AutoConvToUsd = p.ConvUsdPerQuote > 0 ? p.ConvUsdPerQuote : null,
        };

        var kl = KlEntryLotCalculator.Compute(in klInput);
        return kl.RoundDisplayedFtmo is > 0 ? kl.RoundDisplayedFtmo : null;
    }

    static MarketFillValidationResult Reject(double fill, string reason) => new()
    {
        Allowed = false,
        RejectReason = reason,
        FillPrice = fill,
    };

    static string Fmt(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

    public static double SlPipsFromEntry(double entry, double sl, double pipSize) =>
        pipSize > 0 ? Math.Round(Math.Abs(entry - sl) / pipSize, 1) : 0;

    public static double TpPipsFromEntry(double entry, double tp, double pipSize) =>
        pipSize > 0 ? Math.Round(Math.Abs(tp - entry) / pipSize, 1) : 0;
}
