using TradeAlert.BacktestRobot.Execution.Kl;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>Runtime inputs for re-validating a limit plan at the actual MARKET fill price.</summary>
public sealed class MarketFillValidationParams
{
    public bool EnableMinSlConstraint { get; init; }
    public double BaseRewardRisk { get; init; }
    public bool SkipIfSwingCRrBelowBase { get; init; }
    public bool MoveEntryIfSwingCRrBelowBase { get; init; }
    public bool EnableSpreadGate { get; init; }
    public double MaxSpreadGate { get; init; }
    public bool SpreadGateIsPriceMode { get; init; }
    public double SpreadGateValue { get; init; }

    public KlAssetType AssetType { get; init; }
    public double CustomContractSize { get; init; }
    public double AccountBalanceFtmo { get; init; }
    public double RiskPercentForLeg { get; init; }
    public double ConvUsdPerQuote { get; init; }
    public double SpreadPips { get; init; }
    public int RoundPrecision { get; init; }
    public string SymbolName { get; init; } = "";
    public double TickSize { get; init; }
    public double PipSize { get; init; }
    public bool IsCryptoSymbol { get; init; }
    public string QuoteCurrency { get; init; } = "USD";
    public string BaseAssetName { get; init; } = "";
}

public sealed class MarketFillValidationResult
{
    public bool Allowed { get; init; }
    public string RejectReason { get; init; } = "";
    public double FillPrice { get; init; }
    public double SlPipsAtFill { get; init; }
    public double TpPipsAtFill { get; init; }
    public double RrAtFill { get; init; }
    public double? LotFtmoAtFill { get; init; }
}
