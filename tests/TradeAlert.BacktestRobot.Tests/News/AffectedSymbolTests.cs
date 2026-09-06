using TradeAlert.BacktestRobot.Execution.News;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests.News;

public class AffectedSymbolTests
{
    static NewsFilterService BuildService(bool conservativeCrossBlock = false)
    {
        var cfg = NewsFilterConfig.Build(
            enableNewsFilter: true, newsProvider: "Test", newsApiKey: "",
            newsLookaheadDays: 14, newsRefreshHours: 12, useNewsCache: false,
            newsCacheFileName: "test_cache.json", newsCacheMaxAgeHours: 48,
            newsFailSafeMode: "UseCacheThenBlock",
            blockCurrencies: "USD",
            blockEventKeywords: "CPI,NFP",
            blockUsdNewsSymbols: "EURUSD,GBPUSD,AUDUSD,NZDUSD,USDJPY,USDCAD,USDCHF,XAUUSD",
            enableConservativeCrossBlock: conservativeCrossBlock,
            conservativeCrossBlockSymbols: "EURJPY",
            blockBeforeHighImpactMin: 60, blockAfterHighImpactMin: 30,
            enableNewsForceClose: true, forceCloseBeforeNewsMin: 15, cancelPendingBeforeNewsMin: 15,
            newsRefreshOnStart: false, newsRefreshOnTimer: false,
            failSafeFridayBlockStartBangkok: "19:00", failSafeFridayBlockEndBangkok: "22:00",
            newsBacktestMode: "CacheOnly");

        var cache = new NewsCacheStore("test_cache.json");
        var provider = new TestProvider(Array.Empty<NewsEvent>());
        return new NewsFilterService(cfg, provider, cache, _ => { });
    }

    static NewsEvent UsdNfpEvent() => new NewsEvent
    {
        EventName = "Nonfarm Payrolls", Currency = "USD", Impact = "High",
        EventTimeUtc = DateTime.UtcNow.AddHours(2),
        Category = NewsEventCategory.Nfp, Provider = "Test",
    };

    [Theory]
    [InlineData("EURUSD")]
    [InlineData("GBPUSD")]
    [InlineData("AUDUSD")]
    [InlineData("NZDUSD")]
    [InlineData("USDJPY")]
    [InlineData("USDCAD")]
    [InlineData("USDCHF")]
    [InlineData("XAUUSD")]
    public void UsdNews_BlocksUsdAffectedSymbols(string symbol)
    {
        var svc = BuildService();
        Assert.True(svc.IsAffectedSymbol(symbol, UsdNfpEvent()));
    }

    [Theory]
    [InlineData("GBPJPY")]
    [InlineData("EURCHF")]
    [InlineData("AUDCAD")]
    public void UsdNews_DoesNotBlockNonListedSymbols(string symbol)
    {
        var svc = BuildService(conservativeCrossBlock: false);
        Assert.False(svc.IsAffectedSymbol(symbol, UsdNfpEvent()));
    }

    [Fact]
    public void UsdNews_BlocksEurJpy_WhenConservativeCrossBlockOn()
    {
        var svc = BuildService(conservativeCrossBlock: true);
        Assert.True(svc.IsAffectedSymbol("EURJPY", UsdNfpEvent()));
    }

    [Fact]
    public void UsdNews_DoesNotBlockEurJpy_WhenConservativeCrossBlockOff()
    {
        var svc = BuildService(conservativeCrossBlock: false);
        Assert.False(svc.IsAffectedSymbol("EURJPY", UsdNfpEvent()));
    }

    sealed class TestProvider : INewsCalendarProvider
    {
        readonly IReadOnlyList<NewsEvent> _events;
        public TestProvider(IReadOnlyList<NewsEvent> events) { _events = events; }
        public IReadOnlyList<NewsEvent> Fetch(NewsFilterConfig cfg, DateTime utcNow, out string error)
        { error = ""; return _events; }
    }
}
