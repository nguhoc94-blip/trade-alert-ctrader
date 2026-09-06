using TradeAlert.BacktestRobot.Execution.News;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests.News;

public class HistoricalNewsCsvLoaderTests : IDisposable
{
    readonly string _tmpPath;

    public HistoricalNewsCsvLoaderTests()
    {
        _tmpPath = Path.Combine(Path.GetTempPath(), $"hist_test_{Guid.NewGuid():N}.csv");
    }

    public void Dispose()
    {
        try { if (File.Exists(_tmpPath)) File.Delete(_tmpPath); } catch { }
    }

    static void WriteCsv(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, System.Text.Encoding.UTF8);
    }

    [Fact]
    public void Load_ParsesNfpRow()
    {
        WriteCsv(_tmpPath,
            "event_name,currency,event_time_utc,block_before_min,force_close_before_min,block_after_min,source\n" +
            "Nonfarm Payrolls,USD,2024-06-07T13:30:00Z,60,15,30,test\n");

        var events = HistoricalNewsCsvLoader.Load(_tmpPath, out var error);

        Assert.Empty(error);
        Assert.Single(events);
        Assert.Equal("Nonfarm Payrolls", events[0].EventName);
        Assert.Equal("USD", events[0].Currency);
        Assert.Equal(NewsEventCategory.Nfp, events[0].Category);
        Assert.Equal(new DateTime(2024, 6, 7, 13, 30, 0, DateTimeKind.Utc), events[0].EventTimeUtc);
        Assert.Equal(60.0, events[0].BlockBeforeMinOverride);
        Assert.Equal(15.0, events[0].ForceCloseBeforeMinOverride);
        Assert.Equal(30.0, events[0].BlockAfterMinOverride);
    }

    [Fact]
    public void Load_ParsesCpiRow()
    {
        WriteCsv(_tmpPath,
            "event_name,currency,event_time_utc,block_before_min,force_close_before_min,block_after_min,source\n" +
            "CPI m/m,USD,2024-04-10T13:30:00Z,-1,-1,-1,historical_manual\n");

        var events = HistoricalNewsCsvLoader.Load(_tmpPath, out _);

        Assert.Single(events);
        Assert.Equal(NewsEventCategory.Cpi, events[0].Category);
        Assert.Null(events[0].BlockBeforeMinOverride);   // -1 → null (use config default)
        Assert.Null(events[0].ForceCloseBeforeMinOverride);
        Assert.Null(events[0].BlockAfterMinOverride);
    }

    [Fact]
    public void Load_SkipsNonCpiNfpRows()
    {
        WriteCsv(_tmpPath,
            "event_name,currency,event_time_utc,block_before_min,force_close_before_min,block_after_min,source\n" +
            "FOMC Statement,USD,2024-06-12T18:00:00Z,60,15,30,test\n" +
            "Retail Sales m/m,USD,2024-06-14T13:30:00Z,60,15,30,test\n" +
            "Nonfarm Payrolls,USD,2024-06-07T13:30:00Z,-1,-1,-1,test\n");

        var events = HistoricalNewsCsvLoader.Load(_tmpPath, out _);

        // Only NFP should be loaded; FOMC and Retail Sales are Other → skipped.
        Assert.Single(events);
        Assert.Equal(NewsEventCategory.Nfp, events[0].Category);
    }

    [Fact]
    public void Load_SkipsCommentLines()
    {
        WriteCsv(_tmpPath,
            "# This is a comment\n" +
            "event_name,currency,event_time_utc,block_before_min,force_close_before_min,block_after_min,source\n" +
            "# Another comment\n" +
            "CPI m/m,USD,2024-01-11T13:30:00Z,-1,-1,-1,historical_manual\n");

        var events = HistoricalNewsCsvLoader.Load(_tmpPath, out _);
        Assert.Single(events);
    }

    [Fact]
    public void Load_ReturnsError_WhenFileNotFound()
    {
        var events = HistoricalNewsCsvLoader.Load("/nonexistent/path/file.csv", out var error);
        Assert.Empty(events);
        Assert.Contains("NOT_FOUND", error);
    }

    [Fact]
    public void EnsureSampleCsvExists_CreatesFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sample_{Guid.NewGuid():N}.csv");
        try
        {
            HistoricalNewsCsvLoader.EnsureSampleCsvExists(path);
            Assert.True(File.Exists(path));
            var content = File.ReadAllText(path);
            // BLS official event names
            Assert.Contains("Employment Situation", content);
            Assert.Contains("Consumer Price Index", content);
            // DST-correct times: 12:30 UTC (EDT summer) and 13:30 UTC (EST winter)
            Assert.Contains("2021-01-08T13:30:00Z", content); // earliest NFP (EST)
            Assert.Contains("2021-04-02T12:30:00Z", content); // spring DST switch to EDT
            // 2025 special schedule — Sep CPI delayed to Oct-24
            Assert.Contains("2025-10-24T12:30:00Z", content);
            // 2026 coverage
            Assert.Contains("2026-12-10T13:30:00Z", content); // last CPI in file
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void EnsureSampleCsvExists_DoesNotOverwriteExistingFile()
    {
        File.WriteAllText(_tmpPath, "existing content");
        HistoricalNewsCsvLoader.EnsureSampleCsvExists(_tmpPath);
        Assert.Equal("existing content", File.ReadAllText(_tmpPath));
    }
}

