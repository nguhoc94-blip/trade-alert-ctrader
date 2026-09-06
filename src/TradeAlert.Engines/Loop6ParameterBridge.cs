using cAlgo.API;
using TradeAlert.Core.Drawing;
using TradeAlert.Core.Engines;

namespace TradeAlert.Indicator.Host;

/// <summary>Maps <see cref="TradeAlertLoop6Host"/> parameters → <see cref="PineStateEngine"/> (Pine input parity).</summary>
public static class Loop6ParameterBridge
{
    public static void Apply(TradeAlertIndicator shell, in Loop6HostParameterSnapshot p)
    {
        var s = shell.State;
        var f = s.Filter;

        s.LookbackBars = p.LookbackBars;

        s.ShowObBox       = p.ShowObBox;
        s.HideObLoseZin   = p.HideObLoseZin;
        s.HideObZin       = p.HideObZin;
        s.ShowFakeSwings  = p.ShowFakeSwings;
        s.ShowLockedSwings = p.ShowLockedSwings;
        s.ShowActiveSwings = p.ShowActiveSwings;
        s.ShowSwingPushDebug = p.ShowSwingPushDebug;
        s.ShowSwingPeakMissLabels = p.ShowSwingPeakMissLabels;
        s.ShowWickNoiseDebugLabels = p.ShowWickNoiseDebugLabels;
        s.ShowObBoxMissingDebug = p.ShowObBoxMissingDebug;
        s.LockSwingCount  = p.LockSwingCount;
        PineColors.ObBoxOpacityPercent = p.ObBoxOpacity;

        s.UseObConfirm         = p.UseObConfirm;
        s.UseWickNoiseFilter   = p.UseWickNoiseFilter;
        s.WickAvgLen           = p.WickAvgLen;
        s.UseLtfWickConfirm    = p.UseLtfWickConfirm;
        s.LtfConfirmRatio      = p.LtfConfirmRatio;
        s.EnableKeylevel       = p.EnableKeylevel;
        s.KeylevelUseAtrRule   = p.KeylevelUseAtrRule;
        s.UsePullbackFilter    = p.UseNewPullbackFilter;
        s.UseMicroSwingRule    = p.UseMicroSwingRule;
        s.UseGapMerge          = p.UseGapMerge;
        s.GapFilterAtr         = p.GapFilterAtr;
        s.StrongAtrMult        = p.StrongAtrMult;
        s.MediumAtrMult        = p.MediumAtrMult;
        s.BreakR3MaxK          = p.BreakR3MaxK;

        f.BMaxLagBars            = p.BMaxLagBars;
        f.CdMaxLagBars           = p.CdMaxLagBars;
        f.StrongAtrMult          = p.StrongAtrMult;
        f.MediumAtrMult          = p.MediumAtrMult;
        f.UseExtremePickRule     = p.UseExtremePickRule;
        f.UseDojiForceBC         = p.UseDojiForceBC;
        f.DojiBodyMaxRatio       = p.DojiBodyMaxRatio;
        f.DojiForceWickRatioMin  = p.DojiForceWickRatioMin;
        f.DojiForceBodyZoneMax   = p.DojiForceBodyZoneMax;
        f.DojiWickDomRatio       = p.DojiWickDomRatio;

        s.LtfBufferSize   = p.LtfBufferSize;
        s.ObScanBars      = p.ObScanBars;
        s.MaxObs          = p.MaxObs;
        s.MinObBodyRatio  = p.MinObBodyRatio;
        s.MaxDojiBodyRatio = p.MaxDojiBodyRatio;

        s.KeylevelLookback      = p.KeylevelLookback;
        s.KeylevelAvgBodyLen    = p.KeylevelAvgBodyLen;
        s.KeylevelMinBodyMult   = p.KeylevelMinBodyMult;
        s.KeylevelAtrMult       = p.KeylevelAtrMult;
        s.KeylevelAtrLen        = p.KeylevelAtrLen;
        s.MaxKeylevelKeep       = p.MaxKeylevelKeep;

        s.ShowKeyBorder              = p.ShowKeyBorder;
        s.KeyBorderWidth             = p.KeyBorderWidth;
        KeyLevelVisual.InvertBrokenKeyColor = p.InvertBrokenKeyColor;
        s.KeyOpacityActivePercent    = p.KeyOpacityActive;
        s.KeyOpacityBrokenPercent    = p.KeyOpacityBroken;

        ApplyStylePalette(p);
    }

