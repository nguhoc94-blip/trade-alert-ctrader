namespace TradeAlert.BacktestRobot.Execution.Analytics;

/// <summary>Tracks MAE/MFE in pips while a position is open (bar-close sampling).</summary>
public sealed class Loop6TradeExcursionTracker
{
    readonly Dictionary<string, ExcursionState> _byLabel = new(StringComparer.Ordinal);

    sealed class ExcursionState
    {
        public bool IsBuy { get; init; }
        public double Entry { get; init; }
        public double InitialSl { get; init; }
        public double PipSize { get; init; }
        public DateTime OpenTimeUtc { get; init; }
        public int OpenBarIndex { get; init; }
        public double MfePips { get; set; }
        public double MaePips { get; set; }
    }

    public void OnOpened(string label, bool isBuy, double entry, double initialSl, double pipSize, DateTime openTimeUtc, int openBarIndex)
    {
        if (string.IsNullOrWhiteSpace(label))
            return;

        _byLabel[label] = new ExcursionState
        {
            IsBuy = isBuy,
            Entry = entry,
            InitialSl = initialSl,
            PipSize = pipSize > 0 ? pipSize : 0.0001,
            OpenTimeUtc = openTimeUtc,
            OpenBarIndex = openBarIndex,
        };
    }

    public void UpdateBar(string label, double bidHigh, double bidLow)
    {
        if (!_byLabel.TryGetValue(label, out var s))
            return;

        var pip = s.PipSize;
        double favorable, adverse;
        if (s.IsBuy)
        {
            favorable = (bidHigh - s.Entry) / pip;
            adverse = (s.Entry - bidLow) / pip;
        }
        else
        {
            favorable = (s.Entry - bidLow) / pip;
            adverse = (bidHigh - s.Entry) / pip;
        }

        if (favorable > s.MfePips)
            s.MfePips = favorable;
        if (adverse > s.MaePips)
            s.MaePips = adverse;
    }

    public bool TryRemove(string label, out ExcursionSnapshot snapshot)
    {
        snapshot = default;
        if (!_byLabel.Remove(label, out var s))
            return false;

        var riskPips = Math.Abs(s.Entry - s.InitialSl) / s.PipSize;
        snapshot = new ExcursionSnapshot
        {
            MfePips = s.MfePips,
            MaePips = s.MaePips,
            MfeR = riskPips > 0 ? s.MfePips / riskPips : 0,
            MaeR = riskPips > 0 ? s.MaePips / riskPips : 0,
            OpenTimeUtc = s.OpenTimeUtc,
            OpenBarIndex = s.OpenBarIndex,
        };
        return true;
    }
}

public readonly struct ExcursionSnapshot
{
    public double MfePips { get; init; }
    public double MaePips { get; init; }
    public double MfeR { get; init; }
    public double MaeR { get; init; }
    public DateTime OpenTimeUtc { get; init; }
    public int OpenBarIndex { get; init; }
}
