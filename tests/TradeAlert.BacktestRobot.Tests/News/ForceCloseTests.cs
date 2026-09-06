using TradeAlert.BacktestRobot.Execution.News;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests.News;

/// <summary>Force-close window: event - ForceCloseBeforeNewsMin (15) to event time.</summary>
public class ForceCloseTests
{
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

    static NewsFilterService BuildService()
    {
        var cfg = NewsFilterConfig.Build(
            enableNewsFilter: true, newsProvider: "Test", newsApiKey: "",
            newsLookaheadDays: 14, newsRefreshHours: 12, useNewsCache: false,
            newsCacheFileName: "fc_test.json", newsCacheMaxAgeHours: 48,
            newsFailSafeMode: "ContinueWithWarning",
            blockCurrencies: "USD",
            blockEventKeywords: "NFP,Nonfarm",
            blockUsdNewsSymbols: "EURUSD,XAUUSD",
            enableConservativeCrossBlock: false, conservativeCrossBlockSymbols: "",
            blockBeforeHighImpactMin: 60, blockAfterHighImpactMin: 30,
            enableNewsForceClose: true, forceCloseBeforeNewsMin: 15, cancelPendingBeforeNewsMin: 15,
            newsRefreshOnStart: false, newsRefreshOnTimer: false,
            failSafeFridayBlockStartBangkok: "19:00", failSafeFridayBlockEndBangkok: "22:00",
            newsBacktestMode: "CacheOnly");

        var svc = new NewsFilterService(cfg, new NoOpProvider(), new NewsCacheStore("fc_test.json"), _ => { });
        svc.InjectEventsForTest(new[] { NfpEvent });
        return svc;
    }

    [Fact]
    public void ShouldForceClose_14MinBefore()
    {
        // 14 minutes before event — within the 15-min window.
        var utcNow = EventUtc.AddMinutes(-14);
        var svc = BuildService();
        var d = svc.CheckForceCloseRequired("EURUSD", utcNow);
        Assert.True(d.ShouldForceCloseNow, "14min before should trigger force-close");
    }

    [Fact]
    public void ShouldNotForceClose_16MinBefore()
    {
        // 16 minutes before event — outside the 15-min window.
        var utcNow = EventUtc.AddMinutes(-16);
        var svc = BuildService();
        var d = svc.CheckForceCloseRequired("EURUSD", utcNow);
        Assert.False(d.ShouldForceCloseNow, "16min before should NOT trigger force-close (window=15)");
    }

    [Fact]
    public void ShouldNotForceClose_AfterEvent()
    {
        // 5 minutes after event — force-close window is closed.
        var utcNow = EventUtc.AddMinutes(5);
        var svc = BuildService();
        var d = svc.CheckForceCloseRequired("EURUSD", utcNow);
        Assert.False(d.ShouldForceCloseNow, "After event should NOT trigger force-close");
    }

    [Fact]
    public void ForceCloseReasonCode_IsNewsForceCloseNfp()
    {
        var utcNow = EventUtc.AddMinutes(-10);
        var svc = BuildService();
        var d = svc.CheckForceCloseRequired("EURUSD", utcNow);
        Assert.True(d.ShouldForceCloseNow);
        Assert.Equal("NEWS_FORCE_CLOSE_NFP", d.ReasonCode);
    }

    sealed class NoOpProvider : INewsCalendarProvider
    {
        public IReadOnlyList<NewsEvent> Fetch(NewsFilterConfig cfg, DateTime utcNow, out string error)
        { error = ""; return Array.Empty<NewsEvent>(); }
    }
}
