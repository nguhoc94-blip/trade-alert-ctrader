using System;
using System.Globalization;

namespace TradeAlert.BacktestRobot.Execution.Kl;

public enum KlAssetType
{
    Auto,
    Forex,
    XAUUSD,
    BTCUSD,
    US100,
    Custom,
}

/// <summary>Inputs mirroring Pine <c>kl_vao_lenh_ctrader_bid_spread_fixed</c>.</summary>
public sealed class KlEntryLotInput
{
    public KlAssetType AssetType { get; init; }
    public int CustomContractSize { get; init; } = 100_000;
    public double AccountBalance { get; init; }
    public double AccountBalanceFtmo { get; init; }
    public double RiskPercent { get; init; }
    /// <summary>0 = auto conversion lookup.</summary>
    public double ManualConvUsdPerQuote { get; init; }
    public double EntryPrice { get; init; }
    public double StopPrice { get; init; }
    public double Tp1 { get; init; }
    public double Tp2 { get; init; }
    public double SpreadPips { get; init; }
    public int RoundPrecision { get; init; } = 1000;
    public string SymbolName { get; init; } = "";
    public double TickSize { get; init; }
    /// <summary>cTrader <see cref="cAlgo.API.Internals.Symbol.PipSize"/> — preferred pip source.</summary>
    public double PipSize { get; init; }
    public bool IsCryptoSymbol { get; init; }
    public string QuoteCurrency { get; init; } = "USD";
    /// <summary>Base asset name for crypto lot label (e.g. BTC).</summary>
    public string BaseAssetName { get; init; } = "";
    /// <summary>Auto conv from host (null = lookup failed).</summary>
    public double? AutoConvToUsd { get; init; }
}

public sealed class KlEntryLotResult
{
    public double Pip { get; init; }
    public double ContractSize { get; init; }
    public string AssetUnit { get; init; } = "lot";
    public double SpreadPrice { get; init; }
    public bool ValidPriceInputs { get; init; }
    public bool ValidForLotCalc { get; init; }
    public bool? IsBuy { get; init; }
    public double? StopPips { get; init; }
    public double? PipValuePerLotQuote { get; init; }
    public double? ConvToUsd { get; init; }
    public double? PipValuePerLot { get; init; }
    public bool ConvWarn { get; init; }
    public int EffectiveRound { get; init; }
    public double? LotRaw { get; init; }
    public double? RoundDisplayed { get; init; }
    public double? PerLegDisplayed { get; init; }
    public double? EstLoss { get; init; }
    public double? PipsToTp1 { get; init; }
    public double? PipsToTp2 { get; init; }
    public double? PnlTp1 { get; init; }
    public double? PnlTp2 { get; init; }
    public double? EntryFtmo { get; init; }
    public double? SlFtmo { get; init; }
    public double? Tp1Ftmo { get; init; }
    public double? Tp2Ftmo { get; init; }
    public double AccForFtmo { get; init; }
    /// <summary>AccountBalanceFtmo × Risk% — target risk USD for FTMO lot sizing.</summary>
    public double? TargetRiskUsdFtmo { get; init; }
    public double? StopPipsFtmo { get; init; }
    public double? LotRawFtmo { get; init; }
    public double? RoundDisplayedFtmo { get; init; }
    public double? PerLegDisplayedFtmo { get; init; }
    public double? EstLossFtmo { get; init; }
    public double? PipsToTp1Ftmo { get; init; }
    public double? PipsToTp2Ftmo { get; init; }
    public double? PnlTp1Ftmo { get; init; }
    public double? PnlTp2Ftmo { get; init; }
}

