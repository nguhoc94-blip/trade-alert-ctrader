using System;
using System.Collections.Generic;
using System.Globalization;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace KlEntryLotIndicator;

public enum KlTablePosition
{
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    MiddleCenter,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
}

public enum KlTextSize
{
    Tiny,
    Small,
    Normal,
    Large,
    Huge,
}

/// <summary>
/// Port of Pine <c>kl_vao_lenh_ctrader_bid_spread_fixed</c> — Bid chart, full spread FTMO lot sizing.
/// Pine <c>input.price(..., confirm=true)</c> → interactive draggable horizontal lines on chart.
/// </summary>
[Indicator("KlEntryLotIndicator", IsOverlay = true, TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
public class KlVaoLenhBidSpreadIndicator : Indicator
{
    const string ObjPrefix = "KlEntry_";
    const string LineEntry = ObjPrefix + "Line_Entry";
    const string LineSl = ObjPrefix + "Line_SL";
    const string LineTp1 = ObjPrefix + "Line_TP1";
    const string LineTp2 = ObjPrefix + "Line_TP2";
    const string MetaSpread = ObjPrefix + "Meta_Spread";
    const string MetaSeeded = ObjPrefix + "Meta_Seeded";
    const int OverlayZIndex = 0;
    const int InteractiveLineZIndex = 100;

    readonly List<string> _overlayNames = new(32);

    Grid? _dashGrid;

    AverageTrueRange? _atr;

    ChartHorizontalLine? _hEntry;
    ChartHorizontalLine? _hSl;
    ChartHorizontalLine? _hTp1;
    ChartHorizontalLine? _hTp2;

    double _entryY;
    double _stopY;
    double _tp1Y;
    double _tp2Y;
    double _baseEntryY;
    double _baseStopY;
    double _baseTp1Y;
    double _baseTp2Y;
    bool _tp1Enabled;
    bool _tp2Enabled;
    bool _linesReady;

    // ---------------- INPUTS ----------------
    [Parameter("Asset Type", DefaultValue = KlAssetType.Auto, Group = "Market")]
    public KlAssetType AssetType { get; set; }

    [Parameter("Custom contract size", DefaultValue = 100000, MinValue = 1, Group = "Market")]
    public int CustomContractSize { get; set; }

    [Parameter("Account Balance (USD)", DefaultValue = 400, MinValue = 0, Group = "Risk")]
    public double AccountBalance { get; set; }

    [Parameter("Account Balance FTMO (USD)", DefaultValue = 20000, MinValue = 0, Group = "Risk")]
    public double AccountBalanceFtmo { get; set; } = 20000;

    [Parameter("Risk % per Trade", DefaultValue = 1.0, MinValue = 0, Step = 0.1, Group = "Risk")]
    public double RiskPercent { get; set; }

    [Parameter("Manual conv USD/QUOTE (0=auto)", DefaultValue = 0, MinValue = 0, Group = "Market")]
    public double ManualConvUsdPerQuote { get; set; }

    /// <summary>Initial seed only — sau đó kéo line trên chart (Pine input.price).</summary>
    [Parameter("Entry Price (seed, 0=Bid)", DefaultValue = 0.0, Group = "Trade Levels")]
    public double EntryPrice { get; set; }

    [Parameter("Stop Price (seed, 0=auto offset)", DefaultValue = 0.0, Group = "Trade Levels")]
    public double StopPrice { get; set; }

    [Parameter("Take Profit #1 (seed, 0=off)", DefaultValue = 0.0, Group = "Trade Levels")]
    public double Tp1 { get; set; }

    [Parameter("Take Profit #2 (seed, 0=off)", DefaultValue = 0.0, Group = "Trade Levels")]
    public double Tp2 { get; set; }

    [Parameter("Spread (cTrader pips)", DefaultValue = 0.0, MinValue = 0, Step = 0.01, Group = "Trade Levels")]
    public double SpreadPips { get; set; }

    [Parameter("Lot rounding precision", DefaultValue = 1000, MinValue = 1, Group = "Risk")]
    public int RoundPrecision { get; set; }

    [Parameter("Auto-place lines at Bid if seed=0", DefaultValue = true, Group = "Trade Levels")]
    public bool AutoPlaceLinesAtBid { get; set; }

    [Parameter("ATR period (auto seed)", DefaultValue = 14, MinValue = 1, Group = "Trade Levels")]
    public int AtrPeriod { get; set; }

    [Parameter("Default SL (× ATR)", DefaultValue = 1.0, MinValue = 0.1, Step = 0.1, Group = "Trade Levels")]
    public double DefaultSlAtrMult { get; set; }

    [Parameter("Default TP1 (× ATR)", DefaultValue = 2.0, MinValue = 0.1, Step = 0.1, Group = "Trade Levels")]
    public double DefaultTp1AtrMult { get; set; }

    [Parameter("Default TP2 (× ATR)", DefaultValue = 4.0, MinValue = 0.1, Step = 0.1, Group = "Trade Levels")]
    public double DefaultTp2AtrMult { get; set; }

    [Parameter("Default SL (pips, 0=ATR)", DefaultValue = 0, MinValue = 0, Group = "Trade Levels")]
    public int DefaultSlPips { get; set; }

    [Parameter("Default TP1 (pips, 0=ATR)", DefaultValue = 0, MinValue = 0, Group = "Trade Levels")]
    public int DefaultTp1Pips { get; set; }

    [Parameter("Default TP2 (pips, 0=ATR)", DefaultValue = 0, MinValue = 0, Group = "Trade Levels")]
    public int DefaultTp2Pips { get; set; }

    [Parameter("Box width (bars)", DefaultValue = 40, MinValue = 1, Group = "Visual")]
    public int BoxWidthBars { get; set; }

    [Parameter("Color Entry→SL", DefaultValue = "Red", Group = "Visual")]
    public Color ClrSl { get; set; }

    [Parameter("Color Entry→TP1", DefaultValue = "Green", Group = "Visual")]
    public Color ClrTp1 { get; set; }

    [Parameter("Color Entry→TP2", DefaultValue = "LimeGreen", Group = "Visual")]
    public Color ClrTp2 { get; set; }

    [Parameter("Box border width", DefaultValue = 1, MinValue = 0, MaxValue = 3, Group = "Visual")]
    public int BoxBorderWidth { get; set; }

    [Parameter("Table position", DefaultValue = KlTablePosition.TopRight, Group = "Dashboard")]
    public KlTablePosition TablePosition { get; set; }

    [Parameter("Text size", DefaultValue = KlTextSize.Large, Group = "Dashboard")]
    public KlTextSize TextSize { get; set; }

    [Parameter("Table bg transparency %", DefaultValue = 20, MinValue = 0, MaxValue = 100, Group = "Dashboard")]
    public int TableBgAlpha { get; set; }

    [Parameter("Label color (tiêu đề)", DefaultValue = "Red", Group = "Dashboard")]
    public Color TableLabelTextColor { get; set; }

    [Parameter("Value color (số)", DefaultValue = "White", Group = "Dashboard")]
    public Color TableValueTextColor { get; set; }

    [Parameter("Header text color", DefaultValue = "White", Group = "Dashboard")]
    public Color TableHeaderTextColor { get; set; }

    protected override void Initialize()
    {
        _atr = Indicators.AverageTrueRange(AtrPeriod, MovingAverageType.Exponential);
        EnsureInteractiveLines();
        Chart.ObjectsUpdated += OnChartObjectsUpdated;
    }

    protected override void OnDestroy()
    {
        Chart.ObjectsUpdated -= OnChartObjectsUpdated;
        ClearOverlayOnly();
    }

    public override void Calculate(int index)
    {
        if (!IsLastBar)
            return;

        SyncPricesFromInteractiveLines();
        RefreshOverlay(index);
    }

    void OnChartObjectsUpdated(ChartObjectsUpdatedEventArgs args)
    {
        if (!SyncPricesFromInteractiveLines())
            return;

        if (Bars.Count == 0)
            return;

        RefreshOverlay(Bars.Count - 1);
    }

    void EnsureInteractiveLines()
    {
        if (_linesReady)
            return;

        _tp1Enabled = Tp1 != 0 || Chart.FindObject(LineTp1) != null;
        _tp2Enabled = Tp2 != 0 || Chart.FindObject(LineTp2) != null;

        var restored = TryRestoreLinePositionsFromChart();
        if (restored)
        {
            if (!HasLinesSeededBefore())
                PersistLinesSeeded();
        }
        else
        {
            var allowAutoBid = !HasLinesSeededBefore();
            SeedLinePositionsFromParameters(allowAutoBid);
            if (allowAutoBid && AutoPlaceLinesAtBid && _entryY != 0 && _stopY != 0)
                PersistLinesSeeded();
        }

        ReconcileBaseLevelsAfterRestore(restored);
        ApplyManualSpreadToInteractiveLines();

        _hEntry = FindOrCreateInteractiveLine(LineEntry, _entryY, Color.White, 2, "Entry");
        _hSl = FindOrCreateInteractiveLine(LineSl, _stopY, Color.Red, 2, "SL");

        if (_tp1Enabled)
            _hTp1 = FindOrCreateInteractiveLine(LineTp1, _tp1Y, Color.Green, 2, "TP1");
        if (_tp2Enabled)
            _hTp2 = FindOrCreateInteractiveLine(LineTp2, _tp2Y, Color.LimeGreen, 1, "TP2");

        WriteInteractiveLinePositions();
        SyncPricesFromInteractiveLines();
        RaiseInteractiveLinesAboveOverlays();
        _linesReady = true;
    }

    void ReconcileBaseLevelsAfterRestore(bool restoredFromChart)
    {
        var pip = ComputePip();

        if (!restoredFromChart)
        {
            _baseEntryY = _entryY;
            _baseStopY = _stopY;
            _baseTp1Y = _tp1Y;
            _baseTp2Y = _tp2Y;
            return;
        }

        var oldSpread = LoadPersistedLineSpread() ?? 0;
        var isBuy = KlEntryLotCalculator.IsBuyTrade(_entryY, _stopY);

        _baseEntryY = _entryY;
        _baseStopY = _stopY;
        _baseTp1Y = _tp1Y;
        _baseTp2Y = _tp2Y;
        KlEntryLotCalculator.ReverseSpreadOnBidLevels(
            ref _baseEntryY, ref _baseStopY, ref _baseTp1Y, ref _baseTp2Y,
            oldSpread, pip, isBuy);
    }

    void ApplyManualSpreadToInteractiveLines()
    {
        var spread = SpreadPips;
        var pip = ComputePip();
        var isBuy = KlEntryLotCalculator.IsBuyTrade(_baseEntryY, _baseStopY);

        _entryY = _baseEntryY;
        _stopY = _baseStopY;
        _tp1Y = _baseTp1Y;
        _tp2Y = _baseTp2Y;
        KlEntryLotCalculator.ForwardSpreadOnBidLevels(
            ref _entryY, ref _stopY, ref _tp1Y, ref _tp2Y,
            spread, pip, isBuy);

        PersistLineSpread(spread);
    }

    void CaptureBaseFromDisplayedLines()
    {
        if (_entryY == 0 || _stopY == 0 || _entryY == _stopY)
            return;

        var spread = SpreadPips;
        var pip = ComputePip();
        var isBuy = KlEntryLotCalculator.IsBuyTrade(_entryY, _stopY);

        _baseEntryY = _entryY;
        _baseStopY = _stopY;
        _baseTp1Y = _tp1Y;
        _baseTp2Y = _tp2Y;
        KlEntryLotCalculator.ReverseSpreadOnBidLevels(
            ref _baseEntryY, ref _baseStopY, ref _baseTp1Y, ref _baseTp2Y,
            spread, pip, isBuy);
    }

    void WriteInteractiveLinePositions()
    {
        SetLineY(LineEntry, _entryY);
        SetLineY(LineSl, _stopY);
        if (_tp1Enabled)
            SetLineY(LineTp1, _tp1Y);
        if (_tp2Enabled)
            SetLineY(LineTp2, _tp2Y);
    }

    void SetLineY(string name, double y)
    {
        if (Chart.FindObject(name) is not ChartHorizontalLine line || y == 0)
            return;

        line.Y = y;
        line.IsInteractive = true;
        line.IsLocked = false;
        line.ZIndex = InteractiveLineZIndex;
    }

    double? LoadPersistedLineSpread()
    {
        if (Chart.FindObject(MetaSpread) is not ChartStaticText meta)
            return null;

        return double.TryParse(meta.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
    }

    void PersistLineSpread(double spreadPips)
    {
        Chart.DrawStaticText(
            MetaSpread,
            spreadPips.ToString("0.####", CultureInfo.InvariantCulture),
            VerticalAlignment.Top,
            HorizontalAlignment.Left,
            Color.Transparent);
    }

    bool HasLinesSeededBefore()
    {
        if (Chart.FindObject(MetaSeeded) is not ChartStaticText meta)
            return false;

        return meta.Text == "1";
    }

    void PersistLinesSeeded()
    {
        Chart.DrawStaticText(MetaSeeded, "1", VerticalAlignment.Top, HorizontalAlignment.Left, Color.Transparent);
    }

    bool TryRestoreLinePositionsFromChart()
    {
        var foundEntry = false;
        var foundSl = false;

        if (Chart.FindObject(LineEntry) is ChartHorizontalLine entry)
        {
            _entryY = entry.Y;
            foundEntry = _entryY != 0;
        }

        if (Chart.FindObject(LineSl) is ChartHorizontalLine sl)
        {
            _stopY = sl.Y;
            foundSl = _stopY != 0;
        }

        if (Chart.FindObject(LineTp1) is ChartHorizontalLine tp1)
        {
            _tp1Y = tp1.Y;
            _tp1Enabled = true;
        }

        if (Chart.FindObject(LineTp2) is ChartHorizontalLine tp2)
        {
            _tp2Y = tp2.Y;
            _tp2Enabled = true;
        }

        return foundEntry && foundSl;
    }

    void SeedLinePositionsFromParameters(bool allowAutoBid)
    {
        var pip = ComputePip();
        var bid = Symbol.Bid > 0 ? Symbol.Bid : Bars.ClosePrices.LastValue;
        var autoBid = allowAutoBid && AutoPlaceLinesAtBid;
        var dist = ResolveSeedDistances(pip);

        _entryY = EntryPrice != 0 ? EntryPrice : (autoBid ? bid : 0);
        _stopY = StopPrice != 0 ? StopPrice : (autoBid && _entryY != 0 ? _entryY - dist.SlPips * pip : 0);

        _tp1Enabled = Tp1 != 0 || autoBid;
        _tp2Enabled = Tp2 != 0 || autoBid;
        _tp1Y = Tp1 != 0 ? Tp1 : (autoBid && _entryY != 0 ? _entryY + dist.Tp1Pips * pip : 0);
        _tp2Y = Tp2 != 0 ? Tp2 : (autoBid && _entryY != 0 ? _entryY + dist.Tp2Pips * pip : 0);
    }

    KlEntryLotCalculator.KlDefaultSeedDistances ResolveSeedDistances(double pip) =>
        KlEntryLotCalculator.ResolveDefaultSeedDistancesFromAtr(
            TryGetSeedAtr(),
            pip,
            DefaultSlAtrMult,
            DefaultTp1AtrMult,
            DefaultTp2AtrMult,
            DefaultSlPips,
            DefaultTp1Pips,
            DefaultTp2Pips);

    double TryGetSeedAtr()
    {
        if (_atr == null || Bars.Count == 0)
            return 0;

        for (var i = Bars.Count - 1; i >= 0; i--)
        {
            var v = _atr.Result[i];
            if (!double.IsNaN(v) && v > 0)
                return v;
        }

        return 0;
    }

    ChartHorizontalLine FindOrCreateInteractiveLine(
        string name, double y, Color color, int thickness, string comment)
    {
        if (Chart.FindObject(name) is ChartHorizontalLine existing)
        {
            if (y != 0)
                existing.Y = y;
            ConfigureInteractiveLine(existing, comment);
            return existing;
        }

        if (y == 0)
            return null!;

        var line = Chart.DrawHorizontalLine(name, y, color, thickness, LineStyle.Solid);
        ConfigureInteractiveLine(line, comment);
        return line;
    }

    static void ConfigureInteractiveLine(ChartHorizontalLine line, string comment)
    {
        line.IsInteractive = true;
        line.IsLocked = false;
        line.ZIndex = InteractiveLineZIndex;
        line.Comment = comment;
    }

    void RaiseInteractiveLinesAboveOverlays()
    {
        RaiseOneInteractiveLine(LineEntry);
        RaiseOneInteractiveLine(LineSl);
        if (_tp1Enabled)
            RaiseOneInteractiveLine(LineTp1);
        if (_tp2Enabled)
            RaiseOneInteractiveLine(LineTp2);
    }

    void RaiseOneInteractiveLine(string name)
    {
        if (Chart.FindObject(name) is not ChartHorizontalLine line)
            return;

        line.IsInteractive = true;
        line.IsLocked = false;
        line.ZIndex = InteractiveLineZIndex;
    }

    bool SyncPricesFromInteractiveLines()
    {
        var changed = false;
        if (TryReadLine(LineEntry, ref _entryY)) changed = true;
        if (TryReadLine(LineSl, ref _stopY)) changed = true;
        if (_tp1Enabled && TryReadLine(LineTp1, ref _tp1Y)) changed = true;
        if (_tp2Enabled && TryReadLine(LineTp2, ref _tp2Y)) changed = true;

        if (changed)
            CaptureBaseFromDisplayedLines();

        return changed;
    }

    bool TryReadLine(string name, ref double y)
    {
        if (Chart.FindObject(name) is not ChartHorizontalLine line)
            return false;
        if (Math.Abs(line.Y - y) < 1e-12)
            return false;
        y = line.Y;
        return true;
    }

    void RefreshOverlay(int barIndex)
    {
        SyncPricesFromInteractiveLines();

        var quoteCurr = Symbol.QuoteAsset?.Name ?? "USD";
        var isCrypto = DetectCryptoSymbol();
        var autoConv = TryResolveAutoConvToUsd(quoteCurr);
        var tp1 = _tp1Enabled ? _baseTp1Y : 0;
        var tp2 = _tp2Enabled ? _baseTp2Y : 0;
        var result = KlEntryLotCalculator.Compute(new KlEntryLotInput
        {
            AssetType = AssetType,
            CustomContractSize = CustomContractSize,
            AccountBalance = AccountBalance,
            AccountBalanceFtmo = AccountBalanceFtmo,
            RiskPercent = RiskPercent,
            ManualConvUsdPerQuote = ManualConvUsdPerQuote,
            EntryPrice = _baseEntryY,
            StopPrice = _baseStopY,
            Tp1 = tp1,
            Tp2 = tp2,
            SpreadPips = SpreadPips,
            RoundPrecision = RoundPrecision,
            SymbolName = Symbol.Name,
            TickSize = Symbol.TickSize,
            PipSize = Symbol.PipSize,
            IsCryptoSymbol = isCrypto,
            QuoteCurrency = quoteCurr,
            BaseAssetName = Symbol.BaseAsset?.Name ?? "",
            AutoConvToUsd = autoConv,
        });

        ClearOverlayOnly();
        UpdateInteractiveLineLabels(result, barIndex);
        DrawBoxes(result, barIndex);
        DrawDashboard(result, SpreadPips);
        RaiseInteractiveLinesAboveOverlays();
    }

    double ComputePip()
    {
        return KlEntryLotCalculator.ResolvePipSize(
            Symbol.Name,
            Symbol.TickSize,
            Symbol.PipSize);
    }

    double? TryResolveAutoConvToUsd(string quoteCurr)
    {
        if (ManualConvUsdPerQuote > 0)
            return ManualConvUsdPerQuote;

        var q = quoteCurr.Trim().ToUpperInvariant();
        if (q is "USD" or "USDT" or "USDC")
            return 1.0;

        var sym1 = TryGetSymbolBid($"{q}USD");
        if (sym1 is > 0)
            return sym1;

        var sym2 = TryGetSymbolBid($"USD{q}");
        if (sym2 is > 0)
            return 1.0 / sym2.Value;

        return null;
    }

    double? TryGetSymbolBid(string symbolName)
    {
        try
        {
            var sym = Symbols.GetSymbol(symbolName);
            if (sym == null)
                return null;
            var bid = sym.Bid;
            return bid > 0 ? bid : null;
        }
        catch
        {
            return null;
        }
    }

    bool DetectCryptoSymbol()
    {
        if (AssetType == KlAssetType.BTCUSD)
            return true;

        var n = Symbol.Name.ToUpperInvariant();
        return n.Contains("BTC", StringComparison.Ordinal)
               || n.Contains("ETH", StringComparison.Ordinal)
               || n.Contains("CRYPTO", StringComparison.Ordinal);
    }

    void UpdateInteractiveLineLabels(KlEntryLotResult r, int barIndex)
    {
        var r1 = KlEntryLotCalculator.RewardToRisk(r.PipsToTp1Ftmo, r.StopPipsFtmo);
        var r2 = KlEntryLotCalculator.RewardToRisk(r.PipsToTp2Ftmo, r.StopPipsFtmo);
        var slPips = KlEntryLotCalculator.FormatNum(r.StopPipsFtmo);
        var r1Str = KlEntryLotCalculator.FormatRewardRatio(r1);
        var r2Str = KlEntryLotCalculator.FormatRewardRatio(r2);

        SetLineComment(LineEntry, "Entry");
        SetLineComment(LineSl, $"SL | {slPips} pips (FTMO)");
        if (_tp1Enabled)
            SetLineComment(LineTp1, $"TP1 | R1={r1Str}:R");
        if (_tp2Enabled)
            SetLineComment(LineTp2, $"TP2 | R2={r2Str}:R");

        DrawLineLabel(barIndex, _entryY, "Entry");
        DrawLineLabel(barIndex, _stopY, $"SL | {slPips} pips (FTMO)");
        if (_tp1Enabled && _tp1Y != 0)
            DrawLineLabel(barIndex, _tp1Y, $"TP1 | R1={r1Str}:R");
        if (_tp2Enabled && _tp2Y != 0)
            DrawLineLabel(barIndex, _tp2Y, $"TP2 | R2={r2Str}:R");
    }

    void SetLineComment(string name, string comment)
    {
        if (Chart.FindObject(name) is ChartHorizontalLine line)
            line.Comment = comment;
    }

    void DrawLineLabel(int barIndex, double y, string text)
    {
        if (y == 0)
            return;

        var name = ObjPrefix + "Lbl_";
        if (text.StartsWith("Entry", StringComparison.Ordinal))
            name += "Entry";
        else if (text.StartsWith("SL", StringComparison.Ordinal))
            name += "SL";
        else if (text.StartsWith("TP1", StringComparison.Ordinal))
            name += "TP1";
        else if (text.StartsWith("TP2", StringComparison.Ordinal))
            name += "TP2";
        else
            name += text.GetHashCode().ToString("X");

        var label = Chart.DrawText(name, text, barIndex, y, Color.White);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.HorizontalAlignment = HorizontalAlignment.Right;
        label.FontSize = 11;
        label.ZIndex = OverlayZIndex;
        _overlayNames.Add(name);
    }

    void DrawBoxes(KlEntryLotResult r, int barIndex)
    {
        var rightIdx = barIndex;
        var leftIdx = Math.Max(0, barIndex - BoxWidthBars + 1);
        var tRight = Bars.OpenTimes[rightIdx];
        var tLeft = Bars.OpenTimes[leftIdx];

        if (_entryY != 0 && _stopY != 0)
        {
            var top = Math.Max(_entryY, _stopY);
            var bot = Math.Min(_entryY, _stopY);
            DrawBox("BoxSL", tLeft, top, tRight, bot, WithAlpha(ClrSl, 70));
        }

        var tp1 = _tp1Enabled ? _tp1Y : 0;
        var tp2 = _tp2Enabled ? _tp2Y : 0;

        if (_entryY != 0 && tp1 != 0)
        {
            var top = Math.Max(_entryY, tp1);
            var bot = Math.Min(_entryY, tp1);
            DrawBox("BoxTP1", tLeft, top, tRight, bot, WithAlpha(ClrTp1, 70));
        }

        if (_entryY != 0 && tp2 != 0)
        {
            var top = Math.Max(_entryY, tp2);
            var bot = Math.Min(_entryY, tp2);
            DrawBox("BoxTP2", tLeft, top, tRight, bot, WithAlpha(ClrTp2, 85));
        }
    }

    void DrawDashboard(KlEntryLotResult r, double spreadPips)
    {
        RemoveDashboardGrid();

        _dashGrid = KlDashboardGridBuilder.Build(
            new KlDashboardGridBuilder.DashboardStyle
            {
                Position = TablePosition,
                TextSize = TextSize,
                BgAlphaPercent = TableBgAlpha,
                LabelTextColor = TableLabelTextColor,
                ValueTextColor = TableValueTextColor,
                HeaderTextColor = TableHeaderTextColor,
            },
            new KlDashboardGridBuilder.DashboardData
            {
                SymbolName = Symbol.Name,
                PriceDigits = Symbol.Digits,
                SpreadPips = spreadPips,
                AutoSpreadPips = KlEntryLotCalculator.ResolveAutoSpreadPips(Symbol.Spread, Symbol.PipSize),
                AccountBalanceFtmo = AccountBalanceFtmo,
                Result = r,
            });

        Chart.AddControl(_dashGrid);
    }

    void RemoveDashboardGrid()
    {
        if (_dashGrid == null)
            return;
        Chart.RemoveControl(_dashGrid);
        _dashGrid = null;
    }

    void DrawBox(string suffix, DateTime tLeft, double yTop, DateTime tRight, double yBot, Color fill)
    {
        var name = ObjPrefix + "Box_" + suffix;
        var rect = Chart.DrawRectangle(name, tLeft, yTop, tRight, yBot, fill);
        rect.IsFilled = true;
        rect.Color = fill;
        rect.Thickness = BoxBorderWidth;
        rect.ZIndex = OverlayZIndex;
        _overlayNames.Add(name);
    }

    void ClearOverlayOnly()
    {
        RemoveDashboardGrid();
        foreach (var n in _overlayNames)
            Chart.RemoveObject(n);
        _overlayNames.Clear();
    }

    static Color WithAlpha(Color c, int transparencyPercent)
    {
        var a = (int)Math.Round(255 * (100 - transparencyPercent) / 100.0);
        return Color.FromArgb(a, c.R, c.G, c.B);
    }
}
