using System.Globalization;
using cAlgo.API;

namespace KlEntryLotIndicator;

/// <summary>Compact FTMO-only dashboard (no mid/standard account rows).</summary>
public static class KlDashboardGridBuilder
{
    const int Rows = 10;
    const int Cols = 4;

    public sealed class DashboardStyle
    {
        public KlTablePosition Position { get; init; } = KlTablePosition.TopRight;
        public KlTextSize TextSize { get; init; } = KlTextSize.Large;
        public int BgAlphaPercent { get; init; } = 20;
        public Color LabelTextColor { get; init; } = Color.Red;
        public Color ValueTextColor { get; init; } = Color.White;
        public Color HeaderTextColor { get; init; } = Color.White;
    }

    public sealed class DashboardData
    {
        public string SymbolName { get; init; } = "";
        public int PriceDigits { get; init; }
        public double SpreadPips { get; init; }
        /// <summary>Live Ask−Bid in cTrader pips — reference only, not applied to calculations.</summary>
        public double AutoSpreadPips { get; init; }
        public double AccountBalanceFtmo { get; init; }
        public KlEntryLotResult Result { get; init; } = new();
    }

    public static Grid Build(in DashboardStyle style, in DashboardData data)
    {
        var r = data.Result;
        var labelFg = style.LabelTextColor;
        var valueFg = style.ValueTextColor;
        var fontSize = MapFontSize(style.TextSize);
        var hdrBg = Color.FromArgb(204, 128, 128, 128);
        var valBg = Color.FromArgb(
            (int)Math.Round(255 * (100 - style.BgAlphaPercent) / 100.0),
            0, 0, 0);
        var titleBg = Color.FromArgb(204, 0, 0, 255);

        var (vAlign, hAlign) = MapTablePosition(style.Position);
        var grid = new Grid(Rows, Cols)
        {
            BackgroundColor = valBg,
            Opacity = 0.95,
            HorizontalAlignment = hAlign,
            VerticalAlignment = vAlign,
            ShowGridLines = true,
        };

        for (var c = 0; c < Cols; c++)
            grid.Columns[c].SetWidthInStars(1);

        var digits = data.PriceDigits;
        var perLegFtmo = KlEntryLotCalculator.FormatLot(r.PerLegDisplayedFtmo, r.EffectiveRound, r.AssetUnit);
        var totalLotFtmo = KlEntryLotCalculator.FormatLot(r.RoundDisplayedFtmo, r.EffectiveRound, r.AssetUnit);
        var accFtmo = data.AccountBalanceFtmo > 0
            ? data.AccountBalanceFtmo.ToString("0.##", CultureInfo.InvariantCulture)
            : "-";
        var estLossFtmo = r.TargetRiskUsdFtmo ?? r.EstLossFtmo;

        // Row 0 — title
        AddCell(grid, 0, 0, data.SymbolName, style.HeaderTextColor, titleBg, fontSize, FontWeight.Bold);
        AddCell(grid, 0, 1, "", style.HeaderTextColor, titleBg, fontSize);
        AddCell(grid, 0, 2, "", style.HeaderTextColor, titleBg, fontSize);
        AddCell(grid, 0, 3, "", style.HeaderTextColor, titleBg, fontSize);

        // Row 1–2 — FTMO prices
        AddCell(grid, 1, 0, "FTMO Entry", labelFg, hdrBg, fontSize);
        AddCell(grid, 1, 1, "FTMO SL", labelFg, hdrBg, fontSize);
        AddCell(grid, 1, 2, "FTMO TP1", labelFg, hdrBg, fontSize);
        AddCell(grid, 1, 3, "FTMO TP2", labelFg, hdrBg, fontSize);
        AddCell(grid, 2, 0, KlEntryLotCalculator.FormatNum(r.EntryFtmo, $"F{digits}"), valueFg, valBg, fontSize);
        AddCell(grid, 2, 1, KlEntryLotCalculator.FormatNum(r.SlFtmo, $"F{digits}"), valueFg, valBg, fontSize);
        AddCell(grid, 2, 2, KlEntryLotCalculator.FormatNum(r.Tp1Ftmo, $"F{digits}"), valueFg, valBg, fontSize);
        AddCell(grid, 2, 3, KlEntryLotCalculator.FormatNum(r.Tp2Ftmo, $"F{digits}"), valueFg, valBg, fontSize);

        // Row 3–4 — manual spread + FTMO pips
        AddCell(grid, 3, 0, "Spread (cTrader pips)", labelFg, hdrBg, fontSize);
        AddCell(grid, 3, 1, "FTMO SL Pips", labelFg, hdrBg, fontSize);
        AddCell(grid, 3, 2, "FTMO TP1 Pips", labelFg, hdrBg, fontSize);
        AddCell(grid, 3, 3, "FTMO TP2 Pips", labelFg, hdrBg, fontSize);
        AddCell(grid, 4, 0, data.SpreadPips.ToString("0.##", CultureInfo.InvariantCulture), valueFg, valBg, fontSize);
        AddCell(grid, 4, 1, KlEntryLotCalculator.FormatNum(r.StopPipsFtmo), valueFg, valBg, fontSize);
        AddCell(grid, 4, 2, KlEntryLotCalculator.FormatNum(r.PipsToTp1Ftmo), valueFg, valBg, fontSize);
        AddCell(grid, 4, 3, KlEntryLotCalculator.FormatNum(r.PipsToTp2Ftmo), valueFg, valBg, fontSize);

        // Row 5 — live auto spread (reference only; user copies into parameter manually)
        AddCell(grid, 5, 0, "Auto spread (cTrader)", labelFg, hdrBg, fontSize);
        AddCell(grid, 5, 1, data.AutoSpreadPips.ToString("0.##", CultureInfo.InvariantCulture), valueFg, valBg, fontSize);
        AddCell(grid, 5, 2, "-", labelFg, hdrBg, fontSize);
        AddCell(grid, 5, 3, "-", labelFg, hdrBg, fontSize);

        // Row 6–7 — FTMO lot + account
        AddCell(grid, 6, 0, "FTMO Lot (per leg)", labelFg, hdrBg, fontSize);
        AddCell(grid, 6, 1, "FTMO Lot (total)", labelFg, hdrBg, fontSize);
        AddCell(grid, 6, 2, "FTMO Est Loss (USD)", labelFg, hdrBg, fontSize);
        AddCell(grid, 6, 3, "Account (FTMO)", labelFg, hdrBg, fontSize);
        AddCell(grid, 7, 0, perLegFtmo, valueFg, valBg, fontSize);
        AddCell(grid, 7, 1, totalLotFtmo, valueFg, valBg, fontSize);
        AddCell(grid, 7, 2, KlEntryLotCalculator.FormatNum(estLossFtmo), valueFg, valBg, fontSize);
        AddCell(grid, 7, 3, accFtmo, valueFg, valBg, fontSize);

        // Row 8–9 — FTMO P&L
        AddCell(grid, 8, 0, "FTMO TP1 P&L", labelFg, hdrBg, fontSize);
        AddCell(grid, 8, 1, "FTMO TP2 P&L", labelFg, hdrBg, fontSize);
        AddCell(grid, 8, 2, "-", labelFg, hdrBg, fontSize);
        AddCell(grid, 8, 3, "-", labelFg, hdrBg, fontSize);
        AddCell(grid, 9, 0, KlEntryLotCalculator.FormatNum(r.PnlTp1Ftmo), valueFg, valBg, fontSize);
        AddCell(grid, 9, 1, KlEntryLotCalculator.FormatNum(r.PnlTp2Ftmo), valueFg, valBg, fontSize);
        AddCell(grid, 9, 2, "-", valueFg, valBg, fontSize);
        AddCell(grid, 9, 3, "-", valueFg, valBg, fontSize);

        return grid;
    }