/// <summary>Pine parity — Bid chart, full spread (no half-spread / spr2).</summary>
public static class KlEntryLotCalculator
{
    public static KlEntryLotResult Compute(in KlEntryLotInput input)
    {
        var pip = ResolvePipSize(
            input.SymbolName,
            input.TickSize,
            input.PipSize);

        var sym = input.SymbolName ?? "";
        var contractSize = ResolveContractSize(input, sym, input.IsCryptoSymbol);
        var assetUnit = ResolveAssetUnit(input, sym, input.IsCryptoSymbol);

        var entryPrice = input.EntryPrice;
        var stopPrice = input.StopPrice;
        var tp1 = input.Tp1;
        var tp2 = input.Tp2;
        var spreadPrice = input.SpreadPips * pip;

        var validPriceInputs = entryPrice != 0 && stopPrice != 0 && entryPrice != stopPrice;
        var validForLotCalc = validPriceInputs && input.AccountBalance > 0 && input.RiskPercent > 0;

        bool? isBuy = validPriceInputs ? stopPrice < entryPrice : null;
        double? stopPips = validPriceInputs ? Math.Abs(entryPrice - stopPrice) / pip : null;

        var pipValuePerLotQuote = pip * contractSize;
        var (convToUsd, convWarn) = ResolveConvToUsd(input);
        double? pipValuePerLot = convToUsd.HasValue
            ? pipValuePerLotQuote * convToUsd.Value
            : IsUsdQuote(input.QuoteCurrency)
                ? pipValuePerLotQuote
                : null;
        if (pipValuePerLot is <= 0)
            pipValuePerLot = null;

        var effectiveRound = input.IsCryptoSymbol && input.RoundPrecision < 100_000
            ? 100_000
            : input.RoundPrecision;

        double? lotRaw = null;
        if (validForLotCalc && stopPips is > 0 && pipValuePerLot is > 0)
            lotRaw = input.AccountBalance * (input.RiskPercent / 100.0) / (stopPips.Value * pipValuePerLot.Value);

        var roundDisplayed = RoundLot(lotRaw, effectiveRound);
        var perLegDisplayed = RoundLot(roundDisplayed.HasValue ? roundDisplayed / 2.0 : null, effectiveRound);

        double? estLoss = roundDisplayed.HasValue && stopPips.HasValue && pipValuePerLot.HasValue
            ? roundDisplayed * stopPips * pipValuePerLot
            : null;

        double? pipsToTp1 = tp1 == 0 ? null : Math.Abs(tp1 - entryPrice) / pip;
        double? pipsToTp2 = tp2 == 0 ? null : Math.Abs(tp2 - entryPrice) / pip;

        double? pnlTp1 = perLegDisplayed.HasValue && pipsToTp1.HasValue && pipValuePerLot.HasValue
            ? pipsToTp1 * pipValuePerLot * perLegDisplayed
            : null;
        double? pnlTp2 = perLegDisplayed.HasValue && pipsToTp2.HasValue && pipValuePerLot.HasValue
            ? pipsToTp2 * pipValuePerLot * perLegDisplayed
            : null;

        // FTMO — full spread on Bid chart (no spr2 / half-spread)
        double? entryFtmo = null, slFtmo = null, tp1Ftmo = null, tp2Ftmo = null;
        if (validPriceInputs && isBuy == true)
        {
            entryFtmo = entryPrice + spreadPrice;
            slFtmo = stopPrice;
            tp1Ftmo = tp1 == 0 ? null : tp1;
            tp2Ftmo = tp2 == 0 ? null : tp2;
        }
        else if (validPriceInputs && isBuy == false)
        {
            entryFtmo = entryPrice;
            slFtmo = stopPrice + spreadPrice;
            tp1Ftmo = tp1 == 0 ? null : tp1 + spreadPrice;
            tp2Ftmo = tp2 == 0 ? null : tp2 + spreadPrice;
        }

        // FTMO — always AccountBalanceFtmo only (never fallback to mid account)
        var accForFtmo = input.AccountBalanceFtmo;
        var targetRiskUsdFtmo = accForFtmo > 0 && input.RiskPercent > 0
            ? accForFtmo * (input.RiskPercent / 100.0)
            : (double?)null;
        var validForLotCalcFtmo = validPriceInputs
                                  && accForFtmo > 0
                                  && input.RiskPercent > 0
                                  && pipValuePerLot is > 0;

        double? stopPipsFtmo = entryFtmo.HasValue && slFtmo.HasValue
            ? Math.Abs(entryFtmo.Value - slFtmo.Value) / pip
            : null;

        double? lotRawFtmo = null;
        if (validForLotCalcFtmo && stopPipsFtmo is > 0)
            lotRawFtmo = accForFtmo * (input.RiskPercent / 100.0) / (stopPipsFtmo.Value * pipValuePerLot!.Value);

        var roundDisplayedFtmo = RoundLot(lotRawFtmo, effectiveRound);
        var perLegDisplayedFtmo = RoundLot(roundDisplayedFtmo.HasValue ? roundDisplayedFtmo / 2.0 : null, effectiveRound);

        double? estLossFtmo = roundDisplayedFtmo.HasValue && stopPipsFtmo.HasValue && pipValuePerLot.HasValue
            ? roundDisplayedFtmo * stopPipsFtmo * pipValuePerLot
            : null;

        double? pipsToTp1Ftmo = tp1Ftmo.HasValue && entryFtmo.HasValue
            ? Math.Abs(tp1Ftmo.Value - entryFtmo.Value) / pip
            : null;
        double? pipsToTp2Ftmo = tp2Ftmo.HasValue && entryFtmo.HasValue
            ? Math.Abs(tp2Ftmo.Value - entryFtmo.Value) / pip
            : null;

        double? pnlTp1Ftmo = perLegDisplayedFtmo.HasValue && pipsToTp1Ftmo.HasValue && pipValuePerLot.HasValue
            ? pipsToTp1Ftmo * pipValuePerLot * perLegDisplayedFtmo
            : null;
        double? pnlTp2Ftmo = perLegDisplayedFtmo.HasValue && pipsToTp2Ftmo.HasValue && pipValuePerLot.HasValue
            ? pipsToTp2Ftmo * pipValuePerLot * perLegDisplayedFtmo
            : null;

        return new KlEntryLotResult
        {
            Pip = pip,
            ContractSize = contractSize,
            AssetUnit = assetUnit,
            SpreadPrice = spreadPrice,
            ValidPriceInputs = validPriceInputs,
            ValidForLotCalc = validForLotCalc,
            IsBuy = isBuy,
            StopPips = stopPips,
            PipValuePerLotQuote = pipValuePerLotQuote,
            ConvToUsd = convToUsd,
            PipValuePerLot = pipValuePerLot,
            ConvWarn = convWarn,
            EffectiveRound = effectiveRound,
            LotRaw = lotRaw,
            RoundDisplayed = roundDisplayed,
            PerLegDisplayed = perLegDisplayed,
            EstLoss = estLoss,
            PipsToTp1 = pipsToTp1,
            PipsToTp2 = pipsToTp2,
            PnlTp1 = pnlTp1,
            PnlTp2 = pnlTp2,
            EntryFtmo = entryFtmo,
            SlFtmo = slFtmo,
            Tp1Ftmo = tp1Ftmo,
            Tp2Ftmo = tp2Ftmo,
            AccForFtmo = accForFtmo,
            TargetRiskUsdFtmo = targetRiskUsdFtmo,
            StopPipsFtmo = stopPipsFtmo,
            LotRawFtmo = lotRawFtmo,
            RoundDisplayedFtmo = roundDisplayedFtmo,
            PerLegDisplayedFtmo = perLegDisplayedFtmo,
            EstLossFtmo = estLossFtmo,
            PipsToTp1Ftmo = pipsToTp1Ftmo,
            PipsToTp2Ftmo = pipsToTp2Ftmo,
            PnlTp1Ftmo = pnlTp1Ftmo,
            PnlTp2Ftmo = pnlTp2Ftmo,
        };
    }

