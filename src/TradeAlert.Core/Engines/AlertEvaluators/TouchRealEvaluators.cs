using System;
using System.Collections.Generic;
using System.Globalization;
using TradeAlert.Core.Models.Alerts;

namespace TradeAlert.Core.Engines.AlertEvaluators;

/// <summary>
/// TOUCH and REAL family evaluators — ports Pine lib_alerts1 logic (pine plot lines 5275-5306).
///
/// TOUCH — f_check_touch_zones(close[0], close[0], zones…)
///   canBuyTouchM5  = tTouch == 1 || tTouch == 0   (green only OR none)
///   canSellTouchM5 = tTouch == -1 || tTouch == 0  (red only OR none)
///
///   C# eval = Pine full-scan (CheckTouchZones price,price) OR buffered nearest (CheckTouchZonesBuffered).
///   Union ensures: (a) price inside ANY zone is detected even when nearest metric picks a different zone,
///   (b) near-miss buffer outside a box is still detected when Pine strict overlap misses.
///
/// REAL — Pine plot 5281-5306: active band from lastPushedSwingType, then
///   tActive = f_check_touch_zones(activeBot, activeTop, zones) (full-band overlap, both colors).
///   canBuyReal  = tActive == 1 || tActive == 0
///   canSellReal = tActive == -1 || tActive == 0
///   tActive: 0=none, 1=green only, -1=red only, 2=both
///
/// CanBuyRealAndM15CloseNow = canBuyReal AND M15CloseEdgeInjected
/// CanSellRealAndM15CloseNow = canSellReal AND M15CloseEdgeInjected
/// </summary>
public static class TouchRealEvaluators
{
    // -----------------------------------------------------------------------
    // TOUCH
    // -----------------------------------------------------------------------

    public static AlertEvaluationResult EvaluateTouchBuy(in AlertEvaluationContext ctx) =>
        EvaluateTouchDirection(ctx, AlertConditionId.CanBuyTouchM5, isBuy: true);

    public static AlertEvaluationResult EvaluateTouchSell(in AlertEvaluationContext ctx) =>
        EvaluateTouchDirection(ctx, AlertConditionId.CanSellTouchM5, isBuy: false);

    static AlertEvaluationResult EvaluateTouchDirection(
        in AlertEvaluationContext ctx,
        AlertConditionId id,
        bool isBuy)
    {
        if (!ctx.ActiveZoneCollectionAvailable || ctx.ZoneState == null)
        {
            return AlertEvaluationResult.NotFired(
                AlertReasonCodes.DepZoneCollectionUnavailable,
                "Zone collection payload not available for this evaluation.",
                dependencyMissing: true);
        }

        var (t, probePrice, probeKind) = ResolveZoneTouchT(ctx, id);
        bool fired = isBuy
            ? t == 1 || t == 0
            : t == -1 || t == 0;

        var pineName = id == AlertConditionId.CanBuyTouchM5 ? "canBuyTouchM5" : "canSellTouchM5";
        return fired
            ? new AlertEvaluationResult
              {
                  Fired = true,
                  ReasonCode = AlertReasonCodes.TouchFired,
                  ReasonText = $"{pineName}: tTouch={t} probe={probeKind} price={probePrice}",
                  StateDependencyMissing = false
              }
            : AlertEvaluationResult.NotFired(
                AlertReasonCodes.TouchNotFired,
                $"{pineName} false: tTouch={t} probe={probeKind} ({(isBuy ? "red zone blocks buy" : "green zone blocks sell")})",
                dependencyMissing: false);
    }

