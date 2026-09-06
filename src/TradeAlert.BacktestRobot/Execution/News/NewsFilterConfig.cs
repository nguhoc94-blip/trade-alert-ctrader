namespace TradeAlert.BacktestRobot.Execution.News;

public enum NewsFailSafeMode
{
    ContinueWithWarning  = 0,
    UseCacheThenBlock    = 1,
    BlockAllAffectedSymbols = 2,
}

public enum NewsBacktestMode
{
    Disabled   = 0,
    CacheOnly  = 1,
    ApiThenCache = 2,
}

/// <summary>Immutable configuration for the news filter, built once from bot parameters.</summary>
public sealed class NewsFilterConfig
{
    public bool EnableNewsFilter       { get; init; } = true;
    public string NewsProvider         { get; init; } = "TradingEconomics";
    public string NewsApiKey           { get; init; } = "";
    public int NewsLookaheadDays       { get; init; } = 14;
    public double NewsRefreshHours     { get; init; } = 12;
    public bool UseNewsCache           { get; init; } = true;
    public string NewsCacheFileName    { get; init; } = "loop6_news_cache.json";
    public double NewsCacheMaxAgeHours { get; init; } = 48;
    public NewsFailSafeMode FailSafeMode { get; init; } = NewsFailSafeMode.UseCacheThenBlock;
    public NewsBacktestMode BacktestMode { get; init; } = NewsBacktestMode.CacheOnly;

    public IReadOnlyList<string> BlockCurrencies       { get; init; } = new[] { "USD" };
    public IReadOnlyList<string> BlockEventKeywords    { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> BlockUsdNewsSymbols   { get; init; } = Array.Empty<string>();
    public bool EnableConservativeCrossBlock           { get; init; } = false;
    public IReadOnlyList<string> ConservativeCrossBlockSymbols { get; init; } = Array.Empty<string>();

    public double BlockBeforeHighImpactMin  { get; init; } = 60;
    public double BlockAfterHighImpactMin   { get; init; } = 30;
    public bool EnableNewsForceClose        { get; init; } = true;
    public double ForceCloseBeforeNewsMin   { get; init; } = 15;
    public double CancelPendingBeforeNewsMin { get; init; } = 15;

    public bool NewsRefreshOnStart          { get; init; } = true;
    public bool NewsRefreshOnTimer          { get; init; } = true;

    // Fail-safe Friday window in Bangkok time (hh:mm).
    public TimeSpan FailSafeFridayBlockStart { get; init; } = new TimeSpan(19, 0, 0);
    public TimeSpan FailSafeFridayBlockEnd   { get; init; } = new TimeSpan(22, 0, 0);

    /// <summary>Parse all string parameters safely.</summary>
    public static NewsFilterConfig Build(
        bool enableNewsFilter,
        string newsProvider,
        string newsApiKey,
        int newsLookaheadDays,
        double newsRefreshHours,
        bool useNewsCache,
        string newsCacheFileName,
        double newsCacheMaxAgeHours,
        string newsFailSafeMode,
        string blockCurrencies,
        string blockEventKeywords,
        string blockUsdNewsSymbols,
        bool enableConservativeCrossBlock,
        string conservativeCrossBlockSymbols,
        double blockBeforeHighImpactMin,
        double blockAfterHighImpactMin,
        bool enableNewsForceClose,
        double forceCloseBeforeNewsMin,
        double cancelPendingBeforeNewsMin,
        bool newsRefreshOnStart,
        bool newsRefreshOnTimer,
        string failSafeFridayBlockStartBangkok,
        string failSafeFridayBlockEndBangkok,
        string newsBacktestMode)
    {
        return new NewsFilterConfig
        {
            EnableNewsFilter       = enableNewsFilter,
            NewsProvider           = newsProvider,
            NewsApiKey             = newsApiKey?.Trim() ?? "",
            NewsLookaheadDays      = Math.Max(1, newsLookaheadDays),
            NewsRefreshHours       = Math.Max(0.1, newsRefreshHours),
            UseNewsCache           = useNewsCache,
            NewsCacheFileName      = string.IsNullOrWhiteSpace(newsCacheFileName) ? "loop6_news_cache.json" : newsCacheFileName.Trim(),
            NewsCacheMaxAgeHours   = Math.Max(1, newsCacheMaxAgeHours),
            FailSafeMode           = ParseFailSafeMode(newsFailSafeMode),
            BacktestMode           = ParseBacktestMode(newsBacktestMode),
            BlockCurrencies        = ParseList(blockCurrencies),
            BlockEventKeywords     = ParseList(blockEventKeywords),
            BlockUsdNewsSymbols    = ParseList(blockUsdNewsSymbols),
            EnableConservativeCrossBlock = enableConservativeCrossBlock,
            ConservativeCrossBlockSymbols = ParseList(conservativeCrossBlockSymbols),
            BlockBeforeHighImpactMin = Math.Max(0, blockBeforeHighImpactMin),
            BlockAfterHighImpactMin  = Math.Max(0, blockAfterHighImpactMin),
            EnableNewsForceClose     = enableNewsForceClose,
            ForceCloseBeforeNewsMin  = Math.Max(0, forceCloseBeforeNewsMin),
            CancelPendingBeforeNewsMin = Math.Max(0, cancelPendingBeforeNewsMin),
            NewsRefreshOnStart       = newsRefreshOnStart,
            NewsRefreshOnTimer       = newsRefreshOnTimer,
            FailSafeFridayBlockStart = ParseTimeSpan(failSafeFridayBlockStartBangkok, new TimeSpan(19, 0, 0)),
            FailSafeFridayBlockEnd   = ParseTimeSpan(failSafeFridayBlockEndBangkok, new TimeSpan(22, 0, 0)),
        };
    }

    static IReadOnlyList<string> ParseList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    static NewsFailSafeMode ParseFailSafeMode(string? raw) => raw?.Trim() switch
    {
        "ContinueWithWarning"    => NewsFailSafeMode.ContinueWithWarning,
        "BlockAllAffectedSymbols" => NewsFailSafeMode.BlockAllAffectedSymbols,
        _ => NewsFailSafeMode.UseCacheThenBlock,
    };

    static NewsBacktestMode ParseBacktestMode(string? raw) => raw?.Trim() switch
    {
        "Disabled"    => NewsBacktestMode.Disabled,
        "ApiThenCache" => NewsBacktestMode.ApiThenCache,
        _ => NewsBacktestMode.CacheOnly,
    };

    static TimeSpan ParseTimeSpan(string? raw, TimeSpan fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;
        var parts = raw.Trim().Split(':');
        if (parts.Length >= 2
            && int.TryParse(parts[0], out var h)
            && int.TryParse(parts[1], out var m))
            return new TimeSpan(h, m, 0);
        return fallback;
    }
}