    static double ResolveContractSize(in KlEntryLotInput input, string sym, bool isCrypto)
    {
        if (isCrypto)
            return 1;

        return input.AssetType switch
        {
            KlAssetType.Forex => 100_000,
            KlAssetType.XAUUSD => 100,
            KlAssetType.BTCUSD => 1,
            KlAssetType.US100 => 1,
            KlAssetType.Custom => Math.Max(1, input.CustomContractSize),
            KlAssetType.Auto => sym.Contains("XAU", StringComparison.OrdinalIgnoreCase)
                                || sym.Contains("GOLD", StringComparison.OrdinalIgnoreCase)
                ? 100
                : 100_000,
            _ => 100_000,
        };
    }

    static string ResolveAssetUnit(in KlEntryLotInput input, string sym, bool isCrypto)
    {
        if (isCrypto)
        {
            if (!string.IsNullOrWhiteSpace(input.BaseAssetName))
                return input.BaseAssetName.Trim();
            if (input.AssetType == KlAssetType.BTCUSD)
                return "BTC";
            return "unit";
        }

        return input.AssetType switch
        {
            KlAssetType.BTCUSD => "BTC",
            KlAssetType.XAUUSD => "oz",
            KlAssetType.US100 => "contract",
            KlAssetType.Forex => "lot",
            KlAssetType.Custom => "unit",
            KlAssetType.Auto => sym.Contains("XAU", StringComparison.OrdinalIgnoreCase)
                                 || sym.Contains("GOLD", StringComparison.OrdinalIgnoreCase)
                ? "oz"
                : "lot",
            _ => "lot",
        };
    }

