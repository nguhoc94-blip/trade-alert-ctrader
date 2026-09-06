namespace TradeAlert.BacktestRobot.Execution.News;

/// <summary>A single economic calendar event fetched from a provider or loaded from cache.</summary>
public sealed class NewsEvent
{
    public string EventName    { get; init; } = "";
    public string Currency     { get; init; } = "";
    public string Impact       { get; init; } = "";
    public DateTime EventTimeUtc { get; init; }
    public string Provider     { get; init; } = "";
    public string RawId        { get; init; } = "";

    /// <summary>Category derived from EventName via <see cref="NewsEventCategoryClassifier"/>.</summary>
    public NewsEventCategory Category { get; init; } = NewsEventCategory.Other;

    // Per-event window overrides (from historical CSV). Null = use NewsFilterConfig defaults.
    public double? BlockBeforeMinOverride        { get; init; }
    public double? BlockAfterMinOverride         { get; init; }
    public double? ForceCloseBeforeMinOverride   { get; init; }

    public override string ToString() =>
        $"{Currency} {EventName} @ {EventTimeUtc:yyyy-MM-dd HH:mm} UTC ({Impact}) [{Category}]";
}

/// <summary>Stateless keyword classifier — CPI or NFP, everything else is Other.</summary>
public static class NewsEventCategoryClassifier
{
    static readonly string[] CpiKeywords =
    {
        "CPI", "Consumer Price Index",
    };

    static readonly string[] NfpKeywords =
    {
        "NFP", "Nonfarm", "Non-Farm", "Non Farm", "Payrolls", "Employment Situation",
    };

    public static NewsEventCategory Classify(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            return NewsEventCategory.Other;

        foreach (var kw in NfpKeywords)
            if (eventName.Contains(kw, StringComparison.OrdinalIgnoreCase))
                return NewsEventCategory.Nfp;

        foreach (var kw in CpiKeywords)
            if (eventName.Contains(kw, StringComparison.OrdinalIgnoreCase))
                return NewsEventCategory.Cpi;

        return NewsEventCategory.Other;
    }

    /// <summary>
    /// Classify using user-configured keyword list (comma-separated).
    /// CPI/NFP categories are detected by checking if any matching keyword is a CPI or NFP keyword.
    /// Falls back to Classify(eventName) if configuredKeywords is empty.
    /// </summary>
    public static NewsEventCategory ClassifyWithKeywords(string eventName, IReadOnlyList<string> configuredKeywords)
    {
        if (configuredKeywords.Count == 0)
            return Classify(eventName);

        foreach (var kw in configuredKeywords)
        {
            if (!eventName.Contains(kw, StringComparison.OrdinalIgnoreCase))
                continue;

            // Determine which built-in category the matching keyword belongs to.
            foreach (var nfpKw in NfpKeywords)
                if (kw.Contains(nfpKw, StringComparison.OrdinalIgnoreCase) || nfpKw.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    return NewsEventCategory.Nfp;

            foreach (var cpiKw in CpiKeywords)
                if (kw.Contains(cpiKw, StringComparison.OrdinalIgnoreCase) || cpiKw.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    return NewsEventCategory.Cpi;
        }

        return NewsEventCategory.Other;
    }
}
