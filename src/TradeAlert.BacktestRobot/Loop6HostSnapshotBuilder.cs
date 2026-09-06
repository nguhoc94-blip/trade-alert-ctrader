using cAlgo.API;
using TradeAlert.Indicator.Host;

namespace TradeAlert.BacktestRobot;

/// <summary>
/// Shared helper that fills the visual-only fields of a <see cref="Loop6HostParameterSnapshot"/>
/// with their indicator defaults. These fields are NOT exposed as [Parameter] on the headless
/// signal hosts (scanner / backtest bot) — they only affect rendering the host never performs.
///
/// Mirrors <c>Loop6MultiSymbolEmailScanner.FillStyleDefaults</c> so the backtest cBot produces
/// an identical engine snapshot without modifying the scanner.
/// </summary>
public static class Loop6HostSnapshotBuilder
{
    /// <summary>Return <paramref name="core"/> with all visual/style fields set to indicator defaults.</summary>
    public static Loop6HostParameterSnapshot WithStyleDefaults(Loop6HostParameterSnapshot core) => core with
    {
        ShowObBox          = true,
        HideObLoseZin      = true,
        HideObZin          = false,
        ShowFakeSwings     = false,
        ShowLockedSwings   = false,
        ShowActiveSwings   = false,
        ShowSwingPushDebug = false,
        ObBoxOpacity       = 30,
        ShowKeyBorder      = true,
        KeyBorderWidth     = 1,
        InvertBrokenKeyColor = true,
        KeyOpacityActive   = 50,
        KeyOpacityBroken   = 30,
        LabelOpacity       = 50,
        HighLabelArgb    = 0xFF008000u,
        LowLabelArgb     = 0xFFFF4500u,
        HhLabelArgb      = 0xFF008000u,
        LlLabelArgb      = 0xFFFF0000u,
        MainLabelArgb    = 0xFFFFFF00u,
        FakeLabelArgb    = 0xFF0000FFu,
        BrokenLabelArgb  = 0xFF808080u,
        HighKeyBoxColor  = Color.FromArgb(205, 92, 92),   // IndianRed
        LowKeyBoxColor   = Color.FromArgb(50, 205, 50),   // LimeGreen
        MainCKeyBoxColor = Color.FromArgb(255, 215, 0),   // Gold
    };
}