    static (double? conv, bool warn) ResolveConvToUsd(in KlEntryLotInput input)
    {
        var quote = (input.QuoteCurrency ?? "USD").Trim().ToUpperInvariant();

        if (input.ManualConvUsdPerQuote > 0)
            return (input.ManualConvUsdPerQuote, false);

        if (quote is "USD" or "USDT" or "USDC")
            return (1.0, false);

        if (input.AutoConvToUsd is > 0)
            return (input.AutoConvToUsd, false);

        // Non-USD quote without conv → cannot size in USD; do not treat quote pip value as USD.
        if (quote is not ("USD" or "USDT" or "USDC"))
            return (null, true);

        return (null, false);
    }

    static bool IsUsdQuote(string? quote)
    {
        var q = (quote ?? "USD").Trim().ToUpperInvariant();
        return q is "USD" or "USDT" or "USDC";
    }

    static double? RoundLot(double? lotRaw, int precision)
    {
        if (!lotRaw.HasValue || precision <= 0)
            return null;
        return Math.Round(lotRaw.Value * precision, MidpointRounding.AwayFromZero) / precision;
    }

    public static string FormatLot(double? lot, int effectiveRound, string assetUnit)
    {
        if (!lot.HasValue)
            return "-";
        if (lot == 0)
        {
            var minVisible = 1.0 / effectiveRound;
            return $"<{minVisible.ToString("0.######", CultureInfo.InvariantCulture)} {assetUnit}";
        }

        return $"{lot.Value.ToString("0.######", CultureInfo.InvariantCulture)} {assetUnit}";
    }

    public static string FormatPrice(double price, int digits) =>
        price == 0 ? "-" : price.ToString($"F{digits}", CultureInfo.InvariantCulture);

    public static string FormatNum(double? v, string format = "0.##") =>
        v.HasValue ? v.Value.ToString(format, CultureInfo.InvariantCulture) : "-";

    /// <summary>Reward:Risk = TP pips / SL pips.</summary>
    public static double? RewardToRisk(double? tpPips, double? slPips)
    {
        if (!tpPips.HasValue || !slPips.HasValue || slPips.Value <= 0)
            return null;
        return tpPips.Value / slPips.Value;
    }

    public static string FormatRewardRatio(double? rr) =>
        rr.HasValue ? rr.Value.ToString("0.##", CultureInfo.InvariantCulture) : "-";

    public readonly record struct KlDefaultSeedDistances(int SlPips, int Tp1Pips, int Tp2Pips);

