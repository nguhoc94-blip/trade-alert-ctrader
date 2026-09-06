using System;
using TradeAlert.Core.Models.KeyLevel;

namespace TradeAlert.Core.Engines;

/// <summary>
/// Pivot push detector — port "bypass mode" của Pine pine plot lines 522-535
/// (<c>if not useNewPullbackFilter</c>).
///
/// Quy tắc (Pine bypass, đã giản hóa cho LOOP6 — không có pullback filter):
///   1) Bar A = bar_index - 1 (vừa đóng) — dùng OHLC tại offset [1] từ bar đánh giá.
///   2) Yêu cầu non-doji & clear body (effClearBody=true) — tận dụng helper sẵn có.
///   3) Direction-aware alternation: HIGH chỉ accept nếu lastPushedSwingType != 1;
///      LOW chỉ accept nếu lastPushedSwingType != -1.
///   4) Color match: HIGH cần bullish (close[1]&gt;open[1]); LOW cần bearish.
///
/// LƯU Ý: Đây là Pine "bypass" path (đường mặc định khi useNewPullbackFilter=false).
/// Path "useNewPullbackFilter=true" với penHCand/penLCand A1/A2/A3 chưa port — sẽ refine sau.
/// </summary>
public sealed class PivotDetector
{
    public int HighIdCounter { get; private set; }
    public int LowIdCounter  { get; private set; }
    public int LastPushedSwingType { get; private set; }  // 1=HIGH, -1=LOW, 0=none

    public readonly record struct DetectResult(
        bool Pushed,
        int  PoolIndex,
        int  Type,           // 1=HIGH, -1=LOW
        int  PivotBarIndex,
        double PivotPrice);

    public DetectResult TryPush(
        int     barIndex,
        PivotStateStore pivots,
        double  effOpen1,
        double  effHigh1,
        double  effLow1,
        double  effClose1,
        bool    effClearBody,
        bool    effIsDoji,
        double  avgBody,
        int     timeMsAtBar1,
        int?    lastPushedOverride = null)
    {
        if (barIndex < 1) return default;
        if (effIsDoji || !effClearBody) return default;

        // Pine: newSwing3etected is determined at bar start using PRE-RESCUE lastPushedSwingType.
        // Caller passes lastPushedOverride=preMicroSwingType to prevent spurious double-push
        // when a micro-swing rescue has already updated LastPushedSwingType this bar.
        var lastForAlt = lastPushedOverride ?? LastPushedSwingType;

        var pivBar = barIndex - 1;
        var bull   = effClose1 > effOpen1;
        var bear   = effClose1 < effOpen1;

        // HIGH candidate: bullish bar + alternation
        if (bull && lastForAlt != 1)
        {
            HighIdCounter++;
            var idx = pivots.PushPivot(
                price:    effHigh1,
                barIndex: pivBar,
                type:     1,
                timeMs:   timeMsAtBar1,
                highId:   HighIdCounter,
                lowId:    0);
            LastPushedSwingType = 1;
            return new DetectResult(true, idx, 1, pivBar, effHigh1);
        }

        // LOW candidate
        if (bear && lastForAlt != -1)
        {
            LowIdCounter++;
            var idx = pivots.PushPivot(
                price:    effLow1,
                barIndex: pivBar,
                type:     -1,
                timeMs:   timeMsAtBar1,
                highId:   0,
                lowId:    LowIdCounter);
            LastPushedSwingType = -1;
            return new DetectResult(true, idx, -1, pivBar, effLow1);
        }

        return default;
    }

    /// <summary>Register a rescue (micro-swing) push without full pivot detection — syncs alternation.</summary>
    public void RegisterRescuePush(int type, int pivotType)
    {
        if (pivotType ==  1) HighIdCounter++;
        if (pivotType == -1) LowIdCounter++;
        LastPushedSwingType = type;
    }

    public void Reset()
    {
        HighIdCounter = 0;
        LowIdCounter  = 0;
        LastPushedSwingType = 0;
    }

    /// <summary>
    /// Seed the alternation gate from carry-over state so Pine's historical
    /// lastPushedSwingType is honoured from bar 0 of a new session.
    /// </summary>
    public void PreloadLastPushedSwingType(int lastPushedSwingType)
    {
        LastPushedSwingType = lastPushedSwingType;
    }
}
