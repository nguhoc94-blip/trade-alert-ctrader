using System.Net.Http;
using System.Text.Json;

namespace TradeAlert.BacktestRobot.Execution.News;

/// <summary>
/// Fetches economic calendar from the TradingEconomics API.
///
/// TODO: Confirm the exact endpoint and schema for your TradingEconomics subscription tier.
/// The URL template below works for the "calendar" endpoint with client/secret auth.
/// Schema: array of objects with "Date", "Country", "Category", "Event", "Importance".
///
/// If the API key is empty, returns empty list immediately (no network call).
/// All errors are caught and returned as the error string — never throws.
/// </summary>
public sealed class TradingEconomicsNewsProvider : INewsCalendarProvider
{
    // TODO: Adjust to match your TradingEconomics subscription endpoint.
    // Placeholder URL uses client+secret query-string auth; replace with your actual format.
    const string UrlTemplate =
        "https://api.tradingeconomics.com/calendar/country/{currencies}/importance/3?c={apiKey}&d1={from}&d2={to}";

    static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public IReadOnlyList<NewsEvent> Fetch(NewsFilterConfig cfg, DateTime utcNow, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(cfg.NewsApiKey))
        {
            error = "NEWS_API_KEY_EMPTY";
            return Array.Empty<NewsEvent>();
        }

        try
        {
            var currencies = string.Join(",", cfg.BlockCurrencies).ToLowerInvariant();
            var from = utcNow.Date.ToString("yyyy-MM-dd");
            var to   = utcNow.Date.AddDays(cfg.NewsLookaheadDays).ToString("yyyy-MM-dd");
            var url  = UrlTemplate
                .Replace("{currencies}", Uri.EscapeDataString(currencies))
                .Replace("{apiKey}", Uri.EscapeDataString(cfg.NewsApiKey))
                .Replace("{from}", from)
                .Replace("{to}", to);

            var response = _http.GetAsync(url).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                error = $"HTTP_{(int)response.StatusCode}";
                return Array.Empty<NewsEvent>();
            }

            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return ParseResponse(json, cfg, out error);
        }
        catch (Exception ex)
        {
            error = $"EXCEPTION: {ex.GetType().Name}: {ex.Message}";
            return Array.Empty<NewsEvent>();
        }
    }

    static IReadOnlyList<NewsEvent> ParseResponse(string json, NewsFilterConfig cfg, out string error)
    {
        error = "";
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                error = "UNEXPECTED_JSON_SHAPE";
                return Array.Empty<NewsEvent>();
            }

            var result = new List<NewsEvent>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var eventName = ReadString(el, "Event") ?? ReadString(el, "Category") ?? "";
                var currency  = (ReadString(el, "Country") ?? "").ToUpperInvariant() switch
                {
                    "UNITED STATES" or "US" or "USA" => "USD",
                    var s => s,
                };
                var impact    = ReadString(el, "Importance") ?? ReadString(el, "Impact") ?? "";
                var dateRaw   = ReadString(el, "Date") ?? ReadString(el, "DateTime") ?? "";
                var rawId     = ReadString(el, "CalendarId") ?? ReadString(el, "Id") ?? "";

                if (!DateTime.TryParse(dateRaw, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var eventTime))
                    continue;

                var eventTimeUtc = eventTime.Kind == DateTimeKind.Utc
                    ? eventTime
                    : DateTime.SpecifyKind(eventTime, DateTimeKind.Utc);

                var category = NewsEventCategoryClassifier.ClassifyWithKeywords(eventName, cfg.BlockEventKeywords);

                result.Add(new NewsEvent
                {
                    EventName    = eventName,
                    Currency     = currency,
                    Impact       = impact,
                    EventTimeUtc = eventTimeUtc,
                    Provider     = "TradingEconomics",
                    RawId        = rawId,
                    Category     = category,
                });
            }
            return result;
        }
        catch (Exception ex)
        {
            error = $"PARSE_ERROR: {ex.GetType().Name}: {ex.Message}";
            return Array.Empty<NewsEvent>();
        }
    }

    static string? ReadString(JsonElement el, string key)
    {
        if (el.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }
}