    /// <summary>
    /// Pine <c>f_check_touch_zones(close[0], close[0], …)</c> trên chart TF.
    /// Backtest HTF (H1/H4/…): khi nến HTF chưa đóng tại cạnh LTF, dùng <b>open</b> M5/M15
    /// (theo loại cond) thay HTF close — zones vẫn lấy từ engine HTF.
    /// C# eval = Pine full-scan OR buffered nearest (union — see CheckTouchZonesCombined).
    /// </summary>
    public static (int tTouch, double probePrice, string probeKind) ResolveZoneTouchT(
        in AlertEvaluationContext ctx,
        AlertConditionId id)
    {
        var zones = ctx.ZoneState!;
        var touchSourceMinutes = TouchSourceTfMinutes(id);
        var evalMinutes = TryParseTfMinutes(ctx.ChartTimeframeToken);

        if (!ctx.IsRealtime
            && evalMinutes.HasValue
            && evalMinutes.Value > touchSourceMinutes
            && ctx.TouchLtfOpenPrice is double ltfProbe)
        {
            var kind = ctx.TouchLtfProbeKind ?? $"ltfOpenM{touchSourceMinutes}";
            var t = CheckTouchZonesCombined(ltfProbe, zones);
            return (t, ltfProbe, kind);
        }

        var probe = zones.BarClose;
        return (CheckTouchZonesCombined(probe, zones), probe, "close");
    }

    static int TouchSourceTfMinutes(AlertConditionId id) =>
        id switch
        {
            AlertConditionId.CanBuyTouchM5 or AlertConditionId.CanSellTouchM5 => 5,
            _ => 5,
        };

    static int? TryParseTfMinutes(string tfToken)
    {
        if (int.TryParse(tfToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var m))
            return m;
        return tfToken switch
        {
            "60" => 60,
            "240" => 240,
            "1440" => 1440,
            _ => null,
        };
    }

    /// <summary>Full TOUCH probe detail for compound FIRE debug (nearest zones + eval vs HTF close).</summary>
    public readonly struct TouchEvalDetail
    {
        public int EvalTouchT { get; init; }
        public int HtfCloseTouchT { get; init; }
        /// <summary>Pine strict overlap result (price,price all zones).</summary>
        public int PineTouchT { get; init; }
        /// <summary>Buffered nearest-zone result.</summary>
        public int BufferedTouchT { get; init; }
        public double ProbePrice { get; init; }
        public string ProbeKind { get; init; }
        public bool GreenFound { get; init; }
        public double GreenBot { get; init; }
        public double GreenTop { get; init; }
        public bool GreenIsOb { get; init; }
        public bool TouchGreen { get; init; }
        public bool RedFound { get; init; }
        public double RedBot { get; init; }
        public double RedTop { get; init; }
        public bool RedIsOb { get; init; }
        public bool TouchRed { get; init; }
        public int GreenZoneCount { get; init; }
        public int RedZoneCount { get; init; }
    }

    public static TouchEvalDetail ComputeTouchDetail(in AlertEvaluationContext ctx, AlertConditionId id)
    {
        if (ctx.ZoneState == null)
            return default;

        var zones = ctx.ZoneState;
        var (evalT, probe, probeKind) = ResolveZoneTouchT(ctx, id);
        var pineT     = CheckTouchZones(probe, probe, zones);
        var bufferedT = CheckTouchZonesBuffered(probe, zones);
        var ng = SelectNearestGreen(probe, zones);
        var nr = SelectNearestRed(probe, zones);

        return new TouchEvalDetail
        {
            EvalTouchT       = evalT,
            HtfCloseTouchT   = zones.ZoneTouchResult,
            PineTouchT       = pineT,
            BufferedTouchT   = bufferedT,
            ProbePrice       = probe,
            ProbeKind        = probeKind,
            GreenFound       = ng.Found,
            GreenBot         = ng.Bot,
            GreenTop         = ng.Top,
            GreenIsOb        = ng.IsOb,
            TouchGreen       = ng.Found && BufferedTouchGreenAt(probe, ng),
            RedFound         = nr.Found,
            RedBot           = nr.Bot,
            RedTop           = nr.Top,
            RedIsOb          = nr.IsOb,
            TouchRed         = nr.Found && BufferedTouchRedAt(probe, nr),
            GreenZoneCount   = zones.GreenKeyZones.Count + zones.GreenObZones.Count,
            RedZoneCount     = zones.RedKeyZones.Count + zones.RedObZones.Count,
        };
    }

