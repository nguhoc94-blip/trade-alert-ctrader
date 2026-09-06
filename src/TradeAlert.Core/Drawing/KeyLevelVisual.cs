using TradeAlert.Core.Models.KeyLevel;

namespace TradeAlert.Core.Drawing;

/// <summary>Pine: <c>f_get_broken_key_color</c>, <c>f_sync_keybox_with_flag</c> broken branch, stable chart object keys.</summary>
public static class KeyLevelVisual
{
    /// <summary>Pine <c>inpInvertBrokenKeyColor</c> default true.</summary>
    public static bool InvertBrokenKeyColor { get; set; } = true;

    /// <summary>Stable cTrader object name — chart-local ticks (must match host + emitters).</summary>
    public static string ChartObjectName(KeyBoxSpec spec) =>
        $"kb_{spec.LeftTimeChartLocal.Ticks}_{spec.Top:F5}";

    /// <summary>Pine fill = inverted box color; border on cTrader = gray + dashed (Pine: border = brokenCol dashed).</summary>
    public const uint BrokenBorderArgb = 0xFF505050u;

    /// <summary>cTrader: nét đứt thưa + viền đậm hơn active (thickness 1 solid).</summary>
    public const int BrokenBorderThickness = 2;

    public static (uint FillArgb, uint BorderArgb) BrokenColors(int pivotType, int brokenTranspPercent = PineColors.DefaultKeyOpacityBrokenPercent) =>
        InvertBrokenKeyColor
            ? pivotType == 1
                ? (PineColors.WithTransparency(Loop6StylePalette.KeyLowBoxR, Loop6StylePalette.KeyLowBoxG, Loop6StylePalette.KeyLowBoxB, brokenTranspPercent), BrokenBorderArgb)
                : (PineColors.WithTransparency(Loop6StylePalette.KeyHighBoxR, Loop6StylePalette.KeyHighBoxG, Loop6StylePalette.KeyHighBoxB, brokenTranspPercent), BrokenBorderArgb)
            : pivotType == 1
                ? (PineColors.WithTransparency(Loop6StylePalette.KeyHighBoxR, Loop6StylePalette.KeyHighBoxG, Loop6StylePalette.KeyHighBoxB, brokenTranspPercent), BrokenBorderArgb)
                : (PineColors.WithTransparency(Loop6StylePalette.KeyLowBoxR, Loop6StylePalette.KeyLowBoxG, Loop6StylePalette.KeyLowBoxB, brokenTranspPercent), BrokenBorderArgb);

    public static (uint FillArgb, uint BorderArgb) ActiveColors(int pivotType, int activeTranspPercent = PineColors.DefaultKeyOpacityActivePercent) =>
        pivotType == 1
            ? (PineColors.WithTransparency(Loop6StylePalette.KeyHighBoxR, Loop6StylePalette.KeyHighBoxG, Loop6StylePalette.KeyHighBoxB, activeTranspPercent),
               Loop6StylePalette.KeyHighBorderOpaque())
            : (PineColors.WithTransparency(Loop6StylePalette.KeyLowBoxR, Loop6StylePalette.KeyLowBoxG, Loop6StylePalette.KeyLowBoxB, activeTranspPercent),
               Loop6StylePalette.KeyLowBorderOpaque());

    public static (uint FillArgb, uint BorderArgb) MainCColors(int activeTranspPercent = PineColors.DefaultKeyOpacityActivePercent) =>
        (PineColors.WithTransparency(Loop6StylePalette.KeyMainBoxR, Loop6StylePalette.KeyMainBoxG, Loop6StylePalette.KeyMainBoxB, activeTranspPercent),
         Loop6StylePalette.KeyMainBorderOpaque());

    /// <summary>
    /// Pine <c>box.set_right(t)</c> / <c>time[lag]</c>: cạnh phải tại <b>open</b> của bar <paramref name="pineRightBarIndex"/>.
    /// cTrader <c>ChartRectangle.Time2</c> kéo qua cả thân nến tại open đó → lùi 1 bar để khớp TV.
    /// </summary>
    public static int ToChartRightBarIndex(int pineRightBarIndex) =>
        pineRightBarIndex > 0 ? pineRightBarIndex - 1 : 0;
}
