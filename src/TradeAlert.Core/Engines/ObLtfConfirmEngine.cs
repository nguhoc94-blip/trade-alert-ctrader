using TradeAlert.Core.Drawing;
using TradeAlert.Core.Models.KeyLevel;

namespace TradeAlert.Core.Engines;

/// <summary>
/// Pine <c>f_check_ob_ltf_confirm_full_range</c> + confirm tại <c>checkBar</c> (visibility loop).
/// </summary>
public static class ObLtfConfirmEngine
{
    /// <summary>
    /// <see cref="ObLtfConfirmKind"/> phân loại auto-confirm vs confirm chuẩn cho label debug.
    /// <see cref="LtfScanHigh"/>/<see cref="LtfScanLow"/> = min/max LTF H/L trong cửa sổ quét (debug label).
    /// </summary>
    public readonly record struct ObLtfConfirmResult(
        bool? IsConfirm,
        int TouchCount,
        int HasLtfBars,
        int TotalScanBars,
        ObLtfConfirmKind Kind,
        double LtfScanHigh = double.NaN,
        double LtfScanLow = double.NaN,
        int LtfFlatCandles = 0,
        int BufferMissBars = 0,
        int ScanMaxBar = -1);

    public readonly record struct ObLtfConfirmApplyContext(
        int ConfirmBarIndex,
        ObLtfConfirmPath Path,
        int BufferFlatCount);

    /// <summary>Quét LTF trong một HTF bar (Pine checkBar = bar_index-2).</summary>
    public static ObLtfConfirmResult ConfirmCheckBar(
        int checkBar,
        double obX,
        int typOb,
        LtfRingBufferStore buffer)
    {
        if (!buffer.TryGetRange(checkBar, out var start, out var end))
            return new ObLtfConfirmResult(null, 0, 0, 1, ObLtfConfirmKind.NoLtfData,
                BufferMissBars: 1, ScanMaxBar: checkBar);

        return ScanLtfRange(start, end, obX, typOb, buffer, hasLtfBars: 1, totalScanBars: 1, scanMaxBar: checkBar);
    }

    /// <summary>Pine <c>f_check_ob_ltf_confirm_full_range</c> — touchCount tích lũy trên toàn scan range.</summary>
    public static ObLtfConfirmResult CheckFullRange(
        int barIndex,
        int barOb,
        double obX,
        int typOb,
        int scanRange,
        LtfRingBufferStore buffer)
    {
        var effectiveScan = Math.Min(scanRange, buffer.Capacity);
        var maxScan = Math.Min(barIndex - 2, barOb + effectiveScan);

        var touchCount = 0;
        var hasFullPierce = false;
        var hasLtfBars = 0;
        var totalScanBars = 0;
        var flatCandles = 0;
        var bounds = new LtfScanBounds();

        for (var b = barOb + 1; b <= maxScan; b++)
        {
            totalScanBars++;
            if (!buffer.TryGetRange(b, out var start, out var end))
                continue;

            hasLtfBars++;
            flatCandles += end - start + 1;
            for (var i = start; i <= end; i++)
            {
                var (o, h, l, c) = buffer.FlatOhlcAt(i);
                bounds.Include(h, l);

                if (!OBEngine.IsTouch(typOb, obX, h, l))
                    continue;

                touchCount++;
                var full = OBEngine.IsFullPierce(typOb, obX, o, h, l, c);

                if (full && touchCount == 1)
                    hasFullPierce = true;
                else if (touchCount >= 2)
                    return MakeResult(false, touchCount, hasLtfBars, totalScanBars, ObLtfConfirmKind.Pending, bounds,
                        flatCandles, totalScanBars - hasLtfBars, maxScan);
            }
        }

        var miss = totalScanBars - hasLtfBars;
        if (hasLtfBars == 0)
            return new ObLtfConfirmResult(null, 0, 0, totalScanBars, ObLtfConfirmKind.NoLtfData,
                BufferMissBars: miss, ScanMaxBar: maxScan);
        if (touchCount == 0)
            return MakeResult(true, 0, hasLtfBars, totalScanBars, ObLtfConfirmKind.HasLtfNoTouch, bounds,
                flatCandles, miss, maxScan);
        if (touchCount == 1 && hasFullPierce)
            return MakeResult(true, 1, hasLtfBars, totalScanBars, ObLtfConfirmKind.Standard, bounds,
                flatCandles, miss, maxScan);

        return MakeResult(false, touchCount, hasLtfBars, totalScanBars, ObLtfConfirmKind.Pending, bounds,
            flatCandles, miss, maxScan);
    }