    public static string FormatTouchDebugText(in TouchEvalDetail d, AlertConditionId id)
    {
        if (d.ProbeKind == null)
            return "touch=zones-unavailable";

        var inv = CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();
        sb.Append("touch evalT=").Append(d.EvalTouchT);
        sb.Append(" probe=").Append(d.ProbePrice.ToString("F5", inv)).Append('@').Append(d.ProbeKind);
        if (d.PineTouchT != d.EvalTouchT || d.BufferedTouchT != d.EvalTouchT)
            sb.Append(" pineT=").Append(d.PineTouchT).Append(" bufT=").Append(d.BufferedTouchT);
        if (d.HtfCloseTouchT != d.EvalTouchT)
            sb.Append(" htfCloseT=").Append(d.HtfCloseTouchT);
        sb.Append(" zones=G").Append(d.GreenZoneCount).Append("/R").Append(d.RedZoneCount);

        if (d.GreenFound)
        {
            sb.Append(" nearG=[").Append(d.GreenBot.ToString("F3", inv)).Append(',')
              .Append(d.GreenTop.ToString("F3", inv)).Append(']')
              .Append(d.GreenIsOb ? "OB" : "KL")
              .Append(" tg=").Append(d.TouchGreen ? 'Y' : 'N');
        }
        else
        {
            sb.Append(" nearG=none");
        }

        if (d.RedFound)
        {
            sb.Append(" nearR=[").Append(d.RedBot.ToString("F3", inv)).Append(',')
              .Append(d.RedTop.ToString("F3", inv)).Append(']')
              .Append(d.RedIsOb ? "OB" : "KL")
              .Append(" tr=").Append(d.TouchRed ? 'Y' : 'N');
        }
        else
        {
            sb.Append(" nearR=none");
        }

        var isBuy = id == AlertConditionId.CanBuyTouchM5;
        var passed = isBuy
            ? d.EvalTouchT == 1 || d.EvalTouchT == 0
            : d.EvalTouchT == -1 || d.EvalTouchT == 0;
        sb.Append(" | ").Append(isBuy ? "canBuy" : "canSell").Append('=');
        if (passed)
        {
            sb.Append("OK(").Append(d.EvalTouchT switch
            {
                0  => "no-touch",
                1  => "green-touch",
                -1 => "red-touch",
                _  => "both-touch",
            }).Append(')');
        }
        else
        {
            sb.Append("BLOCK(").Append(d.EvalTouchT switch
            {
                1  => isBuy ? "ok" : "green-blocks-sell",
                -1 => isBuy ? "red-blocks-buy" : "ok",
                2  => "both-zones",
                _  => "unknown",
            }).Append(')');
        }

        return sb.ToString();
    }

    static bool BufferedTouchGreenAt(double price, NearestZoneBand band)
    {
        if (!band.Found) return false;
        var buffer = ZoneTouchBuffer(band.Top, band.Bot, band.IsOb);
        return price >= band.Bot && price - buffer <= band.Top;
    }

    static bool BufferedTouchRedAt(double price, NearestZoneBand band)
    {
        if (!band.Found) return false;
        var buffer = ZoneTouchBuffer(band.Top, band.Bot, band.IsOb);
        return price <= band.Top && price + buffer >= band.Bot;
    }

    // -----------------------------------------------------------------------
    // REAL
    // -----------------------------------------------------------------------

    public readonly struct RealZoneEvalDetail
    {
        public bool CanBuy { get; init; }
        public bool CanSell { get; init; }
        public string DebugText { get; init; }
    }

