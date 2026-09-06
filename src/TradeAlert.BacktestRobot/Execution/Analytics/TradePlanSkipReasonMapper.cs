namespace TradeAlert.BacktestRobot.Execution.Analytics;

/// <summary>Normalize trade-mapper skip text to TRADE_PLAN_SKIP reason codes.</summary>
public static class TradePlanSkipReasonMapper
{
    public const string SlBelowMin = "SL_BELOW_MIN";
    public const string BMapperFail = "B_MAPPER_FAIL";
    public const string RrFail = "RR_FAIL";
    public const string ZoneGate = "ZONE_GATE";
    public const string NearDGate = "NEAR_D_GATE";
    public const string OutsideSession = "OUTSIDE_SESSION";
    public const string Other = "OTHER";

    public static string Map(string? reason)
    {
        var r = (reason ?? "").Trim();
        if (string.IsNullOrWhiteSpace(r))
            return Other;

        if (r.Contains("SL too tight", StringComparison.OrdinalIgnoreCase)
            || r.Contains("below min", StringComparison.OrdinalIgnoreCase))
            return SlBelowMin;

        if (r.Contains("no active swing", StringComparison.OrdinalIgnoreCase)
            || r.Contains("not swing", StringComparison.OrdinalIgnoreCase)
            || r.Contains("no keylevel", StringComparison.OrdinalIgnoreCase)
            || r.Contains("keylevel not extending", StringComparison.OrdinalIgnoreCase)
            || r.Contains("degenerate keylevel", StringComparison.OrdinalIgnoreCase)
            || r.Contains("pinned B", StringComparison.OrdinalIgnoreCase)
            || r.Contains("plan invalid", StringComparison.OrdinalIgnoreCase)
            || r.Contains("split leg", StringComparison.OrdinalIgnoreCase))
            return BMapperFail;

        if (r.Contains("RR", StringComparison.OrdinalIgnoreCase)
            || r.Contains("reward", StringComparison.OrdinalIgnoreCase)
            || r.Contains("swing-C RR", StringComparison.OrdinalIgnoreCase))
            return RrFail;

        if (r.Contains("zone", StringComparison.OrdinalIgnoreCase)
            || r.Contains("overlap", StringComparison.OrdinalIgnoreCase)
            || r.Contains("obstacle", StringComparison.OrdinalIgnoreCase))
            return ZoneGate;

        if (r.Contains("near D", StringComparison.OrdinalIgnoreCase)
            || r.Contains("NEAR_D", StringComparison.OrdinalIgnoreCase))
            return NearDGate;

        if (r.Contains("outside", StringComparison.OrdinalIgnoreCase)
            || r.Contains("session", StringComparison.OrdinalIgnoreCase)
            || r.Contains("cutoff", StringComparison.OrdinalIgnoreCase))
            return OutsideSession;

        var (code, _) = Loop6SkipReasonCodes.Map(r);
        return code switch
        {
            Loop6SkipReasonCodes.SlBelowMin => SlBelowMin,
            Loop6SkipReasonCodes.ZoneGate => ZoneGate,
            Loop6SkipReasonCodes.RrBelowMin => RrFail,
            Loop6SkipReasonCodes.OutsideTradingHours => OutsideSession,
            Loop6SkipReasonCodes.EntryCutoffBangkok => OutsideSession,
            _ => Other,
        };
    }
}
