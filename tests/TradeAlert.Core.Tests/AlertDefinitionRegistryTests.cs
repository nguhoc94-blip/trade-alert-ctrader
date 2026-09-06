using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.Alerts;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class AlertDefinitionRegistryTests
{
    [Fact]
    public void Registry_HasExactlyFifteenDefinitions()
    {
        var eng = new AlertEngine();
        // 12 Pine + PhaKhungLon + bot HLNG M15 pair
        Assert.Equal(15, eng.Registry.Count);
    }

    [Fact]
    public void Registry_AllEnumValuesPresent_NoDuplicateId()
    {
        var eng = new AlertEngine();
        var seen = new HashSet<AlertConditionId>();
        foreach (var e in Enum.GetValues<AlertConditionId>())
        {
            Assert.True(eng.Registry.ContainsKey(e));
            Assert.True(seen.Add(e));
            var d = eng.Registry[e];
            Assert.Equal(e, d.Id);
            Assert.False(string.IsNullOrWhiteSpace(d.Title));
            Assert.False(string.IsNullOrWhiteSpace(d.PineConditionSymbol));
            Assert.False(string.IsNullOrWhiteSpace(d.CSharpTargetHint));
            Assert.False(string.IsNullOrWhiteSpace(d.DuplicateKeyFieldDescriptor));
            Assert.False(string.IsNullOrWhiteSpace(d.LogFieldDescriptor));
        }
    }
}
