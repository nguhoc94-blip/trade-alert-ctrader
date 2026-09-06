using TradeAlert.BacktestRobot.Execution.News;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests.News;

public class NewsCacheStoreTests : IDisposable
{
    readonly string _fileName;
    readonly NewsCacheStore _store;

    public NewsCacheStoreTests()
    {
        _fileName = $"test_cache_{Guid.NewGuid():N}.json";
        _store = new NewsCacheStore(_fileName);
    }

    public void Dispose()
    {
        try { if (File.Exists(_store.CachePath)) File.Delete(_store.CachePath); } catch { }
    }

    static NewsEvent MakeEvent(NewsEventCategory cat, string name = "Test Event") => new NewsEvent
    {
        EventName    = name,
        Currency     = "USD",
        Impact       = "High",
        EventTimeUtc = new DateTime(2026, 6, 6, 13, 30, 0, DateTimeKind.Utc),
        Provider     = "Test",
        RawId        = "abc123",
        Category     = cat,
    };

    [Fact]
    public void SaveAndLoad_RoundTrip()
    {
        var events = new[]
        {
            MakeEvent(NewsEventCategory.Nfp, "Nonfarm Payrolls"),
            MakeEvent(NewsEventCategory.Cpi, "CPI m/m"),
        };

        _store.Save(events, "TestProvider");

        var result = _store.Load();
        Assert.NotNull(result);
        Assert.Equal(2, result!.Events.Count);
        Assert.Equal("Nonfarm Payrolls", result.Events[0].EventName);
        Assert.Equal(NewsEventCategory.Nfp, result.Events[0].Category);
        Assert.Equal("CPI m/m", result.Events[1].EventName);
        Assert.Equal(NewsEventCategory.Cpi, result.Events[1].Category);
        Assert.Equal("TestProvider", result.Provider);
        Assert.Equal("USD", result.Events[0].Currency);
    }

    [Fact]
    public void Load_ReturnsNull_WhenNoFile()
    {
        var result = _store.Load();
        Assert.Null(result);
    }

    [Fact]
    public void IsStale_ReturnsFalse_WhenFresh()
    {
        var fetchedAt = DateTime.UtcNow.AddHours(-10);
        Assert.False(_store.IsStale(fetchedAt, maxAgeHours: 24, utcNow: DateTime.UtcNow));
    }

    [Fact]
    public void IsStale_ReturnsTrue_WhenTooOld()
    {
        var fetchedAt = DateTime.UtcNow.AddHours(-50);
        Assert.True(_store.IsStale(fetchedAt, maxAgeHours: 48, utcNow: DateTime.UtcNow));
    }

    [Fact]
    public void EventTimeUtc_PreservedAsUtc_AfterRoundTrip()
    {
        var original = new DateTime(2026, 6, 6, 13, 30, 0, DateTimeKind.Utc);
        _store.Save(new[] { MakeEvent(NewsEventCategory.Nfp) }, "X");
        var loaded = _store.Load()!.Events[0];
        Assert.Equal(DateTimeKind.Utc, loaded.EventTimeUtc.Kind);
        Assert.Equal(original, loaded.EventTimeUtc);
    }
}