    public static RealZoneEvalDetail ComputeRealFlagsDetail(in AlertEvaluationContext ctx)
    {
        var rf = ctx.RealtimeFilter;
        if (rf?.Zones == null)
            return new RealZoneEvalDetail { DebugText = "no realtime filter/zones" };

        double? activeBot = null, activeTop = null;

        if (rf.LastPushedSwingType == -1)
        {
            if (rf.RealBuyBot == null || rf.RealBuyTop == null)
                return new RealZoneEvalDetail { DebugText = "buy realzone absent" };

            activeTop = rf.RealBuyTop;
            activeBot = rf.EffBuyBreakBot.HasValue
                ? Math.Min(rf.EffBuyBreakBot.Value, rf.RealBuyBot.Value)
                : rf.RealBuyBot;
        }
        else if (rf.LastPushedSwingType == 1)
        {
            if (rf.RealSellBot == null || rf.RealSellTop == null)
                return new RealZoneEvalDetail { DebugText = "sell realzone absent" };

            activeBot = rf.RealSellBot;
            activeTop = rf.EffSellBreakTop.HasValue
                ? Math.Max(rf.EffSellBreakTop.Value, rf.RealSellTop.Value)
                : rf.RealSellTop;
        }
        else
        {
            return new RealZoneEvalDetail { DebugText = $"lastPushed={rf.LastPushedSwingType}" };
        }

        if (activeBot == null || activeTop == null)
            return new RealZoneEvalDetail { DebugText = "active band null" };

        var bandBot = activeBot.Value;
        var bandTop = activeTop.Value;
        var zones = rf.Zones;
        var bandLabel = rf.LastPushedSwingType == -1 ? "buy" : "sell";

        // Band check: same-direction flag uses full-band overlap (Pine f_check_touch_zones).
        var tActive = CheckTouchZones(bandBot, bandTop, zones);

        // Touch check: opposite-direction flag uses direction-specific touch probe so that Pass D
        // (event-fire re-eval) injects the correct M5 high probe for canBuyReal or M5 low probe for canSellReal.
        //   Buy-band → canSell opposite uses CanSellTouchM5 probe (M5 low in Pass D sell-event re-eval).
        //   Sell-band → canBuy  opposite uses CanBuyTouchM5  probe (M5 high in Pass D buy-event  re-eval).
        bool canBuy, canSell;
        string dbg;
        if (rf.LastPushedSwingType == -1)
        {
            // Buy band active: canBuyReal from band (same dir), canSellReal from sell-touch (opposite dir).
            canBuy = tActive == 1 || tActive == 0;
            var (tTouchSell, tSellProbe, tSellKind) = ResolveZoneTouchT(ctx, AlertConditionId.CanSellTouchM5);
            canSell = tTouchSell == -1 || tTouchSell == 0;
            dbg = FormattableString.Invariant(
                $"{bandLabel} band=[{bandBot},{bandTop}] tActive={tActive} tTouchSell={tTouchSell}({tSellKind}@{tSellProbe}) canBuy={canBuy} canSell={canSell}");
        }
        else
        {
            // Sell band active: canSellReal from band (same dir), canBuyReal from buy-touch (opposite dir).
            canSell = tActive == -1 || tActive == 0;
            var (tTouchBuy, tBuyProbe, tBuyKind) = ResolveZoneTouchT(ctx, AlertConditionId.CanBuyTouchM5);
            canBuy = tTouchBuy == 1 || tTouchBuy == 0;
            dbg = FormattableString.Invariant(
                $"{bandLabel} band=[{bandBot},{bandTop}] tActive={tActive} tTouchBuy={tTouchBuy}({tBuyKind}@{tBuyProbe}) canBuy={canBuy} canSell={canSell}");
        }

        return new RealZoneEvalDetail { CanBuy = canBuy, CanSell = canSell, DebugText = dbg };
    }

    /// <summary>Pine plot 5305-5306: map <c>_tActive</c> to canBuyReal / canSellReal.</summary>
    public static (bool CanBuy, bool CanSell) MapPineRealFlags(int tActive) => tActive switch
    {
        0  => (true, true),
        1  => (true, false),
        -1 => (false, true),
        2  => (false, false),
        _  => (false, false),
    };

    public static (bool canBuy, bool canSell) ComputeRealFlags(in AlertEvaluationContext ctx)
    {
        var d = ComputeRealFlagsDetail(ctx);
        return (d.CanBuy, d.CanSell);
    }

    static string RealReasonSuffix(in AlertEvaluationContext ctx, in RealZoneEvalDetail detail) =>
        ctx.RealZoneDebug ? $" | {detail.DebugText}" : "";

