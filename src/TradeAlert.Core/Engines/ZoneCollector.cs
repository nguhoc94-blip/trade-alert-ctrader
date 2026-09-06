using System.Collections.Generic;
using TradeAlert.Core.Engines.AlertEvaluators;
using TradeAlert.Core.Models.Alerts;
using TradeAlert.Core.Models.KeyLevel;

namespace TradeAlert.Core.Engines;

/// <summary>
/// Build ZoneState từ ObPoolStore + PivotStateStore — port chính xác Pine
/// <c>f_collect_active_zones</c> (lib_alerts new lines 75-152).
///
/// Filter chung (mọi key):
///   skip nếu <c>!pivHasKey[i]</c>, <c>kb == null</c>, <c>!pivKeyExtending[i]</c>.
///
/// Phân loại xanh/đỏ (Pine lines 110-120):
///   - typ == -1 (Low pivot): green nếu flag ∈ {1,3,4,7,5,50} hoặc (flag==0 và mainRole==1)
///   - typ ==  1 (High pivot): red  nếu flag ∈ {1,3,4,7,5,50} hoặc (flag==0 và mainRole==1)
///   - Bổ sung broken (lines 116-120, second branch — KHÔNG else-if):
///     nếu flag ∈ {-3, 2, -2, 6}:
///       typ == 1 (broken HIGH) → green  (support sau khi vỡ)
///       typ == -1 (broken LOW) → red    (resistance sau khi vỡ)
///
/// OB (Pine lines 129-149):
///   - skip nếu obState != 2 (ZIN), <c>!obExtending</c>, na(ob).
///   - obType == -1 (OB đỏ = support) → green
///   - obType ==  1 (OB xanh = resistance) → red
/// </summary>
public static class ZoneCollector
{
    /// <summary>
    /// Tập hợp zones đang active + compute ZoneTouchResult (Pine full-scan OR buffered — combined).
    /// </summary>
    public static ZoneState Collect(
        ObPoolStore obPool,
        IPivotKeyBoxView pivotKeyBoxes,
        double barClose)
    {
        var greenKey = new List<(double Low, double High)>();
        var redKey   = new List<(double Low, double High)>();
        var greenOb  = new List<(double Low, double High)>();
        var redOb    = new List<(double Low, double High)>();

        for (var i = 0; i < pivotKeyBoxes.Count; i++)
        {
            if (!pivotKeyBoxes.GetHasKey(i)) continue;

            var kb = pivotKeyBoxes.GetKeyBox(i);
            if (kb is null) continue;

            if (!pivotKeyBoxes.GetKeyExtending(i)) continue;

            var typ      = pivotKeyBoxes.GetType(i);
            var flag     = pivotKeyBoxes.GetFlag(i);
            var mainRole = pivotKeyBoxes.GetMainRole(i);
            var top    = kb.Spec.Top;
            var bot    = kb.Spec.Bottom;
            var lo  = System.Math.Min(top, bot);
            var hi  = System.Math.Max(top, bot);

            ClassifyKeyZone(typ, flag, mainRole, out var isGreen, out var isRed);

            if (isGreen) greenKey.Add((lo, hi));
            if (isRed)   redKey.Add((lo, hi));
        }

        // OB zones — Pine lines 129-149
        for (var i = 0; i < obPool.Count; i++)
        {
            var r = obPool.GetRecord(i);
            if (r.State != 2) continue;
            if (!r.Extending) continue;
            if (r.Box is null) continue;

            var top = r.Box.Top;
            var bot = r.Box.Bottom;
            var lo = System.Math.Min(top, bot);
            var hi = System.Math.Max(top, bot);

            // OB đỏ (type=-1) = support → green; OB xanh (type=1) = resistance → red
            if (r.Type == -1)
                greenOb.Add((lo, hi));
            else if (r.Type == 1)
                redOb.Add((lo, hi));
        }

        var zoneState = new ZoneState
        {
            GreenKeyZones = greenKey,
            RedKeyZones   = redKey,
            GreenObZones  = greenOb,
            RedObZones    = redOb,
            BarClose      = barClose,
        };

        return new ZoneState
        {
            GreenKeyZones    = zoneState.GreenKeyZones,
            RedKeyZones      = zoneState.RedKeyZones,
            GreenObZones     = zoneState.GreenObZones,
            RedObZones       = zoneState.RedObZones,
            BarClose         = barClose,
            ZoneTouchResult  = TouchRealEvaluators.CheckTouchZonesCombined(barClose, zoneState),
        };
    }

    /// <summary>Mirror Pine f_collect_active_zones key classify (lines 110-120).</summary>
    internal static void ClassifyKeyZone(int typ, int flag, int mainRole, out bool isGreen, out bool isRed)
    {
        isGreen = false;
        isRed   = false;

        bool nonBrokenMatch =
            flag == 1 || flag == 3 || flag == 4 || flag == 7
            || (flag == 0 && mainRole == 1)
            || flag == 5 || flag == 50;

        if (typ == -1 && nonBrokenMatch) isGreen = true;
        else if (typ == 1 && nonBrokenMatch) isRed = true;

        if (flag == -3 || flag == 2 || flag == -2 || flag == 6)
        {
            if (typ == 1)       isGreen = true;
            else if (typ == -1) isRed   = true;
        }
    }
}

/// <summary>Read-only facade — pivot key boxes + flags + extending/mainRole cho ZoneCollector.</summary>
public interface IPivotKeyBoxView
{
    int Count { get; }
    KeyBoxRef? GetKeyBox(int i);
    int GetType(int i);
    int GetFlag(int i);
    bool GetHasKey(int i);
    bool GetKeyExtending(int i);
    int GetMainRole(int i);
}
