using TradeAlert.Core.Drawing;

namespace TradeAlert.Core.Engines;

/// <summary>Per-bar inputs for keylevel draw/sync (Pine keylevel settings + series).</summary>
public sealed class KeyLevelTickContext
{
    public int BarIndex { get; init; }
    public double AvgBody { get; init; }
    public double AtrValue { get; init; }
    public double TickSize { get; init; }
    /// <summary>Pine <c>maxKeylevelKeep</c> default 2.</summary>
    public int MaxKeylevelKeep { get; init; } = 2;
    public int KeylevelLookback { get; init; } = 2;
    public double KeylevelMinBodyMult { get; init; } = 1.5;
    public int KeylevelAtrLen { get; init; } = 14;
    public double KeylevelAtrMult { get; init; } = 0.2;
    public int SearchRangeBars { get; init; } = 200;
    public bool EnableKeylevel { get; init; } = true;
    public bool KeylevelUseAtrRule { get; init; } = true;
    /// <summary>Pine <c>inpKeyOpacityActive</c> — lower = more visible (default 50, was 90).</summary>
    public int KeyOpacityActivePercent { get; init; } = PineColors.DefaultKeyOpacityActivePercent;
    public int KeyOpacityBrokenPercent { get; init; } = PineColors.DefaultKeyOpacityBrokenPercent;
    public Func<int, (double Open, double High, double Low, double Close)> OhlcAtLag { get; init; } = null!;
    public Func<int, DateTime> ChartTimeAtLag { get; init; } = null!;
}
