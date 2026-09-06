using TradeAlert.BacktestRobot.Execution.Analytics;

namespace TradeAlert.BacktestRobot.Execution.News;

/// <summary>
/// Orchestrates news fetch, cache, fail-safe, entry-block checks, and force-close decisions.
/// All public methods are safe to call from the bot's single-threaded context.
/// </summary>
public sealed class NewsFilterService
{
    readonly NewsFilterConfig _cfg;
    readonly INewsCalendarProvider _provider;
    readonly NewsCacheStore _cache;
    readonly Action<string> _log;

    List<NewsEvent> _currentEvents = new();
    DateTime _lastRefreshUtc = DateTime.MinValue;
    bool _usedCache;
    double _cacheAgeHours;
    string _lastApiError = "";

    public NewsFilterService(
        NewsFilterConfig cfg,
        INewsCalendarProvider provider,
        NewsCacheStore cache,
        Action<string> log)
    {
        _cfg      = cfg;
        _provider = provider;
        _cache    = cache;
        _log      = log;
    }

    public IReadOnlyList<NewsEvent> CurrentEvents => _currentEvents;
    public bool UsedCache    => _usedCache;
    public double CacheAgeHours => _cacheAgeHours;
    public string LastApiError  => _lastApiError;

    /// <summary>For unit tests only — inject pre-built event list directly.</summary>
    public void InjectEventsForTest(IReadOnlyList<NewsEvent> events)
    {
        _currentEvents = new List<NewsEvent>(events);
        _usedCache = false;
        _cacheAgeHours = 0;
        _lastRefreshUtc = DateTime.UtcNow;
    }

    /// <summary>For unit tests only — simulate stale/unavailable data so fail-safe logic triggers.</summary>
    public void SimulateStaleCache()
    {
        _currentEvents = new List<NewsEvent>();
        _usedCache = true;
        _cacheAgeHours = _cfg.NewsCacheMaxAgeHours + 1; // exceed max age -> stale
        _lastRefreshUtc = DateTime.UtcNow;
    }

    /// <summary>For unit tests only — simulate API failure with no cache available.</summary>
    public void SimulateApiError()
    {
        _currentEvents = new List<NewsEvent>();
        _usedCache = false;
        _cacheAgeHours = 0;
        _lastApiError = "SIMULATED_API_ERROR";
        _lastRefreshUtc = DateTime.UtcNow;
    }

    // ── Initialise ────────────────────────────────────────────────────────

    /// <summary>Load cache from disk on startup. Tries JSON cache first, then historical CSV. Call before RefreshIfNeeded.</summary>
    public void LoadCacheOnStart()
    {
        // 1. Try JSON cache.
        if (_cfg.UseNewsCache)
        {
            var cached = _cache.Load();
            if (cached != null)
            {
                _currentEvents = new List<NewsEvent>(cached.Events);
                _usedCache     = true;
                _cacheAgeHours = cached.AgeHours(DateTime.UtcNow);
                _log($"[NEWS] JSON cache loaded: {_currentEvents.Count} events, age={_cacheAgeHours:0.#}h, provider={cached.Provider}");
                return;
            }
            _log("[NEWS] No JSON cache file found.");
        }

        // 2. Fallback: historical CSV (auto-created with 2021-2026 sample data if missing).
        var csvPath = HistoricalNewsCsvLoader.GetDefaultPath();
        HistoricalNewsCsvLoader.EnsureSampleCsvExists(csvPath);

        var histEvents = HistoricalNewsCsvLoader.Load(csvPath, out var csvError);
        if (histEvents.Count > 0)
        {
            _currentEvents = new List<NewsEvent>(
                histEvents.Where(e => IsCurrencyMatch(e)).ToList());
            _usedCache     = true;
            _cacheAgeHours = 0; // static historical — always considered fresh
            _log($"[NEWS] Historical CSV loaded: {_currentEvents.Count} USD CPI/NFP events from {csvPath}");
        }
        else
        {
            _log($"[NEWS] Historical CSV unavailable: {csvError}");
        }
    }

    // ── Refresh ───────────────────────────────────────────────────────────