    static void AddCell(
        Grid grid, int row, int col, string text, Color fg, Color bg, double fontSize,
        FontWeight weight = FontWeight.Normal)
    {
        var block = new TextBlock
        {
            Text = text,
            ForegroundColor = fg,
            BackgroundColor = bg,
            FontSize = fontSize,
            FontWeight = weight,
            Margin = 3,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            TextAlignment = TextAlignment.Center,
        };
        grid.AddChild(block, row, col);
    }

    static double MapFontSize(KlTextSize size) =>
        size switch
        {
            KlTextSize.Tiny => 9,
            KlTextSize.Small => 10,
            KlTextSize.Normal => 11,
            KlTextSize.Large => 12,
            KlTextSize.Huge => 14,
            _ => 12,
        };

    static (VerticalAlignment, HorizontalAlignment) MapTablePosition(KlTablePosition pos) =>
        pos switch
        {
            KlTablePosition.TopLeft => (VerticalAlignment.Top, HorizontalAlignment.Left),
            KlTablePosition.TopCenter => (VerticalAlignment.Top, HorizontalAlignment.Center),
            KlTablePosition.TopRight => (VerticalAlignment.Top, HorizontalAlignment.Right),
            KlTablePosition.MiddleLeft => (VerticalAlignment.Center, HorizontalAlignment.Left),
            KlTablePosition.MiddleCenter => (VerticalAlignment.Center, HorizontalAlignment.Center),
            KlTablePosition.MiddleRight => (VerticalAlignment.Center, HorizontalAlignment.Right),
            KlTablePosition.BottomLeft => (VerticalAlignment.Bottom, HorizontalAlignment.Left),
            KlTablePosition.BottomCenter => (VerticalAlignment.Bottom, HorizontalAlignment.Center),
            _ => (VerticalAlignment.Bottom, HorizontalAlignment.Right),
        };
}
