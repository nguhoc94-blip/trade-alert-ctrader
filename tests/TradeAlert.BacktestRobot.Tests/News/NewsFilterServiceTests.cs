using TradeAlert.BacktestRobot.Execution.News;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests.News;

/// <summary>
/// NFP event at 13:30 UTC = 20:30 Bangkok.
/// Block window: before=60min -> block starts 12:30 UTC (19:30 BKK), after=30min -> block ends 14:00 UTC (21:00 BKK).
/// </summary>
public class NewsFilterServiceTests
{
    // 20:30 Bangkok = 13:30 UTC
    static readonly DateTime EventUtc = new DateTime(2026, 6, 6, 13, 30, 0, DateTimeKind.Utc);

    static readonly NewsEvent NfpEvent = new NewsEvent
    {
        EventName    = "Nonfarm Payrolls",
        Currency     = "USD",
        Impact       = "High",
        EventTimeUtc = EventUtc,
        Category     = NewsEventCategory.Nfp,
        Provider     = "Test",
    };

    static NewsFilterService BuildService(IReadOnlyList<NewsEvent> preloadEvents)
    {
        var cfg = NewsFilterConfig.Build(
            enableNewsFilter: true, newsProvider: "Test", newsApiKey: "",
            newsLookaheadDays: 14, newsRefreshHours: 12, useNewsCache: false,
            newsCacheFileName: "svc_test.json", newsCacheMaxAgeHours: 48,
            newsFailSafeMode: "ContinueWithWarning",
            blockCurrencies: "USD",
            blockEventKeywords: "NFP,Nonfarm",
            blockUsdNewsSymbols: "EURUSD,GBPUSD,USDJPY,XAUUSD",
            enableConservativeCrossBlock: false,
            conservativeCrossBlockSymbols: "",
            blockBeforeHighImpactMin: 60, blockAfterHighImpactMin: 30,
            enableNewsForceClose: true, forceCloseBeforeNewsMin: 15, cancelPendingBeforeNewsMin: 15,
            newsRefreshOnStart: false, newsRefreshOnTimer: false,
            failSafeFridayBlockStartBangkok: "19:00", failSafeFridayBlockEndBangkok: "22:00",
            newsBacktestMode: "CacheOnly");

        var svc = new NewsFilterService(cfg, new NoOpProvider(), new NewsCacheStore("svc_test.json"), _ => { });
        // Inject events directly (simulates a cache load)
        svc.InjectEventsForTest(preloadEvents);
        return svc;
    }

    [Fact]
    public void BlocksEntry_60MinBefore_Exactly()
    {
        // At exactly block-start (event - 60min = 12:30 UTC).
        var utcNow = EventUtc.AddMinutes(-60);
        var svc = BuildService(new[] { NfpEvent });
        var d = svc.CheckEntryBlocked("EURUSD", utcNow);
        Assert.True(d.IsBlocked, "Should be blocked at block start");
    }

    [Fact]
    public void BlocksEntry_61MinBefore_NotBlocked()
    {
        // 61 minutes before — just outside the window.
        var utcNow = EventUtc.AddMinutes(-61);
        var svc = BuildService(new[] { NfpEvent });
        var d = svc.CheckEntryBlocked("EURUSD", utcNow);
        Assert.False(d.IsBlocked, "Should NOT be blocked 61min before");
    }

    [Fact]
    public void BlocksEntry_45MinBefore()
    {
        var utcNow = EventUtc.AddMinutes(-45);
        var svc = BuildService(new[] { NfpEvent });
        var d = svc.CheckEntryBlocked("EURUSD", utcNow);
        Assert.True(d.IsBlocked);
        Assert.Equal("NEWS_BLOCK_NFP", d.ReasonCode);
    }

    [Fact]
    public void BlocksEntry_15MinAfter()
    {
        var utcNow = EventUtc.AddMinutes(15);
        var svc = BuildService(new[] { NfpEvent });
        var d = svc.CheckEntryBlocked("EURUSD", utcNow);
        Assert.True(d.IsBlocked);
    }

    [Fact]
    public void NotBlocked_31MinAfter()
    {
        // after=30, so 31 min after event is outside block window.
        var utcNow = EventUtc.AddMinutes(31);
        var svc = BuildService(new[] { NfpEvent });
        var d = svc.CheckEntryBlocked("EURUSD", utcNow);
        Assert.False(d.IsBlocked, "Should NOT be blocked 31min after event");
    }

    [Fact]
    public void ReasonCode_IsNewsBlockNfp()
    {
        var utcNow = EventUtc.AddMinutes(-30);
        var svc = BuildService(new[] { NfpEvent });
        var d = svc.CheckEntryBlocked("EURUSD", utcNow);
        Assert.True(d.IsBlocked);
        Assert.Equal("NEWS_BLOCK_NFP", d.ReasonCode);
    }

    [Fact]
    public void NoEvents_NotBlocked()
    {
        var utcNow = EventUtc.AddMinutes(-30);
        var svc = BuildService(Array.Empty<NewsEvent>());
        var d = svc.CheckEntryBlocked("EURUSD", utcNow);
        Assert.False(d.IsBlocked);
    }

    sealed class NoOpProvider : INewsCalendarProvider
    {
        public IReadOnlyList<NewsEvent> Fetch(NewsFilterConfig cfg, DateTime utcNow, out string error)
        { error = ""; return Array.Empty<NewsEvent>(); }
    }
}