    /// <summary>Refresh only when enough time has elapsed since the last refresh.</summary>
    public void RefreshIfNeeded(DateTime utcNow)
    {
        if (!_cfg.EnableNewsFilter)
            return;

        var hoursSinceRefresh = (utcNow - _lastRefreshUtc).TotalHours;
        if (_lastRefreshUtc != DateTime.MinValue && hoursSinceRefresh < _cfg.NewsRefreshHours)
            return;

        if (_cfg.BacktestMode == NewsBacktestMode.Disabled)
            return;

        if (_cfg.BacktestMode == NewsBacktestMode.CacheOnly)
        {
            // CacheOnly: only refresh from cache if we have nothing yet.
            if (_currentEvents.Count == 0 && _cfg.UseNewsCache)
                LoadCacheOnStart();
            _lastRefreshUtc = utcNow;
            return;
        }

        // ApiThenCache or live (no BacktestMode constraint).
        TryFetchFromApi(utcNow);
    }

    /// <summary>Explicit refresh call (e.g. on startup or timer).</summary>
    public void ForceRefresh(DateTime utcNow)
    {
        if (!_cfg.EnableNewsFilter)
            return;
        if (_cfg.BacktestMode == NewsBacktestMode.Disabled)
            return;
        if (_cfg.BacktestMode == NewsBacktestMode.CacheOnly)
        {
            if (_currentEvents.Count == 0 && _cfg.UseNewsCache)
                LoadCacheOnStart();
            _lastRefreshUtc = utcNow;
            return;
        }
        TryFetchFromApi(utcNow);
    }

    void TryFetchFromApi(DateTime utcNow)
    {
        // Triple guard: only call HTTP when filter is enabled, key is present, and mode allows API.
        // Default backtest mode is CacheOnly → this method is never reached in normal backtest.
        if (!_cfg.EnableNewsFilter
            || string.IsNullOrWhiteSpace(_cfg.NewsApiKey)
            || _cfg.BacktestMode == NewsBacktestMode.CacheOnly)
        {
            _lastRefreshUtc = utcNow;
            return;
        }
        _log($"[NEWS] Fetching from API provider={_cfg.NewsProvider}...");
        var fetched = _provider.Fetch(_cfg, utcNow, out var error);
        _lastApiError = error;

        if (fetched.Count > 0)
        {
            // Filter to only CPI/NFP categories immediately.
            _currentEvents = fetched
                .Where(e => IsBlockableCategory(e) && IsCurrencyMatch(e))
                .ToList();
            _usedCache = false;
            _cacheAgeHours = 0;
            _lastRefreshUtc = utcNow;

            if (_cfg.UseNewsCache)
                _cache.Save(_currentEvents, _cfg.NewsProvider);

            _log($"[NEWS] API OK: {_currentEvents.Count} CPI/NFP USD events (of {fetched.Count} total)");
        }
        else
        {
            _log($"[NEWS] API failed/empty: {error}. Trying cache...");

            var cached = _cfg.UseNewsCache ? _cache.Load() : null;
            if (cached != null && !_cache.IsStale(cached.FetchedAtUtc, _cfg.NewsCacheMaxAgeHours, utcNow))
            {
                _currentEvents = new List<NewsEvent>(cached.Events);
                _usedCache     = true;
                _cacheAgeHours = cached.AgeHours(utcNow);
                _lastRefreshUtc = utcNow;
                _log($"[NEWS] Using valid cache: {_currentEvents.Count} events, age={_cacheAgeHours:0.#}h");
            }
            else
            {
                _log($"[NEWS] Cache missing or stale (>{_cfg.NewsCacheMaxAgeHours}h). Fail-safe mode={_cfg.FailSafeMode}");
                _lastRefreshUtc = utcNow;
                // _currentEvents kept as-is; caller will apply fail-safe in Check* methods.
            }
        }
    }

    // ── Entry Block ───────────────────────────────────────────────────────

    public NewsBlockDecision CheckEntryBlocked(string symbolName, DateTime utcNow)
    {
        if (!_cfg.EnableNewsFilter)
            return NewsBlockDecision.NotBlocked;

        var baseDecision = CheckEventsForBlock(symbolName, utcNow);
        if (baseDecision.IsBlocked)
            return baseDecision;

        // Fail-safe when no fresh data.
        return ApplyFailSafeEntryBlock(symbolName, utcNow);
    }

