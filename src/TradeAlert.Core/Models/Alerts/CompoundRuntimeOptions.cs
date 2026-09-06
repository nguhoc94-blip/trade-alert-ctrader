namespace TradeAlert.Core.Models.Alerts;

public sealed class CompoundRuntimeOptions
{
    public CompoundEvalSyncMode SyncMode { get; init; } = CompoundEvalSyncMode.PineEventWindow;
    public int EventValidBars { get; init; } = 3;
    public int ChartBarIndex { get; init; } = -1;
}