    static ObLtfConfirmResult ScanLtfRange(
        int start, int end, double obX, int typOb,
        LtfRingBufferStore buffer, int hasLtfBars, int totalScanBars, int scanMaxBar)
    {
        var touchCount = 0;
        var hasFullPierce = false;
        var bounds = new LtfScanBounds();
        var flatCandles = end - start + 1;

        for (var i = start; i <= end; i++)
        {
            var (o, h, l, c) = buffer.FlatOhlcAt(i);
            bounds.Include(h, l);

            if (!OBEngine.IsTouch(typOb, obX, h, l))
                continue;

            touchCount++;
            var full = OBEngine.IsFullPierce(typOb, obX, o, h, l, c);

            if (full && touchCount == 1)
            {
                hasFullPierce = true;
                continue;
            }

            if (touchCount >= 2 || !full)
                return MakeResult(false, touchCount, hasLtfBars, totalScanBars, ObLtfConfirmKind.Pending, bounds,
                    flatCandles, totalScanBars - hasLtfBars, scanMaxBar);
        }

        if (touchCount == 0)
            return MakeResult(true, 0, hasLtfBars, totalScanBars, ObLtfConfirmKind.HasLtfNoTouch, bounds,
                flatCandles, totalScanBars - hasLtfBars, scanMaxBar);
        if (touchCount == 1 && hasFullPierce)
            return MakeResult(true, 1, hasLtfBars, totalScanBars, ObLtfConfirmKind.Standard, bounds,
                flatCandles, totalScanBars - hasLtfBars, scanMaxBar);

        return MakeResult(false, touchCount, hasLtfBars, totalScanBars, ObLtfConfirmKind.Pending, bounds,
            flatCandles, totalScanBars - hasLtfBars, scanMaxBar);
    }

    static ObLtfConfirmResult MakeResult(
        bool? isConfirm,
        int touchCount,
        int hasLtfBars,
        int totalScanBars,
        ObLtfConfirmKind kind,
        in LtfScanBounds bounds,
        int flatCandles = 0,
        int bufferMissBars = 0,
        int scanMaxBar = -1)
    {
        bounds.TryGet(out var hi, out var lo);
        return new ObLtfConfirmResult(isConfirm, touchCount, hasLtfBars, totalScanBars, kind, hi, lo,
            flatCandles, bufferMissBars, scanMaxBar);
    }

    struct LtfScanBounds
    {
        double _high;
        double _low;
        bool _has;

        public void Include(double high, double low)
        {
            if (!_has)
            {
                _high = high;
                _low = low;
                _has = true;
                return;
            }

            if (high > _high) _high = high;
            if (low < _low) _low = low;
        }

        public bool TryGet(out double high, out double low)
        {
            high = _high;
            low = _low;
            return _has;
        }
    }

    /// <summary>Áp dụng kết quả LTF lên pool (reject → LOSE + xóa box).</summary>
    public static void ApplyOutcome(
        int obIdx,
        ObLtfConfirmResult result,
        ObPoolStore pool,
        bool hideObLoseZin,
        IDrawingCommandSink? sink,
        in ObLtfConfirmApplyContext ctx = default)
    {
        pool.SetLtfHasBars(obIdx, result.HasLtfBars);
        pool.SetLtfTotalBars(obIdx, result.TotalScanBars);
        pool.SetLtfScanHigh(obIdx, result.LtfScanHigh);
        pool.SetLtfScanLow(obIdx, result.LtfScanLow);
        pool.SetLtfTouchCount(obIdx, result.TouchCount);
        pool.SetLtfFlatCandles(obIdx, result.LtfFlatCandles);
        pool.SetLtfBufferMissBars(obIdx, result.BufferMissBars);
        pool.SetLtfScanMaxBar(obIdx, result.ScanMaxBar);
        pool.SetLtfConfirmBar(obIdx, ctx.ConfirmBarIndex);
        pool.SetLtfConfirmPath(obIdx, ctx.Path);
        pool.SetLtfBufferFlatAtConfirm(obIdx, ctx.BufferFlatCount);

        if (!result.IsConfirm.HasValue)
        {
            pool.SetFlagLtf(obIdx, 2);
            pool.SetLtfConfirmKind(obIdx, ObLtfConfirmKind.NoLtfData);
            return;
        }

        if (result.IsConfirm == true)
        {
            pool.SetFlagLtf(obIdx, 2);
            pool.SetLtfConfirmKind(obIdx, result.Kind);
            return;
        }

        pool.SetFlagLtf(obIdx, 3);
        pool.SetState(obIdx, 3);
        pool.SetBoxMissingReason(obIdx, ObBoxMissingReason.LtfReject);
        ObLabelDrawEngine.SetLoseLabel(pool, obIdx, hideObLoseZin, sink);

        var r = pool.GetRecord(obIdx);
        if (r.Box == null) return;

        ObDrawEngine.EmitDelete(r.Box, sink);
        pool.SetBox(obIdx, null);
        pool.SetExtending(obIdx, false);
    }
}