/// <summary>
/// Integration: BacktestEntryBlockTests - simulates blocking from historical CSV data.
/// Verifies the full pipeline: CSV load → NewsFilterService → CheckEntryBlocked.
/// </summary>
public class BacktestEntryBlockTests
{
    // Real BLS release: May 2024 Employment Situation → 2024-06-07 12:30 UTC (EDT, UTC-4).
    static readonly DateTime EventUtc = new DateTime(2024, 6, 7, 12, 30, 0, DateTimeKind.Utc);

    static NewsFilterService BuildServiceFromCsv(string csvContent)
    {
        var tmpPath = Path.Combine(Path.GetTempPath(), $"bktest_{Guid.NewGuid():N}.csv");
        File.WriteAllText(tmpPath, csvContent);

        try
        {
            var cfg = NewsFilterConfig.Build(
                enableNewsFilter: true, newsProvider: "Test", newsApiKey: "",
                newsLookaheadDays: 14, newsRefreshHours: 12, useNewsCache: false,
                newsCacheFileName: "bt_test.json", newsCacheMaxAgeHours: 48,
                newsFailSafeMode: "ContinueWithWarning",
                blockCurrencies: "USD",
                blockEventKeywords: "NFP,Nonfarm,CPI",
                blockUsdNewsSymbols: "EURUSD,GBPUSD,XAUUSD,USDJPY",
                enableConservativeCrossBlock: false, conservativeCrossBlockSymbols: "",
                blockBeforeHighImpactMin: 60, blockAfterHighImpactMin: 30,
                enableNewsForceClose: true, forceCloseBeforeNewsMin: 15, cancelPendingBeforeNewsMin: 15,
                newsRefreshOnStart: false, newsRefreshOnTimer: false,
                failSafeFridayBlockStartBangkok: "19:00", failSafeFridayBlockEndBangkok: "22:00",
                newsBacktestMode: "CacheOnly");

            var events = HistoricalNewsCsvLoader.Load(tmpPath, out _);
            var svc = new NewsFilterService(cfg, new NoOpProvider(), new NewsCacheStore("bt_test.json"), _ => { });
            svc.InjectEventsForTest(events);
            return svc;
        }
        finally
        {
            try { File.Delete(tmpPath); } catch { }
        }
    }

    // BLS official format: "Employment Situation for ..." = NFP, "Consumer Price Index for ..." = CPI.
    // Times in user's file: 12:30 UTC (EDT/summer), 13:30 UTC (EST/winter).
    // EventUtc = 2024-06-07T12:30:00Z (EDT — May 2024 NFP, actual BLS release).

    [Fact]
    public void EntryBlocked_60MinBefore_NfpFromHistoricalCsv()
    {
        var csv =
            "event_name,currency,event_time_utc,block_before_min,force_close_before_min,block_after_min,source\n" +
            $"Employment Situation for May 2024,USD,{EventUtc:yyyy-MM-ddTHH:mm:ssZ},60,15,30,BLS official schedule\n";

        var svc = BuildServiceFromCsv(csv);
        var utcNow = EventUtc.AddMinutes(-60);
        var d = svc.CheckEntryBlocked("EURUSD", utcNow);
        Assert.True(d.IsBlocked);
        Assert.Equal("NEWS_BLOCK_NFP", d.ReasonCode);
    }

    [Fact]
    public void EntryNotBlocked_61MinBefore_FromHistoricalCsv()
    {
        var csv =
            "event_name,currency,event_time_utc,block_before_min,force_close_before_min,block_after_min,source\n" +
            $"Employment Situation for May 2024,USD,{EventUtc:yyyy-MM-ddTHH:mm:ssZ},60,15,30,BLS official schedule\n";

        var svc = BuildServiceFromCsv(csv);
        var d = svc.CheckEntryBlocked("EURUSD", EventUtc.AddMinutes(-61));
        Assert.False(d.IsBlocked);
    }