    public NewsBlockDecision CheckForceCloseRequired(string symbolName, DateTime utcNow)
    {
        if (!_cfg.EnableNewsFilter || !_cfg.EnableNewsForceClose)
            return NewsBlockDecision.NotBlocked;

        foreach (var ev in _currentEvents)
        {
            if (!IsAffectedSymbol(symbolName, ev))
                continue;

            var minutesTo = (ev.EventTimeUtc - utcNow).TotalMinutes;
            var fcMin     = ev.ForceCloseBeforeMinOverride ?? _cfg.ForceCloseBeforeNewsMin;
            if (minutesTo >= 0 && minutesTo <= fcMin)
            {
                var forceCloseStart = ev.EventTimeUtc.AddMinutes(-fcMin);
                return new NewsBlockDecision
                {
                    IsBlocked          = true,
                    ShouldForceCloseNow = true,
                    Event              = ev,
                    ReasonCode         = ForceCloseCode(ev),
                    ReasonDetail       = $"force_close: {ev.EventName} in {minutesTo:0.#}m",
                    MinutesToEvent     = minutesTo,
                    UsedCache          = _usedCache,
                    CacheAgeHours      = _cacheAgeHours,
                    BlockStartUtc      = ev.EventTimeUtc.AddMinutes(-(ev.BlockBeforeMinOverride ?? _cfg.BlockBeforeHighImpactMin)),
                    BlockEndUtc        = ev.EventTimeUtc.AddMinutes(ev.BlockAfterMinOverride ?? _cfg.BlockAfterHighImpactMin),
                    ForceCloseStartUtc = forceCloseStart,
                };
            }
        }
        return NewsBlockDecision.NotBlocked;
    }

