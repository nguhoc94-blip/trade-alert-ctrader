using System;
using System.Globalization;
using TradeAlert.Core.Drawing;
using TradeAlert.Core.Models.KeyLevel;

namespace TradeAlert.Core.Engines;

/// <summary>Pine OB labels — create, LOSE text, ZIN visibility (<c>hideOBZin</c> / <c>hideOBLoseZin</c>).</summary>
public static class ObLabelDrawEngine
{
    public static string ChartObjectName(int owner, int source, int barOb, int number) =>
        $"ob_lb_{owner}_{source}_{barOb}_{number:D2}";

    public static string GetImmutableSwingId(ObRecord r) =>
        r.PivotType == 1
            ? $"H{r.PivotHighId:D3}"
            : $"L{r.PivotLowId:D3}";

    public static string BuildLoseText(ObRecord r) =>
        $"{(r.Source == 0 ? "P" : "S")} {GetImmutableSwingId(r)} #{r.Number:D2} LOSE";

    public static uint LabelColorFor(int source) =>
        source == 0 ? 0xFFFFA500u : PineColors.FakeBlue;

    /// <summary>Text label ZIN theo phân loại LTF confirm.</summary>
    public static string BuildZinLabelText(ObRecord r)
    {
        if (r.State == 2 && r.FlagLtf == 0)
            return "OB?";

        if (r.FlagLtf == 2)
        {
            return r.LtfConfirmKind switch
            {
                ObLtfConfirmKind.NoLtfData => FormatNoLtfData(r),
                ObLtfConfirmKind.HasLtfNoTouch => FormatHasLtfNoTouch(r),
                _ => "OB",
            };
        }

        return "OB";
    }

    static string FormatHasLtfNoTouch(ObRecord r)
    {
        var head = HasLtfScanRange(r)
            ? $"OB has LTF but not real H={Fmt(r.LtfScanHigh)} L={Fmt(r.LtfScanLow)} x={Fmt(r.X)}"
            : "OB has LTF but not real";
        return $"{head}\n{FormatLtfConfirmDebug(r)}";
    }

    static string FormatNoLtfData(ObRecord r) =>
        $"OB no LTF data\n{FormatLtfConfirmDebug(r)}";

    static string FormatLtfConfirmDebug(ObRecord r)
    {
        var path = r.LtfConfirmPath switch
        {
            ObLtfConfirmPath.CheckBar => "1Bar",
            ObLtfConfirmPath.FullRange => "Full",
            ObLtfConfirmPath.NoLtfMapping => "NoMap",
            ObLtfConfirmPath.ObConfirmOff => "Off",
            _ => "?",
        };

        var missHint = r.LtfBufferMissBars > 0
            ? " MISS_RING"
            : r.LtfFlatCandles == 0 && r.LtfHasBars > 0
                ? " EMPTY_SLICE"
                : "";

        return
            $"st={r.State} htfCnt={r.Count} ob={r.Bar} chk={r.LastCheckedBar} cf@{r.LtfConfirmBar}" +
            $" | {path} htfLtf={r.LtfHasBars}/{r.LtfTotalBars} miss={r.LtfBufferMissBars}" +
            $" m5={r.LtfFlatCandles} tc={r.LtfTouchCount} buf={r.LtfBufferFlatAtConfirm}" +
            $" max={r.LtfScanMaxBar} ring=50{missHint}";
    }

    static bool HasLtfScanRange(ObRecord r) =>
        r.LtfScanHigh > r.LtfScanLow
        && !double.IsNaN(r.LtfScanHigh)
        && !double.IsNaN(r.LtfScanLow);

    static string Fmt(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

    public static void EmitCreate(
        int barOb,
        double labelPrice,
        int typ,
        int owner,
        int source,
        int number,
        IDrawingCommandSink? sink)
    {
        if (sink == null) return;
        sink.Enqueue(new DrawingCommand
        {
            Kind        = DrawingCommandKind.AddLabel,
            LabelKeyStr = ChartObjectName(owner, source, barOb, number),
            BarIndex    = barOb,
            Price       = labelPrice,
            Text        = "OB",
            ColorArgb   = LabelColorFor(source),
            IsHigh      = typ == 1,
        });
    }

    public static void EmitUpdateText(string? labelKey, string text, uint colorArgb, IDrawingCommandSink? sink)
    {
        if (sink == null || string.IsNullOrEmpty(labelKey)) return;
        sink.Enqueue(new DrawingCommand
        {
            Kind        = DrawingCommandKind.AddLabel,
            LabelKeyStr = labelKey,
            Text        = text,
            ColorArgb   = colorArgb,
        });
    }

    public static void EmitHide(string? labelKey, IDrawingCommandSink? sink) =>
        EmitUpdateText(labelKey, "", 0x00FFFFFFu, sink);

    public static void EmitDelete(string? labelKey, IDrawingCommandSink? sink)
    {
        if (sink == null || string.IsNullOrEmpty(labelKey)) return;
        sink.Enqueue(new DrawingCommand { Kind = DrawingCommandKind.DelLabel, LabelKeyStr = labelKey });
    }

    public static void SetLoseLabel(ObPoolStore pool, int i, bool hideObLoseZin, IDrawingCommandSink? sink)
    {
        var r = pool.GetRecord(i);
        var key = r.LabelDrawingKey;
        if (string.IsNullOrEmpty(key)) return;

        if (hideObLoseZin)
            EmitHide(key, sink);
        else
            EmitUpdateText(key, BuildLoseText(r), PineColors.BrokenGray, sink);
    }

    public static void UpdateZinVisibility(ObRecord r, bool hideObZin, IDrawingCommandSink? sink)
    {
        if (string.IsNullOrEmpty(r.LabelDrawingKey)) return;
        if (hideObZin)
            EmitHide(r.LabelDrawingKey, sink);
        else
            EmitUpdateText(r.LabelDrawingKey, BuildZinLabelText(r), LabelColorFor(r.Source), sink);
    }
}
