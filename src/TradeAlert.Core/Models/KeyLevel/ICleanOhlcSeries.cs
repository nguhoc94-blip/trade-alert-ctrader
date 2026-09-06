namespace TradeAlert.Core.Models.KeyLevel;

/// <summary>
/// Wick-clean extreme for micro-swing scan.
/// <paramref name="off"/> = Pine loop index (1..maxOff): clean of bar <c>barIndex - 1 - off</c>
/// (same as <c>cleanLow1Raw[off]</c> / <c>cleanHigh1Raw[off]</c> at evaluation bar).
/// </summary>
public interface ICleanOhlcSeries
{
    double CleanLowAtOffset(int off);
    double CleanHighAtOffset(int off);
}