    public static AlertEvaluationResult EvaluateRealBuy(in AlertEvaluationContext ctx)
    {
        if (!ctx.RealtimeFilterStateAvailable || ctx.RealtimeFilter == null)
        {
            return AlertEvaluationResult.NotFired(
                AlertReasonCodes.DepRealtimeFilterUnavailable,
                "Realtime filter payload not available for this evaluation.",
                dependencyMissing: true);
        }

        var rf = ctx.RealtimeFilter;
        if (rf.LastPushedSwingType == -1 && (rf.RealBuyBot == null || rf.RealBuyTop == null))
        {
            return AlertEvaluationResult.NotFired(
                AlertReasonCodes.RealAnchorAbsent,
                $"canBuyReal: buy realzone not yet formed (lastPushed={rf.LastPushedSwingType})",
                dependencyMissing: false);
        }

        var detail = ComputeRealFlagsDetail(ctx);
        var canBuy = detail.CanBuy;
        var dbg = RealReasonSuffix(in ctx, in detail);

        return canBuy
            ? new AlertEvaluationResult
              {
                  Fired = true,
                  ReasonCode = AlertReasonCodes.RealFired,
                  ReasonText = $"canBuyReal: realzone buy active lastPushed={rf.LastPushedSwingType}{dbg}",
                  StateDependencyMissing = false
              }
            : AlertEvaluationResult.NotFired(
                AlertReasonCodes.RealNotFired,
                $"canBuyReal false: red zone blocks buy lastPushed={rf.LastPushedSwingType}{dbg}",
                dependencyMissing: false);
    }

    public static AlertEvaluationResult EvaluateRealSell(in AlertEvaluationContext ctx)
    {
        if (!ctx.RealtimeFilterStateAvailable || ctx.RealtimeFilter == null)
        {
            return AlertEvaluationResult.NotFired(
                AlertReasonCodes.DepRealtimeFilterUnavailable,
                "Realtime filter payload not available for this evaluation.",
                dependencyMissing: true);
        }

        var rf = ctx.RealtimeFilter;
        var detail = ComputeRealFlagsDetail(ctx);
        var canSell = detail.CanSell;
        var dbg = RealReasonSuffix(in ctx, in detail);

        return canSell
            ? new AlertEvaluationResult
              {
                  Fired = true,
                  ReasonCode = AlertReasonCodes.RealFired,
                  ReasonText = $"canSellReal: realzone sell active lastPushed={rf.LastPushedSwingType}{dbg}",
                  StateDependencyMissing = false
              }
            : AlertEvaluationResult.NotFired(
                AlertReasonCodes.RealNotFired,
                $"canSellReal false: green zone blocks sell lastPushed={rf.LastPushedSwingType}{dbg}",
                dependencyMissing: false);
    }

    // -----------------------------------------------------------------------
    // REAL + M15 CLOSE composites
    // -----------------------------------------------------------------------

    public static AlertEvaluationResult EvaluateRealBuyAndM15Close(in AlertEvaluationContext ctx)
    {
        // M15 edge check FIRST — preserves existing test contract (CARRY_OVER before filter dep)
        if (!ctx.M15CloseEdgeInjected)
        {
            return AlertEvaluationResult.NotFired(
                AlertReasonCodes.CarryOverMissingM15Edge,
                "CanBuyRealAndM15Close: M15 edge not present on this bar.",
                dependencyMissing: false);
        }

        if (!ctx.RealtimeFilterStateAvailable || ctx.RealtimeFilter == null)
        {
            return AlertEvaluationResult.NotFired(
                AlertReasonCodes.DepRealtimeFilterUnavailable,
                "Realtime filter payload not available for this evaluation.",
                dependencyMissing: true);
        }

        var detail = ComputeRealFlagsDetail(ctx);
        var dbg = RealReasonSuffix(in ctx, in detail);
        return detail.CanBuy
            ? new AlertEvaluationResult
              {
                  Fired = true,
                  ReasonCode = AlertReasonCodes.RealM15Fired,
                  ReasonText = $"CanBuyRealAndM15Close: canBuyReal=T AND M15 edge present.{dbg}",
                  StateDependencyMissing = false
              }
            : AlertEvaluationResult.NotFired(
                AlertReasonCodes.RealM15NotFired,
                $"CanBuyRealAndM15Close: canBuyReal=F (red zone blocks).{dbg}",
                dependencyMissing: false);
    }

