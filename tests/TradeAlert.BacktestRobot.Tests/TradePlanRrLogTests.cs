using TradeAlert.BacktestRobot.Execution;
using TradeAlert.Core.Models.Alerts;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests;

public class TradePlanRrLogTests
{
    [Fact]
    public void AppliedRr_IsTpOverRisk()
    {
        Assert.Equal(2.0, TradePlanRrLog.AppliedRr(10, 20), 6);
        Assert.Equal(1.5, TradePlanRrLog.AppliedRr(10, 15), 6);
    }

    [Fact]
    public void FormatPlanRrCheck_ShowsRrAndCfg()
    {
        var plan = new TradePlan
        {
            SlotIndex = 2,
            EntryLimit = 100,
            StopLoss = 97,
            TakeProfit = 106,
            StopLossPips = 30,
            TakeProfitPips = 60,
            IsBuy = true,
        };

        var log = TradePlanRrLog.FormatPlanRrCheck(in plan, ruleSlot: 2, configuredRewardRisk: 2.0);

        Assert.Contains("PLAN RR CHECK rule=R3", log);
        Assert.Contains("riskPips=30", log);
        Assert.Contains("tpPips=60", log);
        Assert.Contains("rr=2", log);
        Assert.Contains("cfgRR=2", log);
    }
}
