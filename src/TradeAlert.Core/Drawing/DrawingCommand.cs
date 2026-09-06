using System;
using TradeAlert.Core.Models.KeyLevel;

namespace TradeAlert.Core.Drawing;

public enum DrawingCommandKind
{
    AddLabel,
    AddLine,
    DelLabel,
    DelLine,
    EnqueueKeyBoxSpec,
    /// <summary>Update style of an existing keybox rectangle (color, dashed/solid).</summary>
    UpdateKeyBox,
    /// <summary>Delete (remove) an existing keybox rectangle.</summary>
    DelKeyBox,
    /// <summary>Stop extending a keybox — set its right edge to the break-bar time.</summary>
    StopExtendKeyBox,
    /// <summary>Extend keybox right edge into the future (Pine <c>extend.right</c> while <c>pivKeyExtending</c>).</summary>
    ExtendKeyBox,
    EnqueueObBox,
    ExtendObBox,
    UpdateObBox,
    StopExtendObBox,
    DelObBox,
}

/// <summary>
/// Pine color constants (ARGB uint, Alpha=255 for full opacity text).
/// Taken directly from Pine input defaults: inpHighColor, inpLowColor, etc.
/// </summary>
public static class PineColors
{
    /// <summary>Pine <c>color.new(rgb, transp)</c> — transp 0=opaque, 100=invisible.</summary>
    public static uint WithTransparency(byte r, byte g, byte b, int transpPercent)
    {
        transpPercent = Math.Clamp(transpPercent, 0, 100);
        var a = (byte)((100 - transpPercent) * 255 / 100);
        return ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
    }

    /// <summary>Pine <c>inpKeyOpacityActive</c> default 50 (was 90).</summary>
    public const int DefaultKeyOpacityActivePercent = 50;

    /// <summary>Pine <c>inpKeyOpacityBroken</c> default 30 (was 70).</summary>
    public const int DefaultKeyOpacityBrokenPercent = 30;

    /// <summary>Pine <c>obBoxOpacity</c> default 30 (was 70).</summary>
    public const int DefaultObBoxOpacityPercent = 30;

    /// <summary>Runtime from host <c>obBoxOpacity</c> input.</summary>
    public static int ObBoxOpacityPercent { get; set; } = DefaultObBoxOpacityPercent;

    // Swing label text colors (Pine: inpHighColor / inpLowColor / hhColor / llColor)
    public const uint HighActive   = 0xFF32AC45u; // #32ac45 green
    public const uint LowActive    = 0xFFDE3B22u; // #de3b22 red
    public const uint HighHH       = 0xFF19A11Cu; // #19a11c darker green (HH)
    public const uint LowLL        = 0xFFE63419u; // #e63419 darker red (LL)
    public const uint BrokenGray   = 0xFF808080u; // color.gray
    public const uint MainYellow   = 0xFFFFFF00u; // color.yellow
    public const uint FakeBlue     = 0xFF2196F3u; // color.blue
    public const uint MainBrokenRed= 0xFFFF0000u; // color.red
    public const uint DOrange      = 0xFFFFA500u; // color.orange
    public const uint MainOldGray  = 0xFF9E9E9Eu; // dim gray for MAIN_OLD

    // Connecting line: color.new(#b8fbdb, 0) — fully opaque mint green
    public const uint LineActive    = 0xFFB8FBDBu;
    // Broken line: color.new(gray, 80) — 80% transparent = alpha 51
    public const uint LineBroken    = 0x33808080u;

    // KeyBox fill — DefaultKeyOpacityActivePercent (50% transp)
    public static uint KeyHighFill   => WithTransparency(197, 89, 89, DefaultKeyOpacityActivePercent);
    public static uint KeyLowFill    => WithTransparency(72, 189, 78, DefaultKeyOpacityActivePercent);
    public static uint KeyMainFill   => WithTransparency(255, 235, 59, DefaultKeyOpacityActivePercent);

    // KeyBox border (full opacity — Pine inpBoxHighColor/inpBoxLowColor with transp=17→alpha=212)
    public const uint KeyHighBorder = 0xD4C55959u; // A=212 R=197 G=89 B=89
    public const uint KeyLowBorder  = 0xB848BD4Eu; // A=184 R=72 G=189 B=78
    public const uint KeyMainBorder = 0xCCFFEB3Bu; // A=204 R=255 G=235 B=59
    public static uint KeyHighBrokenFill   => WithTransparency(197, 89, 89, DefaultKeyOpacityBrokenPercent);
    public static uint KeyLowBrokenFill    => WithTransparency(72, 189, 78, DefaultKeyOpacityBrokenPercent);
    public const uint KeyHighBrokenBorder = 0xFFC55959u;
    public const uint KeyLowBrokenBorder  = 0xFF48BD4Eu;

    // OB box — inverted vs key (Pine build_ob_box)
    public static uint ObGreenFill  => WithTransparency(72, 189, 78, ObBoxOpacityPercent);
    public static uint ObGreenBorder => 0xFF48BD4Eu;
    public static uint ObRedFill    => WithTransparency(197, 89, 89, ObBoxOpacityPercent);
    public static uint ObRedBorder  => 0xFFC55959u;
}

public sealed class DrawingCommand
{
    public DrawingCommandKind Kind { get; init; }
    public KeyBoxSpec? KeyBoxSpec { get; init; }
    public ObBoxSpec? ObBoxSpec { get; init; }

    /// <summary>Stable chart-object key string, e.g. "sw_H63" or "kb_...".
    /// Used as cTrader object name so repeated enqueues UPDATE rather than create duplicates.</summary>
    public string? LabelKeyStr { get; init; }

    /// <summary>ARGB fill color (0 = fallback). For UpdateKeyBox / EnqueueKeyBoxSpec.</summary>
    public uint ColorArgb { get; init; }

    /// <summary>ARGB border color (0 = derive from fill / type).</summary>
    public uint BorderColorArgb { get; init; }

    /// <summary>For UpdateKeyBox: use dashed border (BROKEN/BROKEN_FAKE style).</summary>
    public bool IsDashed { get; init; }

    /// <summary>For UpdateKeyBox: cTrader rectangle border thickness (broken = 2).</summary>
    public int BorderThickness { get; init; } = 1;

    /// <summary>For StopExtendKeyBox: true = right edge at <see cref="BarIndex"/> (break bar); false = current bar.</summary>
    public bool StopRightAtBreakBar { get; init; }

    /// <summary>For UpdateKeyBox: use MAIN C style (yellow solid border).</summary>
    public bool IsMainC { get; init; }

    public int? LabelKey { get; init; }
    public int? LineKey { get; init; }
    public int? BarIndex { get; init; }
    public double? Price { get; init; }
    public bool? IsHigh { get; init; }
    public string? Text { get; init; }
    public int? X1 { get; init; }
    public double? Y1 { get; init; }
    public int? X2 { get; init; }
    public double? Y2 { get; init; }
}
