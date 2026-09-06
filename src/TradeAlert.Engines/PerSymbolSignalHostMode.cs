namespace TradeAlert.Indicator;

/// <summary>
/// Controls how <see cref="PerSymbolSignalHost"/> builds compound FIRE context.
/// </summary>
public enum PerSymbolSignalHostMode
{
    /// <summary>
    /// Scanner / live timer path — requires last bar + realtime hits &gt;= 2 (default).
    /// </summary>
    ScannerRealtime,

    /// <summary>
    /// Backtest bar-close path — evaluate compound rules on each closed bar (OnBar replay).
    /// </summary>
    BacktestBarClose,

    /// <summary>
    /// VisualBacktesting parity — replay M1 sub-bars within each chart bar so compound
    /// RealZone / min-TF sync matches the indicator panel (many Calculate passes per HTF bar).
    /// </summary>
    BacktestVisualReplay,
}
