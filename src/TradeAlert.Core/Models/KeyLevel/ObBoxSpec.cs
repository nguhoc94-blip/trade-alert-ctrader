using System;

namespace TradeAlert.Core.Models.KeyLevel;

/// <summary>OB zin box geometry — Pine <c>build_ob_box</c> (candle H/L at obBar).</summary>
public sealed class ObBoxSpec
{
    public DateTime LeftTimeChartLocal  { get; init; }
    public DateTime RightTimeChartLocal { get; init; }
    public double Top    { get; init; }
    public double Bottom { get; init; }
    /// <summary>1 = green OB (uses high key colors inverted), -1 = red OB.</summary>
    public int ObType { get; init; }
    /// <summary>0 = pivot (orange), 1 = structure (blue).</summary>
    public int Source { get; init; }
    /// <summary>Pivot owner index — used to make chart object name unique across OBs sharing the same bar+price.</summary>
    public int Owner  { get; init; }
    /// <summary>OB sequence number for this owner — used to make chart object name unique.</summary>
    public int Number { get; init; }
}