    /// <summary>Pip size — ưu tiên <c>Symbol.PipSize</c> (cTrader), fallback JPY/tick.</summary>
    public static double ResolvePipSize(string symbolName, double tickSize, double pipSize = 0)
    {
        if (pipSize > 0)
            return pipSize;

        var sym = symbolName ?? "";
        if (sym.Contains("JPY", StringComparison.OrdinalIgnoreCase))
            return 0.01;

        return Math.Max(tickSize, 0.0001);
    }

    /// <summary>Spread price (Ask − Bid) → cTrader pips using symbol pip size.</summary>
    public static double ResolveAutoSpreadPips(double spreadPrice, double pipSize) =>
        pipSize > 0 && spreadPrice >= 0
            ? spreadPrice / pipSize
            : 0;

    /// <summary>
    /// Khoảng cách seed SL/TP từ ATR (price) × multiplier → pips.
    /// Override từng leg khi tham số pips &gt; 0.
    /// </summary>
    public static KlDefaultSeedDistances ResolveDefaultSeedDistancesFromAtr(
        double atrPrice,
        double pip,
        double slAtrMult = 1.0,
        double tp1AtrMult = 2.0,
        double tp2AtrMult = 4.0,
        int overrideSlPips = 0,
        int overrideTp1Pips = 0,
        int overrideTp2Pips = 0)
    {
        var auto = ComputeAtrSeedDistances(atrPrice, pip, slAtrMult, tp1AtrMult, tp2AtrMult);
        return new KlDefaultSeedDistances(
            overrideSlPips > 0 ? overrideSlPips : auto.SlPips,
            overrideTp1Pips > 0 ? overrideTp1Pips : auto.Tp1Pips,
            overrideTp2Pips > 0 ? overrideTp2Pips : auto.Tp2Pips);
    }

    public static KlDefaultSeedDistances ComputeAtrSeedDistances(
        double atrPrice,
        double pip,
        double slAtrMult,
        double tp1AtrMult,
        double tp2AtrMult)
    {
        if (atrPrice <= 0 || pip <= 0)
            return new KlDefaultSeedDistances(50, 100, 200);

        return new KlDefaultSeedDistances(
            PipsFromAtr(atrPrice, pip, slAtrMult),
            PipsFromAtr(atrPrice, pip, tp1AtrMult),
            PipsFromAtr(atrPrice, pip, tp2AtrMult));
    }

    static int PipsFromAtr(double atrPrice, double pip, double multiplier)
    {
        if (atrPrice <= 0 || pip <= 0 || multiplier <= 0)
            return 50;

        var pips = (int)Math.Round(atrPrice * multiplier / pip, MidpointRounding.AwayFromZero);
        return Math.Clamp(Math.Max(1, pips), 5, 10_000);
    }

    public static bool IsBuyTrade(double entry, double stop) => stop < entry;

    /// <summary>
    /// Bid-chart levels with spread baked into draggable lines (inverse of Pine bid inputs).
    /// Buy: entry += spread. Sell: sl/tp += spread.
    /// </summary>
    public static void ForwardSpreadOnBidLevels(
        ref double entry, ref double stop, ref double tp1, ref double tp2,
        double spreadPips, double pip, bool isBuy)
    {
        var spr = spreadPips * pip;
        if (spr == 0)
            return;

        if (isBuy)
            entry += spr;
        else
        {
            stop += spr;
            if (tp1 != 0)
                tp1 += spr;
            if (tp2 != 0)
                tp2 += spr;
        }
    }

    /// <summary>Extract Pine bid inputs from spread-adjusted line positions.</summary>
    public static void ReverseSpreadOnBidLevels(
        ref double entry, ref double stop, ref double tp1, ref double tp2,
        double spreadPips, double pip, bool isBuy)
    {
        var spr = spreadPips * pip;
        if (spr == 0)
            return;

        if (isBuy)
            entry -= spr;
        else
        {
            stop -= spr;
            if (tp1 != 0)
                tp1 -= spr;
            if (tp2 != 0)
                tp2 -= spr;
        }
    }
}