    public static AlertEvaluationResult EvaluateRealSellAndM15Close(in AlertEvaluationContext ctx)
    {
        if (!ctx.M15CloseEdgeInjected)
        {
            return AlertEvaluationResult.NotFired(
                AlertReasonCodes.CarryOverMissingM15Edge,
                "CanSellRealAndM15Close: M15 edge not present on this bar.",
                dependencyMissing: false);
        }

        if (!ctx.RealtimeFilterStateAvailable || ctx.RealtimeFilter == null)
        {
            return AlertEvaluationResult.NotFired(
                AlertReasonCodes.DepRealtimeFilterUnavailable,
                "Realtime filter payload not available for this evaluation.",
                dependencyMissing: true);
        }

        var detail = ComputeRealFlagsDetail(ctx);
        var dbg = RealReasonSuffix(in ctx, in detail);
        return detail.CanSell
            ? new AlertEvaluationResult
              {
                  Fired = true,
                  ReasonCode = AlertReasonCodes.RealM15Fired,
                  ReasonText = $"CanSellRealAndM15Close: canSellReal=T AND M15 edge present.{dbg}",
                  StateDependencyMissing = false
              }
            : AlertEvaluationResult.NotFired(
                AlertReasonCodes.RealM15NotFired,
                $"CanSellRealAndM15Close: canSellReal=F (green zone blocks).{dbg}",
                dependencyMissing: false);
    }

    // -----------------------------------------------------------------------
    // Unified dispatch
    // -----------------------------------------------------------------------

    public static AlertEvaluationResult EvaluateById(
        AlertConditionId id,
        in AlertEvaluationContext ctx) => id switch
    {
        AlertConditionId.CanBuyTouchM5             => EvaluateTouchBuy(ctx),
        AlertConditionId.CanSellTouchM5            => EvaluateTouchSell(ctx),
        AlertConditionId.CanBuyReal                => EvaluateRealBuy(ctx),
        AlertConditionId.CanSellReal               => EvaluateRealSell(ctx),
        AlertConditionId.CanBuyRealAndM15CloseNow  => EvaluateRealBuyAndM15Close(ctx),
        AlertConditionId.CanSellRealAndM15CloseNow => EvaluateRealSellAndM15Close(ctx),
        _ => AlertEvaluationResult.NotFired("DISPATCH_MISS", $"TouchRealEvaluators: no handler for {id}", false)
    };

    // -----------------------------------------------------------------------
    // TOUCH M5 — combined Pine full-scan + buffered nearest (union)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Decompose tTouch into green/red flags.
    /// </summary>
    static (bool green, bool red) TouchFlags(int t) => (t == 1 || t == 2, t == -1 || t == 2);

    static int CombineTouchFlags(bool touchGreen, bool touchRed) =>
        touchGreen && touchRed ? 2 : touchGreen ? 1 : touchRed ? -1 : 0;

    /// <summary>
    /// Combined TOUCH: Pine strict overlap (all zones) OR buffered nearest.
    /// Union ensures price-inside-zone is never missed even when nearest metric
    /// selects a different zone, while preserving the near-miss buffer extension.
    /// </summary>
    public static int CheckTouchZonesCombined(double price, ZoneState zones)
    {
        var pineT     = CheckTouchZones(price, price, zones);
        var bufferedT = CheckTouchZonesBuffered(price, zones);
        var (pg, pr)  = TouchFlags(pineT);
        var (bg, br)  = TouchFlags(bufferedT);
        return CombineTouchFlags(pg || bg, pr || br);
    }

    // -----------------------------------------------------------------------
    // TOUCH M5 — nearest zone + edge buffer (Key a=1, OB a=2)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Touch M5 (all TFs): nearest red by |price−bot|, nearest green by |price−top|.
    /// Red touch: price ≤ top and price + height/(8a) ≥ bot.
    /// Green touch: price ≥ bot and price − height/(8a) ≤ top.
    /// Used internally by CheckTouchZonesCombined; kept public for cancel-rule usage.
    /// </summary>
    public static int CheckTouchZonesBuffered(double price, ZoneState zones)
    {
        var touchGreen = TryBufferedTouchGreen(price, zones);
        var touchRed   = TryBufferedTouchRed(price, zones);
        return touchGreen && touchRed ? 2 : touchGreen ? 1 : touchRed ? -1 : 0;
    }