    [Fact]
    public void ForceClose_At15MinBefore_FromHistoricalCsv()
    {
        var csv =
            "event_name,currency,event_time_utc,block_before_min,force_close_before_min,block_after_min,source\n" +
            $"Employment Situation for May 2024,USD,{EventUtc:yyyy-MM-ddTHH:mm:ssZ},60,15,30,BLS official schedule\n";

        var svc = BuildServiceFromCsv(csv);
        var d = svc.CheckForceCloseRequired("EURUSD", EventUtc.AddMinutes(-14));
        Assert.True(d.ShouldForceCloseNow);
        Assert.Equal("NEWS_FORCE_CLOSE_NFP", d.ReasonCode);
    }

    [Fact]
    public void NonAffectedSymbol_NotBlocked_FromHistoricalCsv()
    {
        var csv =
            "event_name,currency,event_time_utc,block_before_min,force_close_before_min,block_after_min,source\n" +
            $"Employment Situation for May 2024,USD,{EventUtc:yyyy-MM-ddTHH:mm:ssZ},60,15,30,BLS official schedule\n";

        var svc = BuildServiceFromCsv(csv);
        // GBPJPY is not in BlockUsdNewsSymbols and no conservative cross block.
        var d = svc.CheckEntryBlocked("GBPJPY", EventUtc.AddMinutes(-30));
        Assert.False(d.IsBlocked);
    }

    [Fact]
    public void CpiRow_ClassifiedCorrectly_FromHistoricalCsv()
    {
        // Real BLS row format for CPI
        var csv =
            "event_name,currency,event_time_utc,block_before_min,force_close_before_min,block_after_min,source\n" +
            $"Consumer Price Index for May 2024,USD,{EventUtc:yyyy-MM-ddTHH:mm:ssZ},60,15,30,BLS official schedule\n";

        var svc = BuildServiceFromCsv(csv);
        var d = svc.CheckEntryBlocked("EURUSD", EventUtc.AddMinutes(-30));
        Assert.True(d.IsBlocked);
        Assert.Equal("NEWS_BLOCK_CPI", d.ReasonCode);
    }

    [Fact]
    public void EnableNewsFilterFalse_NeverBlocks()
    {
        var cfg = NewsFilterConfig.Build(
            enableNewsFilter: false, newsProvider: "Test", newsApiKey: "",
            newsLookaheadDays: 14, newsRefreshHours: 12, useNewsCache: false,
            newsCacheFileName: "dis_test.json", newsCacheMaxAgeHours: 48,
            newsFailSafeMode: "BlockAllAffectedSymbols",
            blockCurrencies: "USD",
            blockEventKeywords: "NFP,Nonfarm,CPI",
            blockUsdNewsSymbols: "EURUSD,XAUUSD",
            enableConservativeCrossBlock: false, conservativeCrossBlockSymbols: "",
            blockBeforeHighImpactMin: 60, blockAfterHighImpactMin: 30,
            enableNewsForceClose: true, forceCloseBeforeNewsMin: 15, cancelPendingBeforeNewsMin: 15,
            newsRefreshOnStart: false, newsRefreshOnTimer: false,
            failSafeFridayBlockStartBangkok: "19:00", failSafeFridayBlockEndBangkok: "22:00",
            newsBacktestMode: "CacheOnly");

        var svc = new NewsFilterService(cfg, new NoOpProvider(), new NewsCacheStore("dis_test.json"), _ => { });
        svc.InjectEventsForTest(new[]
        {
            new NewsEvent
            {
                EventName = "Employment Situation for May 2024", Currency = "USD", Impact = "High",
                EventTimeUtc = EventUtc, Category = NewsEventCategory.Nfp, Provider = "BLS official schedule",
            },
        });

        // With EnableNewsFilter=false, nothing should be blocked regardless of events.
        var entryDecision = svc.CheckEntryBlocked("EURUSD", EventUtc.AddMinutes(-30));
        Assert.False(entryDecision.IsBlocked);

        var fcDecision = svc.CheckForceCloseRequired("EURUSD", EventUtc.AddMinutes(-10));
        Assert.False(fcDecision.ShouldForceCloseNow);
    }

    sealed class NoOpProvider : INewsCalendarProvider
    {
        public IReadOnlyList<NewsEvent> Fetch(NewsFilterConfig cfg, DateTime utcNow, out string error)
        { error = ""; return Array.Empty<NewsEvent>(); }
    }
}
