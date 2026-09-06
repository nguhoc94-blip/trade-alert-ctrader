using cAlgo.API;

namespace TradeAlert.BacktestRobot.Execution;

public enum VirtualXrFillMode
{
    SignalBarClose,
    NextBarOpen,
    ActualMarketClose,
}

public sealed class VirtualXrConfig
{
    public bool Enabled { get; init; }
    public double TriggerR { get; init; } = 1.25;
    public string ExitTimeframeToken { get; init; } = "15";
    public VirtualXrFillMode FillMode { get; init; } = VirtualXrFillMode.SignalBarClose;
    public bool UseBrokerTp { get; init; }
    public double EmergencyBrokerTpR { get; init; }

    public bool PureVirtualMode => Enabled && !UseBrokerTp;

    public static VirtualXrConfig Parse(
        bool enabled,
        double triggerR,
        string exitTf,
        string fillModeRaw,
        bool useBrokerTp,
        double emergencyBrokerTpR) => new()
    {
        Enabled = enabled,
        TriggerR = triggerR > 0 ? triggerR : 1.25,
        ExitTimeframeToken = string.IsNullOrWhiteSpace(exitTf) ? "15" : exitTf.Trim(),
        FillMode = ParseFillMode(fillModeRaw),
        UseBrokerTp = useBrokerTp,
        EmergencyBrokerTpR = emergencyBrokerTpR > 0 ? emergencyBrokerTpR : 0,
    };

    public static VirtualXrFillMode ParseFillMode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return VirtualXrFillMode.SignalBarClose;

        return raw.Trim().ToLowerInvariant() switch
        {
            "nextbaropen" => VirtualXrFillMode.NextBarOpen,
            "actualmarketclose" => VirtualXrFillMode.ActualMarketClose,
            _ => VirtualXrFillMode.SignalBarClose,
        };
    }

    public static string CloseReasonForFillMode(VirtualXrFillMode mode) => mode switch
    {
        VirtualXrFillMode.NextBarOpen => "XRBarCloseNextOpen",
        VirtualXrFillMode.ActualMarketClose => "XRBarCloseActual",
        _ => "XRBarClose",
    };
}

public static class VirtualXrTimeframeParser
{
    public static TimeFrame ToTimeFrame(string token)
    {
        var t = token.Trim();
        return t switch
        {
            "1" => TimeFrame.Minute,
            "5" => TimeFrame.Minute5,
            "15" => TimeFrame.Minute15,
            "30" => TimeFrame.Minute30,
            "60" => TimeFrame.Hour,
            "240" => TimeFrame.Hour4,
            "D" => TimeFrame.Daily,
            _ => int.TryParse(t, out var mins) && mins > 0
                ? TimeFrame.Minute15
                : TimeFrame.Minute15,
        };
    }
}
