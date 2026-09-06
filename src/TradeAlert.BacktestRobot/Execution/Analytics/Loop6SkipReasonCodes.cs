namespace TradeAlert.BacktestRobot.Execution.Analytics;

public static class Loop6SkipReasonCodes
{
    public const string OutsideTradingHours = "OUTSIDE_TRADING_HOURS";
    public const string EntryCutoffBangkok = "ENTRY_CUTOFF_BANGKOK";
    public const string SpreadAboveMax = "SPREAD_ABOVE_MAX";
    public const string SpreadToSlTooHigh = "SPREAD_TO_SL_TOO_HIGH";
    public const string SlBelowMin = "SL_BELOW_MIN";
    public const string RrBelowMin = "RR_BELOW_MIN";
    public const string LevelInvalid = "LEVEL_INVALID";
    public const string SignalInvalid = "SIGNAL_INVALID";
    public const string OrderDistanceInvalid = "ORDER_DISTANCE_INVALID";
    public const string CorrelationCap = "CORRELATION_CAP";
    public const string PortfolioRiskCap = "PORTFOLIO_RISK_CAP";
    public const string NewsBlock        = "NEWS_BLOCK";
    public const string NewsBlockCpi     = "NEWS_BLOCK_CPI";
    public const string NewsBlockNfp     = "NEWS_BLOCK_NFP";
    public const string NewsForceClose   = "NEWS_FORCE_CLOSE";
    public const string NewsForceCloseCpi = "NEWS_FORCE_CLOSE_CPI";
    public const string NewsForceCloseNfp = "NEWS_FORCE_CLOSE_NFP";
    public const string NewsApiError     = "NEWS_API_ERROR";
    public const string NewsCacheStale   = "NEWS_CACHE_STALE";
    public const string FridayFlatBlock  = "FRIDAY_FLAT_BLOCK";
    public const string FridayFlatClose  = "FRIDAY_FLAT_CLOSE";
    public const string ZoneGate = "ZONE_GATE";
    public const string Dedup = "DEDUP";
    public const string DryRun = "DRY_RUN";
    public const string DeferredSplit = "DEFERRED_SPLIT";
    public const string Other = "OTHER";

    public static (string Code, string Detail) Map(string? reason, string? context = null)
    {
        var r = (reason ?? "").Trim();
        var detail = string.IsNullOrWhiteSpace(context) ? r : $"{context}: {r}";

        if (string.IsNullOrWhiteSpace(r))
            return (Other, detail);

        if (r.Contains("outside", StringComparison.OrdinalIgnoreCase)
            || r.Contains("session", StringComparison.OrdinalIgnoreCase))
            return (OutsideTradingHours, detail);

        if (r.Contains("SL too tight", StringComparison.OrdinalIgnoreCase))
            return (SlBelowMin, detail);

        if (r.Contains("inconsistent levels", StringComparison.OrdinalIgnoreCase)
            || r.Contains("zero risk", StringComparison.OrdinalIgnoreCase)
            || r.Contains("degenerate keylevel", StringComparison.OrdinalIgnoreCase))
            return (LevelInvalid, detail);

        if (r.Contains("no active swing", StringComparison.OrdinalIgnoreCase)
            || r.Contains("not swing", StringComparison.OrdinalIgnoreCase)
            || r.Contains("no keylevel", StringComparison.OrdinalIgnoreCase)
            || r.Contains("keylevel not extending", StringComparison.OrdinalIgnoreCase)
            || r.Contains("direction", StringComparison.OrdinalIgnoreCase)
            || r.Contains("ATR adjustment unavailable", StringComparison.OrdinalIgnoreCase)
            || r.Contains("Swing-2 TP", StringComparison.OrdinalIgnoreCase))
            return (SignalInvalid, detail);

        if (r.Contains("wrong side", StringComparison.OrdinalIgnoreCase)
            || r.Contains("limit wrong", StringComparison.OrdinalIgnoreCase))
            return (OrderDistanceInvalid, detail);

        if (r.Contains("ZONE", StringComparison.OrdinalIgnoreCase)
            || r.Contains("zone gate", StringComparison.OrdinalIgnoreCase)
            || r.Contains("overlap", StringComparison.OrdinalIgnoreCase))
            return (ZoneGate, detail);

        if (r.Contains("dedup", StringComparison.OrdinalIgnoreCase))
            return (Dedup, detail);

        if (r.Contains("parallel disabled", StringComparison.OrdinalIgnoreCase)
            || r.Contains("flip disabled", StringComparison.OrdinalIgnoreCase)
            || r.Contains("no lot", StringComparison.OrdinalIgnoreCase)
            || r.Contains("PlaceLimit FAILED", StringComparison.OrdinalIgnoreCase)
            || r.Contains("PlaceMarket FAILED", StringComparison.OrdinalIgnoreCase))
            return (Other, detail);

        if (r.Contains("AlertOn", StringComparison.OrdinalIgnoreCase))
            return (SignalInvalid, detail);

        return (Other, detail);
    }
}
