namespace TradeAlert.Core.Series;

public readonly record struct OhlcTuple(double Open, double High, double Low, double Close)
{
    public static OhlcTuple NaN { get; } = new(double.NaN, double.NaN, double.NaN, double.NaN);

    public bool IsNaN => double.IsNaN(Open) || double.IsNaN(High) || double.IsNaN(Low) || double.IsNaN(Close);
}
