namespace TradeAlert.BacktestRobot.Execution;

/// <summary>How the initial take-profit price was chosen for a trade plan.</summary>
public enum TakeProfitSource
{
    RewardRisk = 0,
    SwingCEdge = 1,
}
