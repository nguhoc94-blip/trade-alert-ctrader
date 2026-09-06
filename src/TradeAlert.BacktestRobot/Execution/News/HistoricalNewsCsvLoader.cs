using System.Globalization;

namespace TradeAlert.BacktestRobot.Execution.News;

/// <summary>
/// Loads historical CPI/NFP events from a CSV file for offline backtest simulation.
///
/// CSV format (first line = header):
///   event_name,currency,event_time_utc,block_before_min,force_close_before_min,block_after_min,source
///
/// File path: MyDocuments/cAlgo/Data/Loop6News/loop6_historical_news_cache.csv
///
/// If the file does not exist, call <see cref="EnsureSampleCsvExists"/> to create a starter file
/// with known USD CPI/NFP release dates from 2021 to 2026. The actual minutes fields in the CSV
/// can be overridden per-event; <c>-1</c> means "use NewsFilterConfig defaults at runtime".
/// </summary>
public static class HistoricalNewsCsvLoader
{
    public const string HistoricalCsvFileName = "loop6_historical_news_cache.csv";

    public static string GetDefaultPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "cAlgo", "Data", "Loop6News");
        return Path.Combine(dir, HistoricalCsvFileName);
    }

    /// <summary>
    /// Load events from CSV. Returns empty list (with error message) on any failure.
    /// Events whose <c>event_name</c> does not match CPI/NFP keywords are skipped.
    /// </summary>
    public static IReadOnlyList<NewsEvent> Load(string csvPath, out string error)
    {
        error = "";
        if (!File.Exists(csvPath))
        {
            error = $"HISTORICAL_CSV_NOT_FOUND: {csvPath}";
            return Array.Empty<NewsEvent>();
        }

        try
        {
            var result = new List<NewsEvent>();
            var lines  = File.ReadAllLines(csvPath, System.Text.Encoding.UTF8);

            for (var i = 1; i < lines.Length; i++) // skip header
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                    continue;

                var cols = SplitCsv(line);
                if (cols.Length < 3)
                    continue;

                var eventName = cols[0].Trim();
                var currency  = cols[1].Trim().ToUpperInvariant();

                var rawDate = cols[2].Trim();
                // Try ISO-8601 round-trip first (handles 'Z' suffix), then assume UTC if no TZ info.
                if (!DateTime.TryParse(rawDate, null, DateTimeStyles.RoundtripKind, out var eventTime)
                    && !DateTime.TryParse(rawDate, null, DateTimeStyles.AssumeUniversal, out eventTime))
                    continue;

                var eventTimeUtc = DateTime.SpecifyKind(eventTime.ToUniversalTime(), DateTimeKind.Utc);

                double? blockBefore = ParseOptionalDouble(cols, 3);
                double? forceClose  = ParseOptionalDouble(cols, 4);
                double? blockAfter  = ParseOptionalDouble(cols, 5);
                var source = cols.Length > 6 ? cols[6].Trim() : "historical_csv";

                var category = NewsEventCategoryClassifier.Classify(eventName);
                if (category == NewsEventCategory.Other)
                    continue; // skip non-CPI/NFP rows

                result.Add(new NewsEvent
                {
                    EventName    = eventName,
                    Currency     = currency,
                    Impact       = "High",
                    EventTimeUtc = eventTimeUtc,
                    Provider     = source,
                    RawId        = $"hist_{eventTimeUtc:yyyyMMddHHmm}_{currency}",
                    Category     = category,
                    BlockBeforeMinOverride       = blockBefore,
                    BlockAfterMinOverride        = blockAfter,
                    ForceCloseBeforeMinOverride  = forceClose,
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            error = $"HISTORICAL_CSV_PARSE_ERROR: {ex.GetType().Name}: {ex.Message}";
            return Array.Empty<NewsEvent>();
        }
    }

    /// <summary>
    /// Creates the starter historical CSV file if it does not exist.
    /// Includes known USD CPI and NFP release dates from 2021 to mid-2026 at 13:30 UTC (08:30 ET).
    /// Dates are best-effort; verify against BLS/ForexFactory for exact historical times if needed.
    /// </summary>
    public static void EnsureSampleCsvExists(string csvPath)
    {
        if (File.Exists(csvPath))
            return;

        try
        {
            var dir = Path.GetDirectoryName(csvPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(csvPath, BuildSampleCsv(), System.Text.Encoding.UTF8);
        }
        catch
        {
            // Non-fatal — bot will fall back to fail-safe.
        }
    }

    // BLS-official release schedule — DST-correct UTC times.
    // 12:30 UTC = 08:30 EDT (summer), 13:30 UTC = 08:30 EST (winter).
    // 2025 note: October CPI/NFP not released separately; Sep releases delayed to Oct-24 / Nov-20.
    // Sourced from BLS release calendar; do NOT replace with approximations.
    static string BuildSampleCsv() => @"event_name,currency,event_time_utc,block_before_min,force_close_before_min,block_after_min,source
Employment Situation for December 2020,USD,2021-01-08T13:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for December 2020,USD,2021-01-13T13:30:00Z,60,15,30,BLS official schedule
Employment Situation for January 2021,USD,2021-02-05T13:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for January 2021,USD,2021-02-10T13:30:00Z,60,15,30,BLS official schedule
Employment Situation for February 2021,USD,2021-03-05T13:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for February 2021,USD,2021-03-10T13:30:00Z,60,15,30,BLS official schedule
Employment Situation for March 2021,USD,2021-04-02T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for March 2021,USD,2021-04-13T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for April 2021,USD,2021-05-07T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for April 2021,USD,2021-05-12T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for May 2021,USD,2021-06-04T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for May 2021,USD,2021-06-10T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for June 2021,USD,2021-07-02T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for June 2021,USD,2021-07-13T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for July 2021,USD,2021-08-06T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for July 2021,USD,2021-08-11T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for August 2021,USD,2021-09-03T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for August 2021,USD,2021-09-14T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for September 2021,USD,2021-10-08T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for September 2021,USD,2021-10-13T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for October 2021,USD,2021-11-05T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for October 2021,USD,2021-11-10T13:30:00Z,60,15,30,BLS official schedule
Employment Situation for November 2021,USD,2021-12-03T13:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for November 2021,USD,2021-12-10T13:30:00Z,60,15,30,BLS official schedule
Employment Situation for December 2021,USD,2022-01-07T13:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for December 2021,USD,2022-01-12T13:30:00Z,60,15,30,BLS official schedule
Employment Situation for January 2022,USD,2022-02-04T13:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for January 2022,USD,2022-02-10T13:30:00Z,60,15,30,BLS official schedule
Employment Situation for February 2022,USD,2022-03-04T13:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for February 2022,USD,2022-03-10T13:30:00Z,60,15,30,BLS official schedule
Employment Situation for March 2022,USD,2022-04-01T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for March 2022,USD,2022-04-12T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for April 2022,USD,2022-05-06T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for April 2022,USD,2022-05-11T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for May 2022,USD,2022-06-03T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for May 2022,USD,2022-06-10T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for June 2022,USD,2022-07-08T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for June 2022,USD,2022-07-13T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for July 2022,USD,2022-08-05T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for July 2022,USD,2022-08-10T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for August 2022,USD,2022-09-02T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for August 2022,USD,2022-09-13T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for September 2022,USD,2022-10-07T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for September 2022,USD,2022-10-13T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for October 2022,USD,2022-11-04T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for October 2022,USD,2022-11-10T13:30:00Z,60,15,30,BLS official schedule
Employment Situation for November 2022,USD,2022-12-02T13:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for November 2022,USD,2022-12-13T13:30:00Z,60,15,30,BLS official schedule
Employment Situation for December 2022,USD,2023-01-06T13:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for December 2022,USD,2023-01-12T13:30:00Z,60,15,30,BLS official schedule
Employment Situation for January 2023,USD,2023-02-03T13:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for January 2023,USD,2023-02-14T13:30:00Z,60,15,30,BLS official schedule
Employment Situation for February 2023,USD,2023-03-10T13:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for February 2023,USD,2023-03-14T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for March 2023,USD,2023-04-07T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for March 2023,USD,2023-04-12T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for April 2023,USD,2023-05-05T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for April 2023,USD,2023-05-10T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for May 2023,USD,2023-06-02T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for May 2023,USD,2023-06-13T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for June 2023,USD,2023-07-07T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for June 2023,USD,2023-07-12T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for July 2023,USD,2023-08-04T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for July 2023,USD,2023-08-10T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for August 2023,USD,2023-09-01T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for August 2023,USD,2023-09-13T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for September 2023,USD,2023-10-06T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for September 2023,USD,2023-10-12T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for October 2023,USD,2023-11-03T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for October 2023,USD,2023-11-14T13:30:00Z,60,15,30,BLS official schedule
Employment Situation for November 2023,USD,2023-12-08T13:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for November 2023,USD,2023-12-12T13:30:00Z,60,15,30,BLS official schedule
Employment Situation for December 2023,USD,2024-01-05T13:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for December 2023,USD,2024-01-11T13:30:00Z,60,15,30,BLS official schedule
Employment Situation for January 2024,USD,2024-02-02T13:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for January 2024,USD,2024-02-13T13:30:00Z,60,15,30,BLS official schedule
Employment Situation for February 2024,USD,2024-03-08T13:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for February 2024,USD,2024-03-12T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for March 2024,USD,2024-04-05T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for March 2024,USD,2024-04-10T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for April 2024,USD,2024-05-03T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for April 2024,USD,2024-05-15T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for May 2024,USD,2024-06-07T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for May 2024,USD,2024-06-12T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for June 2024,USD,2024-07-05T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for June 2024,USD,2024-07-11T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for July 2024,USD,2024-08-02T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for July 2024,USD,2024-08-14T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for August 2024,USD,2024-09-06T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for August 2024,USD,2024-09-11T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for September 2024,USD,2024-10-04T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for September 2024,USD,2024-10-10T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for October 2024,USD,2024-11-01T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for October 2024,USD,2024-11-13T13:30:00Z,60,15,30,BLS official schedule
Employment Situation for November 2024,USD,2024-12-06T13:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for November 2024,USD,2024-12-11T13:30:00Z,60,15,30,BLS official schedule
Employment Situation for December 2024,USD,2025-01-10T13:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for December 2024,USD,2025-01-15T13:30:00Z,60,15,30,BLS official schedule
Employment Situation for January 2025,USD,2025-02-07T13:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for January 2025,USD,2025-02-12T13:30:00Z,60,15,30,BLS official schedule
Employment Situation for February 2025,USD,2025-03-07T13:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for February 2025,USD,2025-03-12T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for March 2025,USD,2025-04-04T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for March 2025,USD,2025-04-10T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for April 2025,USD,2025-05-02T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for April 2025,USD,2025-05-13T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for May 2025,USD,2025-06-06T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for May 2025,USD,2025-06-11T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for June 2025,USD,2025-07-03T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for June 2025,USD,2025-07-15T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for July 2025,USD,2025-08-01T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for July 2025,USD,2025-08-12T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for August 2025,USD,2025-09-05T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for August 2025,USD,2025-09-11T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for September 2025,USD,2025-10-24T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for September 2025,USD,2025-11-20T13:30:00Z,60,15,30,BLS official schedule
Employment Situation for November 2025,USD,2025-12-16T13:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for November 2025,USD,2025-12-18T13:30:00Z,60,15,30,BLS official schedule
Employment Situation for December 2025,USD,2026-01-09T13:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for December 2025,USD,2026-01-13T13:30:00Z,60,15,30,BLS official schedule
Employment Situation for January 2026,USD,2026-02-11T13:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for January 2026,USD,2026-02-13T13:30:00Z,60,15,30,BLS official schedule
Employment Situation for February 2026,USD,2026-03-06T13:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for February 2026,USD,2026-03-11T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for March 2026,USD,2026-04-03T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for March 2026,USD,2026-04-10T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for April 2026,USD,2026-05-08T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for April 2026,USD,2026-05-12T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for May 2026,USD,2026-06-05T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for May 2026,USD,2026-06-10T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for June 2026,USD,2026-07-02T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for June 2026,USD,2026-07-14T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for July 2026,USD,2026-08-07T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for July 2026,USD,2026-08-12T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for August 2026,USD,2026-09-04T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for August 2026,USD,2026-09-11T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for September 2026,USD,2026-10-02T12:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for September 2026,USD,2026-10-14T12:30:00Z,60,15,30,BLS official schedule
Employment Situation for October 2026,USD,2026-11-06T13:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for October 2026,USD,2026-11-10T13:30:00Z,60,15,30,BLS official schedule
Employment Situation for November 2026,USD,2026-12-04T13:30:00Z,60,15,30,BLS official schedule
Consumer Price Index for November 2026,USD,2026-12-10T13:30:00Z,60,15,30,BLS official schedule
";

    // Unused helper stubs removed — data is now a verbatim string literal (BLS official).

    static string[] SplitCsv(string line)
    {
        // Simple CSV split — does not handle quoted commas (sufficient for this format).
        return line.Split(',');
    }

    static double? ParseOptionalDouble(string[] cols, int idx)
    {
        if (cols.Length <= idx)
            return null;
        if (double.TryParse(cols[idx].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
            && v >= 0)
            return v;
        return null; // -1 or non-parseable = use config default
    }
}
