using System.Text.Json;
using System.Text.Json.Serialization;

namespace TradeAlert.BacktestRobot.Execution.News;

/// <summary>
/// Persists news events to a JSON file under MyDocuments/cAlgo/Data/Loop6News/.
/// Thread-safety: not required — called from single-threaded bot context.
/// </summary>
public sealed class NewsCacheStore
{
    readonly string _cachePath;

    public NewsCacheStore(string cacheFileName)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "cAlgo", "Data", "Loop6News");
        Directory.CreateDirectory(dir);
        _cachePath = Path.Combine(dir, cacheFileName);
    }

    /// <summary>Exposed for test injection.</summary>
    public string CachePath => _cachePath;

    public void Save(IReadOnlyList<NewsEvent> events, string provider)
    {
        try
        {
            var payload = new CachePayload
            {
                FetchedAtUtc = DateTime.UtcNow,
                Provider     = provider,
                Events       = events.Select(SerializedEvent.From).ToList(),
            };
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            File.WriteAllText(_cachePath, json, System.Text.Encoding.UTF8);
        }
        catch
        {
            // Cache write failure is non-fatal.
        }
    }

    /// <summary>
    /// Load cached events.  Returns null if file missing or unparseable.
    /// </summary>
    public CacheResult? Load()
    {
        try
        {
            if (!File.Exists(_cachePath))
                return null;

            var json = File.ReadAllText(_cachePath, System.Text.Encoding.UTF8);
            var payload = JsonSerializer.Deserialize<CachePayload>(json, JsonOptions);
            if (payload == null)
                return null;

            var events = payload.Events
                .Select(e => e.ToNewsEvent())
                .ToList();

            return new CacheResult(events, payload.FetchedAtUtc, payload.Provider);
        }
        catch
        {
            return null;
        }
    }

    public bool IsStale(DateTime fetchedAtUtc, double maxAgeHours, DateTime utcNow) =>
        (utcNow - fetchedAtUtc).TotalHours > maxAgeHours;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public sealed record CacheResult(
        IReadOnlyList<NewsEvent> Events,
        DateTime FetchedAtUtc,
        string Provider)
    {
        public double AgeHours(DateTime utcNow) => (utcNow - FetchedAtUtc).TotalHours;
    }

    sealed class CachePayload
    {
        [JsonPropertyName("fetched_at_utc")]
        public DateTime FetchedAtUtc { get; set; }

        [JsonPropertyName("provider")]
        public string Provider { get; set; } = "";

        [JsonPropertyName("events")]
        public List<SerializedEvent> Events { get; set; } = new();
    }

    sealed class SerializedEvent
    {
        [JsonPropertyName("event_name")]   public string EventName    { get; set; } = "";
        [JsonPropertyName("currency")]     public string Currency     { get; set; } = "";
        [JsonPropertyName("impact")]       public string Impact       { get; set; } = "";
        [JsonPropertyName("event_utc")]    public DateTime EventTimeUtc { get; set; }
        [JsonPropertyName("provider")]     public string Provider     { get; set; } = "";
        [JsonPropertyName("raw_id")]       public string RawId        { get; set; } = "";
        [JsonPropertyName("category")]     public string Category     { get; set; } = "";

        public static SerializedEvent From(NewsEvent e) => new()
        {
            EventName    = e.EventName,
            Currency     = e.Currency,
            Impact       = e.Impact,
            EventTimeUtc = e.EventTimeUtc,
            Provider     = e.Provider,
            RawId        = e.RawId,
            Category     = e.Category.ToString(),
        };

        public NewsEvent ToNewsEvent()
        {
            Enum.TryParse<NewsEventCategory>(Category, out var cat);
            return new NewsEvent
            {
                EventName    = EventName,
                Currency     = Currency,
                Impact       = Impact,
                EventTimeUtc = DateTime.SpecifyKind(EventTimeUtc, DateTimeKind.Utc),
                Provider     = Provider,
                RawId        = RawId,
                Category     = cat,
            };
        }
    }
}
