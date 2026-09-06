namespace TradeAlert.Core.Drawing;

/// <summary>Runtime style từ host inputs (Pine <c>groupStyle</c> + opacity).</summary>
public static class Loop6StylePalette
{
    public static int LabelOpacityPercent { get; set; } = 50;

    public static uint HighLabelArgb   { get; set; } = 0xFF32AC45u;
    public static uint LowLabelArgb    { get; set; } = 0xFFDE3B22u;
    public static uint HhLabelArgb     { get; set; } = 0xFF19A11Cu;
    public static uint LlLabelArgb     { get; set; } = 0xFFE63419u;
    public static uint MainLabelArgb   { get; set; } = 0xFFFFFF00u;
    public static uint FakeLabelArgb   { get; set; } = 0xFF2196F3u;
    public static uint BrokenLabelArgb { get; set; } = 0xFF808080u;

    /// <summary>Pine <c>inpBoxHighColor</c> rgb(197,89,89).</summary>
    public static byte KeyHighBoxR { get; set; } = 197;
    public static byte KeyHighBoxG { get; set; } = 89;
    public static byte KeyHighBoxB { get; set; } = 89;

    /// <summary>Pine <c>inpBoxLowColor</c> rgb(72,189,78).</summary>
    public static byte KeyLowBoxR { get; set; } = 72;
    public static byte KeyLowBoxG { get; set; } = 189;
    public static byte KeyLowBoxB { get; set; } = 78;

    /// <summary>Pine <c>inpMainCColor</c> rgb(255,235,59).</summary>
    public static byte KeyMainBoxR { get; set; } = 255;
    public static byte KeyMainBoxG { get; set; } = 235;
    public static byte KeyMainBoxB { get; set; } = 59;

    public static uint LabelFromRgb(byte r, byte g, byte b) =>
        PineColors.WithTransparency(r, g, b, LabelOpacityPercent);

    /// <summary>Opaque border ARGB from host key-box RGB (Pine uses inpBox*Color for border).</summary>
    public static uint KeyHighBorderOpaque() =>
        0xFF000000u | ((uint)KeyHighBoxR << 16) | ((uint)KeyHighBoxG << 8) | KeyHighBoxB;

    public static uint KeyLowBorderOpaque() =>
        0xFF000000u | ((uint)KeyLowBoxR << 16) | ((uint)KeyLowBoxG << 8) | KeyLowBoxB;

    public static uint KeyMainBorderOpaque() =>
        0xFF000000u | ((uint)KeyMainBoxR << 16) | ((uint)KeyMainBoxG << 8) | KeyMainBoxB;

    /// <summary>Pine build_ob_box: obType=1 → inpBoxHighColor; obType=-1 → inpBoxLowColor.</summary>
    public static (uint Fill, uint Border) ObColorsFor(int obType) =>
        obType == 1
            ? (PineColors.WithTransparency(KeyHighBoxR, KeyHighBoxG, KeyHighBoxB, PineColors.ObBoxOpacityPercent),
               KeyHighBorderOpaque())
            : (PineColors.WithTransparency(KeyLowBoxR, KeyLowBoxG, KeyLowBoxB, PineColors.ObBoxOpacityPercent),
               KeyLowBorderOpaque());

    public static uint HighLabel()   => LabelFromOpaque(HighLabelArgb);
    public static uint LowLabel()    => LabelFromOpaque(LowLabelArgb);
    public static uint HhLabel()     => LabelFromOpaque(HhLabelArgb);
    public static uint LlLabel()     => LabelFromOpaque(LlLabelArgb);
    public static uint MainLabel()   => LabelFromOpaque(MainLabelArgb);
    public static uint FakeLabel()   => LabelFromOpaque(FakeLabelArgb);
    public static uint BrokenLabel() => LabelFromOpaque(BrokenLabelArgb);
    public static uint DOrangeLabel() => LabelFromOpaque(PineColors.DOrange);

    static uint LabelFromOpaque(uint opaqueArgb)
    {
        var r = (byte)((opaqueArgb >> 16) & 0xFF);
        var g = (byte)((opaqueArgb >> 8) & 0xFF);
        var b = (byte)(opaqueArgb & 0xFF);
        return LabelFromRgb(r, g, b);
    }
}