    public bool IsAffectedSymbol(string symbolName, NewsEvent ev)
    {
        var sym = NormalizeSymbol(symbolName);

        // Check direct USD-affected list.
        foreach (var s in _cfg.BlockUsdNewsSymbols)
        {
            if (sym.Contains(NormalizeSymbol(s), StringComparison.Ordinal))
                return true;
        }

        // Conservative cross pairs.
        if (_cfg.EnableConservativeCrossBlock)
        {
            foreach (var s in _cfg.ConservativeCrossBlockSymbols)
            {
                if (sym.Contains(NormalizeSymbol(s), StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    // ── Telemetry helpers ─────────────────────────────────────────────────

    public NewsEvent? NextEvent(DateTime utcNow) =>
        _currentEvents
            .Where(e => e.EventTimeUtc > utcNow)
            .OrderBy(e => e.EventTimeUtc)
            .FirstOrDefault();

    public string BuildTelemetryLine(DateTime utcNow)
    {
        var next = NextEvent(utcNow);
        var nextStr = next != null ? $"{next.EventName} @ {next.EventTimeUtc:yyyy-MM-dd HH:mm}Z" : "none";
        return $"[NEWS] Filter enabled={_cfg.EnableNewsFilter} provider={_cfg.NewsProvider} " +
               $"backtest_mode={_cfg.BacktestMode} fail_safe={_cfg.FailSafeMode} " +
               $"used_cache={_usedCache} cache_age={_cacheAgeHours:0.#}h " +
               $"events={_currentEvents.Count} next={nextStr}";
    }

    // ── Private helpers ───────────────────────────────────────────────────

    NewsBlockDecision CheckEventsForBlock(string symbolName, DateTime utcNow)
    {
        foreach (var ev in _currentEvents)
        {
            if (!IsAffectedSymbol(symbolName, ev))
                continue;

            var minutesTo  = (ev.EventTimeUtc - utcNow).TotalMinutes;
            var beforeMin  = ev.BlockBeforeMinOverride   ?? _cfg.BlockBeforeHighImpactMin;
            var afterMin   = ev.BlockAfterMinOverride    ?? _cfg.BlockAfterHighImpactMin;
            var blockStart = ev.EventTimeUtc.AddMinutes(-beforeMin);
            var blockEnd   = ev.EventTimeUtc.AddMinutes(afterMin);

            if (utcNow >= blockStart && utcNow <= blockEnd)
            {
                var fcMin = ev.ForceCloseBeforeMinOverride ?? _cfg.ForceCloseBeforeNewsMin;
                var forceCloseStart = ev.EventTimeUtc.AddMinutes(-fcMin);
                var d = new NewsBlockDecision
                {
                    IsBlocked      = true,
                    ShouldForceCloseNow = false,
                    Event          = ev,
                    ReasonCode     = BlockCode(ev),
                    MinutesToEvent = minutesTo,
                    UsedCache      = _usedCache,
                    CacheAgeHours  = _cacheAgeHours,
                    BlockStartUtc  = blockStart,
                    BlockEndUtc    = blockEnd,
                    ForceCloseStartUtc = forceCloseStart,
                    ReasonDetail   = BuildDetailForEvent(ev, minutesTo),
                };
                return d;
            }
        }
        return NewsBlockDecision.NotBlocked;
    }

    NewsBlockDecision ApplyFailSafeEntryBlock(string symbolName, DateTime utcNow)
    {
        // Only apply fail-safe when data is stale or missing.
        bool dataFresh = _usedCache
            ? _cacheAgeHours <= _cfg.NewsCacheMaxAgeHours
            : _lastApiError == "";

        if (dataFresh)
            return NewsBlockDecision.NotBlocked;

        return _cfg.FailSafeMode switch
        {
            NewsFailSafeMode.ContinueWithWarning => new NewsBlockDecision
            {
                IsBlocked   = false,
                ReasonCode  = "NEWS_CACHE_STALE",
                ReasonDetail = "fail-safe=ContinueWithWarning: stale/missing data, trading allowed with warning",
                UsedCache   = _usedCache,
                CacheAgeHours = _cacheAgeHours,
            },

            NewsFailSafeMode.BlockAllAffectedSymbols when IsAnyAffectedSymbol(symbolName) =>
                BuildFailSafeDecision(symbolName, utcNow, "BlockAllAffectedSymbols: no fresh news data"),

            NewsFailSafeMode.UseCacheThenBlock when IsAnyAffectedSymbol(symbolName) && IsFridayNfpWindow(utcNow) =>
                BuildFailSafeDecision(symbolName, utcNow, "UseCacheThenBlock: Friday 19-22 BKK fail-safe window"),

            _ => NewsBlockDecision.NotBlocked,
        };
    }

    bool IsAnyAffectedSymbol(string symbolName)
    {
        var sym = NormalizeSymbol(symbolName);
        foreach (var s in _cfg.BlockUsdNewsSymbols)
            if (sym.Contains(NormalizeSymbol(s), StringComparison.Ordinal))
                return true;
        if (_cfg.EnableConservativeCrossBlock)
            foreach (var s in _cfg.ConservativeCrossBlockSymbols)
                if (sym.Contains(NormalizeSymbol(s), StringComparison.Ordinal))
                    return true;
        return false;
    }

    bool IsFridayNfpWindow(DateTime utcNow)
    {
        var bkk = Loop6BangkokSession.GetBangkokTime(utcNow);
        if (bkk.DayOfWeek != DayOfWeek.Friday)
            return false;
        var tod = bkk.TimeOfDay;
        return tod >= _cfg.FailSafeFridayBlockStart && tod < _cfg.FailSafeFridayBlockEnd;
    }

    static NewsBlockDecision BuildFailSafeDecision(string symbolName, DateTime utcNow, string detail) =>
        new()
        {
            IsBlocked   = true,
            ReasonCode  = "NEWS_CACHE_STALE",
            ReasonDetail = detail,
        };

    bool IsBlockableCategory(NewsEvent ev) =>
        ev.Category == NewsEventCategory.Cpi || ev.Category == NewsEventCategory.Nfp;

    bool IsCurrencyMatch(NewsEvent ev)
    {
        foreach (var c in _cfg.BlockCurrencies)
            if (ev.Currency.Equals(c, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    static string BlockCode(NewsEvent ev) => ev.Category switch
    {
        NewsEventCategory.Cpi => "NEWS_BLOCK_CPI",
        NewsEventCategory.Nfp => "NEWS_BLOCK_NFP",
        _ => "NEWS_BLOCK",
    };

    static string ForceCloseCode(NewsEvent ev) => ev.Category switch
    {
        NewsEventCategory.Cpi => "NEWS_FORCE_CLOSE_CPI",
        NewsEventCategory.Nfp => "NEWS_FORCE_CLOSE_NFP",
        _ => "NEWS_FORCE_CLOSE",
    };

    static string NormalizeSymbol(string s) =>
        s.ToUpperInvariant().Replace(".", "").Replace(" ", "").Replace("_", "");

    static string BuildDetailForEvent(NewsEvent ev, double minutesTo)
    {
        var eventBkk = ev.EventTimeUtc.AddHours(7);
        return string.Join("|",
            $"event={Escape(ev.EventName)}",
            $"currency={ev.Currency}",
            $"impact={ev.Impact}",
            $"event_utc={ev.EventTimeUtc:yyyy-MM-ddTHH:mm:ssZ}",
            $"event_bkk={eventBkk:yyyy-MM-ddTHH:mm:ss}",
            $"minutes={minutesTo:0.#}");
    }

    static string Escape(string s) => s.Replace("|", "/").Replace(",", ";");
}
