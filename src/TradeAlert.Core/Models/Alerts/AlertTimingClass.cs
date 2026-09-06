namespace TradeAlert.Core.Models.Alerts;

public enum AlertTimingClass
{
    BarCloseM5Event,
    BarCloseM15Event,
    RealtimeTouchBar0,
    RealtimeFilterBar0,
    RealtimeFilterBar0WithM15CloseEdge,
    /// <summary>Pine <c>condPhaKhungLon</c> — mọi TF, mỗi <c>isConfirmed</c> bar.</summary>
    BarCloseAnyTfPhaKhungLon,
}
