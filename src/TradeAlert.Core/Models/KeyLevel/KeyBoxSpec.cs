namespace TradeAlert.Core.Models.KeyLevel;

/// <summary>Geometry keylevel từ <c>build_keylevel_box</c> — thời gian chart-local <see cref="DateTimeKind.Unspecified"/> (REV_003).</summary>
public sealed class KeyBoxSpec
{
    public DateTime LeftTimeChartLocal { get; init; }
    public DateTime RightTimeChartLocal { get; init; }
    public double Top { get; init; }
    public double Bottom { get; init; }
    public uint HighColorArgb { get; init; }
    public uint LowColorArgb { get; init; }
    public int Opacity { get; init; }
    public bool ExtendRight { get; init; }
}
