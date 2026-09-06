using TradeAlert.BacktestRobot.Execution.News;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests.News;

public class NewsCategoryTests
{
    [Theory]
    [InlineData("CPI m/m",                  NewsEventCategory.Cpi)]
    [InlineData("Consumer Price Index",      NewsEventCategory.Cpi)]
    [InlineData("Core CPI",                  NewsEventCategory.Cpi)]
    [InlineData("NFP",                        NewsEventCategory.Nfp)]
    [InlineData("Nonfarm Payrolls",          NewsEventCategory.Nfp)]
    [InlineData("Non-Farm Payrolls",         NewsEventCategory.Nfp)]
    [InlineData("Non Farm Payrolls",         NewsEventCategory.Nfp)]
    [InlineData("Employment Situation",      NewsEventCategory.Nfp)]
    [InlineData("Payrolls",                  NewsEventCategory.Nfp)]
    public void Classify_MatchesExpected(string eventName, NewsEventCategory expected)
    {
        var result = NewsEventCategoryClassifier.Classify(eventName);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("FOMC Statement")]
    [InlineData("Fed Interest Rate Decision")]
    [InlineData("Fed Chair Powell Speech")]
    [InlineData("PCE Price Index")]
    [InlineData("Core PCE")]
    [InlineData("Retail Sales m/m")]
    [InlineData("ISM Manufacturing PMI")]
    [InlineData("GDP q/q")]
    [InlineData("Unemployment Claims")]
    public void Classify_NonTargetEventsAreOther(string eventName)
    {
        var result = NewsEventCategoryClassifier.Classify(eventName);
        Assert.Equal(NewsEventCategory.Other, result);
    }

    [Fact]
    public void ClassifyWithKeywords_UsesConfiguredList()
    {
        var keywords = new[] { "CPI", "NFP" };
        Assert.Equal(NewsEventCategory.Cpi, NewsEventCategoryClassifier.ClassifyWithKeywords("Core CPI m/m", keywords));
        Assert.Equal(NewsEventCategory.Nfp, NewsEventCategoryClassifier.ClassifyWithKeywords("US NFP Report", keywords));
        Assert.Equal(NewsEventCategory.Other, NewsEventCategoryClassifier.ClassifyWithKeywords("FOMC", keywords));
    }
}