    readonly struct NearestZoneBand
    {
        public bool Found { get; init; }
        public double BestDist { get; init; }
        public double Bot { get; init; }
        public double Top { get; init; }
        public bool IsOb { get; init; }
    }

    static bool TryBufferedTouchRed(double price, ZoneState zones)
    {
        var nearest = SelectNearestRed(price, zones);
        if (!nearest.Found) return false;

        var buffer = ZoneTouchBuffer(nearest.Top, nearest.Bot, nearest.IsOb);
        return price <= nearest.Top && price + buffer >= nearest.Bot;
    }

    static bool TryBufferedTouchGreen(double price, ZoneState zones)
    {
        var nearest = SelectNearestGreen(price, zones);
        if (!nearest.Found) return false;

        var buffer = ZoneTouchBuffer(nearest.Top, nearest.Bot, nearest.IsOb);
        return price >= nearest.Bot && price - buffer <= nearest.Top;
    }

    static double ZoneTouchBuffer(double top, double bot, bool isOb)
    {
        var height = top - bot;
        if (height <= 0) return 0;
        return height / (8.0 * (isOb ? 2 : 1));
    }

    static NearestZoneBand SelectNearestRed(double price, ZoneState zones)
    {
        var result = default(NearestZoneBand);
        ConsiderNearestRed(ref result, zones.RedKeyZones, isOb: false, price);
        ConsiderNearestRed(ref result, zones.RedObZones, isOb: true, price);
        return result;
    }

    static NearestZoneBand SelectNearestGreen(double price, ZoneState zones)
    {
        var result = default(NearestZoneBand);
        ConsiderNearestGreen(ref result, zones.GreenKeyZones, isOb: false, price);
        ConsiderNearestGreen(ref result, zones.GreenObZones, isOb: true, price);
        return result;
    }

    static void ConsiderNearestRed(
        ref NearestZoneBand best,
        IReadOnlyList<(double Low, double High)> list,
        bool isOb,
        double price)
    {
        foreach (var z in list)
        {
            var dist = Math.Abs(price - z.Low);
            if (!best.Found || dist < best.BestDist || (dist == best.BestDist && best.IsOb && !isOb))
            {
                best = new NearestZoneBand
                {
                    Found    = true,
                    BestDist = dist,
                    Bot      = z.Low,
                    Top      = z.High,
                    IsOb     = isOb,
                };
            }
        }
    }

    static void ConsiderNearestGreen(
        ref NearestZoneBand best,
        IReadOnlyList<(double Low, double High)> list,
        bool isOb,
        double price)
    {
        foreach (var z in list)
        {
            var dist = Math.Abs(price - z.High);
            if (!best.Found || dist < best.BestDist || (dist == best.BestDist && best.IsOb && !isOb))
            {
                best = new NearestZoneBand
                {
                    Found    = true,
                    BestDist = dist,
                    Bot      = z.Low,
                    Top      = z.High,
                    IsOb     = isOb,
                };
            }
        }
    }

    // -----------------------------------------------------------------------
    // REAL — Pine f_check_touch_zones(rangeLow, rangeHigh, …) full-band overlap
    // -----------------------------------------------------------------------

    public static int CheckTouchZones(double rangeLow, double rangeHigh, ZoneState zones)
    {
        bool touchGreen = false;
        bool touchRed   = false;

        foreach (var z in zones.GreenKeyZones)
            if (!(rangeHigh < z.Low || rangeLow > z.High)) { touchGreen = true; break; }

        if (!touchGreen)
            foreach (var z in zones.GreenObZones)
                if (!(rangeHigh < z.Low || rangeLow > z.High)) { touchGreen = true; break; }

        foreach (var z in zones.RedKeyZones)
            if (!(rangeHigh < z.Low || rangeLow > z.High)) { touchRed = true; break; }

        if (!touchRed)
            foreach (var z in zones.RedObZones)
                if (!(rangeHigh < z.Low || rangeLow > z.High)) { touchRed = true; break; }

        return touchGreen && touchRed ? 2 : touchGreen ? 1 : touchRed ? -1 : 0;
    }
}