    static void ApplyStylePalette(in Loop6HostParameterSnapshot p)
    {
        Loop6StylePalette.LabelOpacityPercent = p.LabelOpacity;
        Loop6StylePalette.HighLabelArgb   = p.HighLabelArgb;
        Loop6StylePalette.LowLabelArgb    = p.LowLabelArgb;
        Loop6StylePalette.HhLabelArgb     = p.HhLabelArgb;
        Loop6StylePalette.LlLabelArgb     = p.LlLabelArgb;
        Loop6StylePalette.MainLabelArgb   = p.MainLabelArgb;
        Loop6StylePalette.FakeLabelArgb   = p.FakeLabelArgb;
        Loop6StylePalette.BrokenLabelArgb = p.BrokenLabelArgb;

        Loop6ColorUtil.SetRgbFromColor(p.HighKeyBoxColor, 0xFFC55959u, out var hr, out var hg, out var hb);
        Loop6StylePalette.KeyHighBoxR = hr;
        Loop6StylePalette.KeyHighBoxG = hg;
        Loop6StylePalette.KeyHighBoxB = hb;

        Loop6ColorUtil.SetRgbFromColor(p.LowKeyBoxColor, 0xFF48BD4Eu, out var lr, out var lg, out var lb);
        Loop6StylePalette.KeyLowBoxR = lr;
        Loop6StylePalette.KeyLowBoxG = lg;
        Loop6StylePalette.KeyLowBoxB = lb;

        Loop6ColorUtil.SetRgbFromColor(p.MainCKeyBoxColor, 0xFFFFEB3Bu, out var mr, out var mg, out var mb);
        Loop6StylePalette.KeyMainBoxR = mr;
        Loop6StylePalette.KeyMainBoxG = mg;
        Loop6StylePalette.KeyMainBoxB = mb;
    }
}

/// <summary>Snapshot of host [Parameter] properties for bridge.</summary>
public readonly struct Loop6HostParameterSnapshot
{
    public int LookbackBars { get; init; }
    public bool EnableKeylevel { get; init; }
    public bool ShowObBox { get; init; }
    public bool HideObLoseZin { get; init; }
    public bool HideObZin { get; init; }
    public bool ShowFakeSwings { get; init; }
    public bool ShowLockedSwings { get; init; }
    public bool ShowActiveSwings { get; init; }
    public bool ShowSwingPushDebug { get; init; }
    public bool ShowSwingPeakMissLabels { get; init; }
    public bool ShowWickNoiseDebugLabels { get; init; }
    public bool ShowObBoxMissingDebug { get; init; }
    public int LockSwingCount { get; init; }
    public int ObBoxOpacity { get; init; }
    public bool UseObConfirm { get; init; }
    public double ObAtrMultiplier { get; init; }
    public bool UseWickNoiseFilter { get; init; }
    public int WickAvgLen { get; init; }
    public bool UseLtfWickConfirm { get; init; }
    public double LtfConfirmRatio { get; init; }
    public bool KeylevelUseAtrRule { get; init; }
    public bool UseNewPullbackFilter { get; init; }
    public int BMaxLagBars { get; init; }
    public int CdMaxLagBars { get; init; }
    public double StrongAtrMult { get; init; }
    public double MediumAtrMult { get; init; }
    public bool UseExtremePickRule { get; init; }
    public bool UseGapMerge { get; init; }
    public double GapFilterAtr { get; init; }
    public bool UseMicroSwingRule { get; init; }
    public bool UseDojiForceBC { get; init; }
    public double DojiBodyMaxRatio { get; init; }
    public double DojiForceWickRatioMin { get; init; }
    public double DojiForceBodyZoneMax { get; init; }
    public double DojiWickDomRatio { get; init; }
    public int BreakR3MaxK { get; init; }
    public int LtfBufferSize { get; init; }
    public int ObScanBars { get; init; }
    public int MaxObs { get; init; }
    public double MinObBodyRatio { get; init; }
    public double MaxDojiBodyRatio { get; init; }
    public int KeylevelLookback { get; init; }
    public int KeylevelAvgBodyLen { get; init; }
    public double KeylevelMinBodyMult { get; init; }
    public double KeylevelAtrMult { get; init; }
    public int KeylevelAtrLen { get; init; }
    public int MaxKeylevelKeep { get; init; }
    public bool ShowKeyBorder { get; init; }
    public int KeyBorderWidth { get; init; }
    public bool InvertBrokenKeyColor { get; init; }
    public int KeyOpacityActive { get; init; }
    public int KeyOpacityBroken { get; init; }
    public int LabelOpacity { get; init; }
    public uint HighLabelArgb { get; init; }
    public uint LowLabelArgb { get; init; }
    public uint HhLabelArgb { get; init; }
    public uint LlLabelArgb { get; init; }
    public uint MainLabelArgb { get; init; }
    public uint FakeLabelArgb { get; init; }
    public uint BrokenLabelArgb { get; init; }
    public Color HighKeyBoxColor { get; init; }
    public Color LowKeyBoxColor { get; init; }
    public Color MainCKeyBoxColor { get; init; }
}
