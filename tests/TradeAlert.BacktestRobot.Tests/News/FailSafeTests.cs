using TradeAlert.BacktestRobot.Execution.News;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests.News;

/// <summary>Fail-safe behavior when API fails and no valid cache is present.</summary>
public class FailSafeTests
{
    // Friday 2026-06-05, 12:30 UTC = 19:30 Bangkok (Friday, within fail-safe window 19:00-22:00).
    static readonly DateTime FridayNfpWindowUtc = new DateTime(2026, 6, 5, 12, 30, 0, DateTimeKind.Utc);

    // Same Friday but outside window: 11:00 UTC = 18:00 Bangkok.
    static readonly DateTime FridayBeforeWindowUtc = new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc);

    static NewsFilterService BuildServiceWithFailSafe(string failSafeMode, bool hasStaleCache = false)
    {
        var cfg = NewsFilterConfig.Build(
            enableNewsFilter: true, newsProvider: "Test", newsApiKey: "",
            newsLookaheadDays: 14, newsRefreshHours: 12, useNewsCache: false,
            newsCacheFileName: "fs_test.json", newsCacheMaxAgeHours: 48,
            newsFailSafeMode: failSafeMode,
            blockCurrencies: "USD",
            blockEventKeywords: "NFP,Nonfarm,CPI",
            blockUsdNewsSymbols: "EURUSD,XAUUSD",
            enableConservativeCrossBlock: false, conservativeCrossBlockSymbols: "",
            blockBeforeHighImpactMin: 60, blockAfterHighImpactMin: 30,
            enableNewsForceClose: false, forceCloseBeforeNewsMin: 15, cancelPendingBeforeNewsMin: 15,
            newsRefreshOnStart: false, newsRefreshOnTimer: false,
            failSafeFridayBlockStartBangkok: "19:00", failSafeFridayBlockEndBangkok: "22:00",
            newsBacktestMode: "CacheOnly");

        var svc = new NewsFilterService(cfg, new NoOpProvider(), new NewsCacheStore("fs_test.json"), _ => { });

        if (hasStaleCache)
            svc.SimulateStaleCache();
        else
            svc.SimulateApiError(); // No events, API error, no valid cache → fail-safe applies.

        return svc;
    }

    [Fact]
    public void UseCacheThenBlock_FridayWindow_BlocksAffectedSymbol()
    {
        var svc = BuildServiceWithFailSafe("UseCacheThenBlock");
        var d = svc.CheckEntryBlocked("EURUSD", FridayNfpWindowUtc);
        Assert.True(d.IsBlocked, "UseCacheThenBlock: EURUSD should be blocked during Friday fail-safe window");
    }

    [Fact]
    public void UseCacheThenBlock_FridayBeforeWindow_NotBlocked()
    {
        var svc = BuildServiceWithFailSafe("UseCacheThenBlock");
        var d = svc.CheckEntryBlocked("EURUSD", FridayBeforeWindowUtc);
        Assert.False(d.IsBlocked, "UseCacheThenBlock: should NOT block before the Friday window");
    }

    [Fact]
    public void ContinueWithWarning_FridayWindow_NotBlocked()
    {
        var svc = BuildServiceWithFailSafe("ContinueWithWarning");
        var d = svc.CheckEntryBlocked("EURUSD", FridayNfpWindowUtc);
        Assert.False(d.IsBlocked, "ContinueWithWarning: should never block from fail-safe");
    }

    [Fact]
    public void BlockAllAffectedSymbols_AlwaysBlocksWhenNoData()
    {
        var svc = BuildServiceWithFailSafe("BlockAllAffectedSymbols");
        // Any time, any affected symbol should be blocked.
        var d = svc.CheckEntryBlocked("EURUSD", FridayBeforeWindowUtc);
        Assert.True(d.IsBlocked, "BlockAllAffectedSymbols: should always block when no data");
    }

    sealed class NoOpProvider : INewsCalendarProvider
    {
        public IReadOnlyList<NewsEvent> Fetch(NewsFilterConfig cfg, DateTime utcNow, out string error)
        { error = "API_FAIL"; return Array.Empty<NewsEvent>(); }
    }
}
