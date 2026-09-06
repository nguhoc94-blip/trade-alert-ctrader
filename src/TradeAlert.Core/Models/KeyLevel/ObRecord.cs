namespace TradeAlert.Core.Models.KeyLevel;

/// <summary>Một dòng OB pool — mirror parallel arrays.</summary>
public sealed class ObRecord
{
    public int State { get; set; }
    public int Count { get; set; }
    public double X { get; set; }
    public int Bar { get; set; }
    public int Type { get; set; }
    public int Owner { get; set; }
    public int Source { get; set; }
    public int Number { get; set; }
    public int PivotType { get; set; }
    public int PivotHighId { get; set; }
    public int PivotLowId { get; set; }
    public int LastCheckedBar { get; set; }
    public ObBoxSpec? Box { get; set; }
    public bool Extending { get; set; }
    public int FlagLtf { get; set; }
    public int LtfHasBars { get; set; }
    public int LtfTotalBars { get; set; }
    /// <summary>Max LTF high trong cửa sổ LTF confirm (debug label).</summary>
    public double LtfScanHigh { get; set; }
    /// <summary>Min LTF low trong cửa sổ LTF confirm (debug label).</summary>
    public double LtfScanLow { get; set; }
    public ObLtfConfirmKind LtfConfirmKind { get; set; }
    /// <summary>Số lần touch trên LTF khi confirm (debug).</summary>
    public int LtfTouchCount { get; set; }
    /// <summary>Tổng số nến LTF (M5/M2…) đã quét (debug).</summary>
    public int LtfFlatCandles { get; set; }
    /// <summary>HTF bars trong scan range không có snapshot LTF trong buffer.</summary>
    public int LtfBufferMissBars { get; set; }
    /// <summary>HTF bar cuối của cửa sổ scan LTF (debug).</summary>
    public int LtfScanMaxBar { get; set; }
    /// <summary><see cref="barIndex"/> hoặc <c>checkBar</c> lúc confirm chạy.</summary>
    public int LtfConfirmBar { get; set; }
    public ObLtfConfirmPath LtfConfirmPath { get; set; }
    /// <summary><see cref="LtfRingBufferStore.FlatCount"/> tại thời điểm confirm.</summary>
    public int LtfBufferFlatAtConfirm { get; set; }
    public string? LabelDrawingKey { get; set; }
    public ObBoxMissingReason BoxMissingReason { get; set; }
    public ObOverlapRival OverlapRival { get; set; } = ObOverlapRival.None;
}
