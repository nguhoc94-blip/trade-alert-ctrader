using System;

namespace TradeAlert.Core.Engines;

/// <summary>
/// Real-zone state machine — port Pine f_rz_buy / f_rz_sell (lines 4997-5083 / 5085-5174).
///
/// PHIÊN BẢN LOOP 8 REV002 — đầy đủ R1/R1'/R2/R3:
///
///   BUY zone — K = bar[0], S root = bar[1]:
///     R1' (M5): G = floor(min(S.close-t, bodyLoK-t)/t)*t; F=G+t; top=max(K.high,F)
///     R1  (non-M5): G = floor(min(sLo+|sBody|/3, bodyLoK-t)/t)*t; ...
///     R2  (non-M5): S=bar[2] (anchor), C=bar[1] (confirm); G from S upper-third capped by C.bodyLo
///     R3  (all TF): K must be bull-clear; scan S=bar[1..breakR3MaxK];
///                   K.close > S.high; G = min(S.close-t, minMid between S and K)
///
///   SELL zone — mirror BUY with bear direction.
///
///   Best candidate rule (per Pine): prefer highest top (buy) / lowest bot (sell),
///   then higher F, then lower priority, then smaller lagS.
///
///   Reset khi swing mới push (opposite direction).
/// </summary>
public sealed class RealZoneEngine
{
    public int LastPushedSwingType { get; private set; }

    public double? RealBuyTop  { get; private set; }
    public double? RealBuyBot  { get; private set; }
    public double? RealSellTop { get; private set; }
    public double? RealSellBot { get; private set; }

    public double? EffBuyBreakBot  { get; private set; }
    public double? EffSellBreakTop { get; private set; }

    public int? BuyRealAnchorBar  { get; private set; }
    public int? SellRealAnchorBar { get; private set; }

    int    _buyPriority;
    int    _buyLagS;
    double _buyF;
    int    _sellPriority;
    int    _sellLagS;
    double _sellF;

    public void Reset()
    {
        LastPushedSwingType = 0;
        RealBuyTop = RealBuyBot = RealSellTop = RealSellBot = null;
        EffBuyBreakBot = EffSellBreakTop = null;
        BuyRealAnchorBar = SellRealAnchorBar = null;
        _buyPriority = _sellPriority = int.MaxValue;
        _buyLagS = _sellLagS = int.MaxValue;
        _buyF = _sellF = double.NaN;
    }

    /// <summary>
    /// Inject carry-over state from Pine bar-0 CSV export so C# starts the session
    /// with the same zone context that Pine has accumulated from its continuous history.
    ///
    /// Anchor bars are set to -1 (before bar 0) so barIndex &gt; anchorBar is always true
    /// from bar 0 onwards — matching Pine behaviour after a carry-over pivot.
    /// EffBreak values are not exported by Pine so they are left null (safe: zone just uses
    /// raw bot/top without expansion until a new break occurs).
    /// </summary>
    public void PreloadCarryOver(
        int      lastPushedSwingType,
        double?  realBuyTop,
        double?  realBuyBot,
        double?  realSellTop,
        double?  realSellBot)
    {
        LastPushedSwingType = lastPushedSwingType;
        RealBuyTop  = realBuyTop;
        RealBuyBot  = realBuyBot;
        RealSellTop = realSellTop;
        RealSellBot = realSellBot;
        EffBuyBreakBot  = null;
        EffSellBreakTop = null;
        // Anchor = -1 → barIndex > -1 is always true from bar 0 onwards
        BuyRealAnchorBar  = lastPushedSwingType == -1 ? -1 : (int?)null;
        SellRealAnchorBar = lastPushedSwingType ==  1 ? -1 : (int?)null;
        // Priority/F not known; use safe defaults so any real bar's zone can update
        _buyPriority  = int.MaxValue;
        _buyLagS      = int.MaxValue;
        _buyF         = double.NaN;
        _sellPriority = int.MaxValue;
        _sellLagS     = int.MaxValue;
        _sellF        = double.NaN;
    }

    /// <summary>Notify swing push — reset opposite-direction zone (Pine lines 4350-4382).</summary>
    public void OnSwingPushed(int swingType, int barIndex)
    {
        if (swingType == 1)
        {
            LastPushedSwingType = 1;
            SellRealAnchorBar = barIndex;
            RealSellTop = RealSellBot = null;
            _sellPriority = int.MaxValue;
            _sellLagS = int.MaxValue;
            _sellF = double.NaN;
            EffSellBreakTop = null;
            EffBuyBreakBot  = null;
        }
        else if (swingType == -1)
        {
            LastPushedSwingType = -1;
            BuyRealAnchorBar = barIndex;
            RealBuyTop = RealBuyBot = null;
            _buyPriority = int.MaxValue;
            _buyLagS = int.MaxValue;
            _buyF = double.NaN;
            EffSellBreakTop = null;
            EffBuyBreakBot  = null;
        }
    }

    /// <summary>
    /// Per-bar update — port Pine f_rz_buy + f_rz_sell called with kLag=0, sRootLag=1.
    /// Guard: barIndex > breakR3MaxK + 2 (matches Pine _rz_enoughBars).
    /// </summary>
    public void Tick(
        int    barIndex,
        bool   isM5,
        double tickSize,
        int    breakR3MaxK,
        Func<int, (double open, double high, double low, double close)> ohlcAtLag,
        Func<int, bool> hasClearBodyAt,
        Func<int, bool> isDojiAt)
    {
        const int kLag     = 0;  // K = current bar
        const int sRootLag = 1;  // S root = bar A (lag 1)

        // Pine guard: bar_index > breakR3MaxK + 2 (need lag up to breakR3MaxK)
        if (barIndex <= breakR3MaxK + 2) return;

        var (kOpen, kHigh, kLow, kClose) = ohlcAtLag(kLag);
        var bodyLoK = Math.Min(kOpen, kClose);
        var bodyHiK = Math.Max(kOpen, kClose);

        // ── BUY zone ─────────────────────────────────────────────────────────
        if (BuyRealAnchorBar.HasValue && barIndex > BuyRealAnchorBar.Value)
        {
            bool   found   = false;
            double bestF   = double.NaN, bestTop = double.NaN, bestBot = double.NaN;
            int    bestPri = int.MaxValue, bestLagS = int.MaxValue;

            // R1' (M5 only): S = bar[sRootLag], G = floor(min(S.close-t, bodyLoK-t)/t)*t
            if (isM5)
            {
                var (so, _, _, sc) = ohlcAtLag(sRootLag);
                var sb = sc - so;
                if (sc > so && sb > 0 && hasClearBodyAt(sRootLag) && !isDojiAt(sRootLag))
                {
                    var gv = Math.Floor(Math.Min(sc - tickSize, bodyLoK - tickSize) / tickSize) * tickSize;
                    TryUpdateBuy(kHigh, gv, tickSize, 1, sRootLag,
                        ref found, ref bestF, ref bestTop, ref bestBot, ref bestPri, ref bestLagS);
                }
            }

            // R1 (non-M5): S = bar[sRootLag]; G = floor(min(sLo+|sb|/3, bodyLoK-t)/t)*t
            if (!isM5)
            {
                var (so, _, _, sc) = ohlcAtLag(sRootLag);
                var sb = sc - so;
                if (sc > so && sb > 0 && hasClearBodyAt(sRootLag) && !isDojiAt(sRootLag))
                {
                    var sLo = Math.Min(so, sc);
                    var gv  = Math.Floor(Math.Min(sLo + Math.Abs(sb) / 3.0, bodyLoK - tickSize) / tickSize) * tickSize;
                    TryUpdateBuy(kHigh, gv, tickSize, 1, sRootLag,
                        ref found, ref bestF, ref bestTop, ref bestBot, ref bestPri, ref bestLagS);
                }
            }

            // R2 (non-M5): S=bar[sRootLag+1] anchor, C=bar[sRootLag] confirm
            //   G = floor(min(min(upT, C.bodyLo-t), bodyLoK-t)/t)*t  where G > loT
            if (!isM5)
            {
                var sL2 = sRootLag + 1;
                var (so, _, _, sc) = ohlcAtLag(sL2);
                var sb = sc - so;
                if (sc > so && sb > 0 && hasClearBodyAt(sL2) && !isDojiAt(sL2))
                {
                    var sLo  = Math.Min(so, sc);
                    var sLen = Math.Abs(sb);
                    var loT  = sLo + sLen / 3.0;
                    var upT  = sLo + sLen * 2.0 / 3.0;
                    var (co, _, _, cc) = ohlcAtLag(sRootLag);  // C = bar A
                    var bLC  = Math.Min(co, cc);
                    var gv   = Math.Floor(Math.Min(Math.Min(upT, bLC - tickSize), bodyLoK - tickSize) / tickSize) * tickSize;
                    if (!double.IsNaN(gv) && gv > loT)
                        TryUpdateBuy(kHigh, gv, tickSize, 2, sL2,
                            ref found, ref bestF, ref bestTop, ref bestBot, ref bestPri, ref bestLagS);
                }
            }

            // R3 (all TF): K must be bull-clear; scan S=bar[sRootLag..sRootLag+breakR3MaxK-1]
            //   cond: K.close > S.high; G = floor(min(S.close-t, minMid)/t)*t
            {
                bool   kBC    = kClose > kOpen && hasClearBodyAt(kLag) && !isDojiAt(kLag);
                double minMid = double.NaN;
                for (var ls = sRootLag; ls <= sRootLag + breakR3MaxK - 1; ls++)
                {
                    if (ls > sRootLag)
                    {
                        var (_, _, _, mc) = ohlcAtLag(ls - 1);
                        minMid = double.IsNaN(minMid) ? mc : Math.Min(minMid, mc);
                    }
                    var (so, sh, _, sc) = ohlcAtLag(ls);
                    var sb = sc - so;
                    if (sc > so && sb > 0 && hasClearBodyAt(ls) && !isDojiAt(ls) && kBC && kClose > sh)
                    {
                        var raw = sc - tickSize;
                        if (!double.IsNaN(minMid)) raw = Math.Min(raw, minMid);
                        var gv = Math.Floor(raw / tickSize) * tickSize;
                        TryUpdateBuy(kHigh, gv, tickSize, 3, ls,
                            ref found, ref bestF, ref bestTop, ref bestBot, ref bestPri, ref bestLagS);
                    }
                }
            }

            // Apply if this bar's best candidate beats stored zone
            if (found)
            {
                bool take = RealBuyTop is null
                    || bestTop > RealBuyTop
                    || (bestTop == RealBuyTop && bestF > _buyF)
                    || (bestTop == RealBuyTop && bestF == _buyF && bestPri < _buyPriority)
                    || (bestTop == RealBuyTop && bestF == _buyF && bestPri == _buyPriority && bestLagS < _buyLagS);
                if (take)
                {
                    RealBuyTop    = bestTop;
                    RealBuyBot    = bestBot;
                    _buyF         = bestF;
                    _buyPriority  = bestPri;
                    _buyLagS      = bestLagS;
                }
            }
        }

        // ── SELL zone ────────────────────────────────────────────────────────
        if (SellRealAnchorBar.HasValue && barIndex > SellRealAnchorBar.Value)
        {
            bool   found   = false;
            double bestF   = double.NaN, bestTop = double.NaN, bestBot = double.NaN;
            int    bestPri = int.MaxValue, bestLagS = int.MaxValue;

            // R1' (M5 only): Gp = ceil(max(S.close+t, bodyHiK+t)/t)*t; Fp = Gp-t
            if (isM5)
            {
                var (so, _, _, sc) = ohlcAtLag(sRootLag);
                var sb = so - sc;
                if (sc < so && sb > 0 && hasClearBodyAt(sRootLag) && !isDojiAt(sRootLag))
                {
                    var rawG = Math.Max(sc + tickSize, bodyHiK + tickSize);
                    var gpv  = Math.Ceiling(rawG / tickSize) * tickSize;
                    var fpv  = gpv - tickSize;
                    TryUpdateSell(kLow, fpv, 1, sRootLag,
                        ref found, ref bestF, ref bestTop, ref bestBot, ref bestPri, ref bestLagS);
                }
            }

            // R1 (non-M5): Gp = ceil(max(sHi-|sb|/3, bodyHiK+t)/t)*t; Fp = Gp-t
            if (!isM5)
            {
                var (so, _, _, sc) = ohlcAtLag(sRootLag);
                var sb = so - sc;
                if (sc < so && sb > 0 && hasClearBodyAt(sRootLag) && !isDojiAt(sRootLag))
                {
                    var sHi  = Math.Max(so, sc);
                    var sLen = Math.Abs(sb);
                    var rawG = Math.Max(sHi - sLen / 3.0, bodyHiK + tickSize);
                    var gpv  = Math.Ceiling(rawG / tickSize) * tickSize;
                    var fpv  = gpv - tickSize;
                    TryUpdateSell(kLow, fpv, 1, sRootLag,
                        ref found, ref bestF, ref bestTop, ref bestBot, ref bestPri, ref bestLagS);
                }
            }

            // R2 (non-M5): S=bar[sRootLag+1] anchor, C=bar[sRootLag] confirm
            //   Gp = ceil(max(max(loT, C.bodyHi+t), bodyHiK+t)/t)*t  where Gp < upT
            if (!isM5)
            {
                var sL2 = sRootLag + 1;
                var (so, _, _, sc) = ohlcAtLag(sL2);
                var sb = so - sc;
                if (sc < so && sb > 0 && hasClearBodyAt(sL2) && !isDojiAt(sL2))
                {
                    var sLo  = Math.Min(so, sc);
                    var sHi  = Math.Max(so, sc);
                    var sLen = Math.Abs(sb);
                    var loT  = sLo + sLen / 3.0;
                    var upT  = sHi - sLen / 3.0;
                    var (co, _, _, cc) = ohlcAtLag(sRootLag);  // C = bar A
                    var bHC  = Math.Max(co, cc);
                    var rawG = Math.Max(Math.Max(loT, bHC + tickSize), bodyHiK + tickSize);
                    var gpv  = Math.Ceiling(rawG / tickSize) * tickSize;
                    if (!double.IsNaN(gpv) && gpv < upT)
                    {
                        var fpv = gpv - tickSize;
                        TryUpdateSell(kLow, fpv, 2, sL2,
                            ref found, ref bestF, ref bestTop, ref bestBot, ref bestPri, ref bestLagS);
                    }
                }
            }

            // R3 (all TF): K must be bear-clear; scan S=bar[sRootLag..sRootLag+breakR3MaxK-1]
            //   cond: K.close < S.low; Gp = ceil(max(S.close+t, maxMid)/t)*t; Fp = Gp-t
            {
                bool   kBC    = kClose < kOpen && hasClearBodyAt(kLag) && !isDojiAt(kLag);
                double maxMid = double.NaN;
                for (var ls = sRootLag; ls <= sRootLag + breakR3MaxK - 1; ls++)
                {
                    if (ls > sRootLag)
                    {
                        var (_, _, _, mc) = ohlcAtLag(ls - 1);
                        maxMid = double.IsNaN(maxMid) ? mc : Math.Max(maxMid, mc);
                    }
                    var (so, _, sl, sc) = ohlcAtLag(ls);
                    var sb = so - sc;
                    if (sc < so && sb > 0 && hasClearBodyAt(ls) && !isDojiAt(ls) && kBC && kClose < sl)
                    {
                        var rawG = sc + tickSize;
                        if (!double.IsNaN(maxMid)) rawG = Math.Max(rawG, maxMid);
                        var gpv  = Math.Ceiling(rawG / tickSize) * tickSize;
                        var fpv  = gpv - tickSize;
                        TryUpdateSell(kLow, fpv, 3, ls,
                            ref found, ref bestF, ref bestTop, ref bestBot, ref bestPri, ref bestLagS);
                    }
                }
            }

            // Apply if this bar's best candidate beats stored zone
            if (found)
            {
                bool take = RealSellBot is null
                    || bestBot < RealSellBot
                    || (bestBot == RealSellBot && bestF < _sellF)
                    || (bestBot == RealSellBot && bestF == _sellF && bestPri < _sellPriority)
                    || (bestBot == RealSellBot && bestF == _sellF && bestPri == _sellPriority && bestLagS < _sellLagS);
                if (take)
                {
                    RealSellTop   = bestTop;
                    RealSellBot   = bestBot;
                    _sellF        = bestF;
                    _sellPriority = bestPri;
                    _sellLagS     = bestLagS;
                }
            }
        }
    }

    /// <summary>Update effBreak khi có pivot opposite-side break (Pine cascade ~4664-4680).</summary>
    public void OnPivotBroken(int pivotType, double brokenClose)
    {
        if (pivotType == -1)
        {
            EffBuyBreakBot = EffBuyBreakBot.HasValue
                ? Math.Min(EffBuyBreakBot.Value, brokenClose)
                : brokenClose;
        }
        else if (pivotType == 1)
        {
            EffSellBreakTop = EffSellBreakTop.HasValue
                ? Math.Max(EffSellBreakTop.Value, brokenClose)
                : brokenClose;
        }
    }

    /// <summary>
    /// Pine <c>pine code alert debug.pine</c> lines 5277-5278 / 5285-5286:
    /// <code>
    ///   if lastPushedSwingType == -1 and not na(realBuyBot) and low[0] &lt; realBuyBot
    ///       effBuyBreakBot := math.min(nz(effBuyBreakBot, low[0]), low[0])
    ///   if lastPushedSwingType == 1 and not na(realSellTop) and high[0] &gt; realSellTop
    ///       effSellBreakTop := math.max(nz(effSellBreakTop, high[0]), high[0])
    /// </code>
    /// Mỗi bar realtime nếu high/low của bar hiện tại vượt khỏi realzone → mở rộng eff*.
    /// </summary>
    public void OnRealtimeBarPrice(double barHigh, double barLow)
    {
        if (LastPushedSwingType == -1 && RealBuyBot.HasValue && barLow < RealBuyBot.Value)
        {
            EffBuyBreakBot = EffBuyBreakBot.HasValue
                ? Math.Min(EffBuyBreakBot.Value, barLow)
                : barLow;
        }
        else if (LastPushedSwingType == 1 && RealSellTop.HasValue && barHigh > RealSellTop.Value)
        {
            EffSellBreakTop = EffSellBreakTop.HasValue
                ? Math.Max(EffSellBreakTop.Value, barHigh)
                : barHigh;
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>BUY: compute zone from G; update best if candidate beats current best (prefer higher top).</summary>
    static void TryUpdateBuy(
        double kHigh, double gv, double tickSize, int pri, int lagS,
        ref bool found, ref double bestF, ref double bestTop, ref double bestBot,
        ref int bestPri, ref int bestLagS)
    {
        if (double.IsNaN(gv)) return;
        var fv  = gv + tickSize;
        var top = Math.Max(kHigh, fv);
        var bot = Math.Min(kHigh, fv);
        var better = !found
            || top > bestTop
            || (top == bestTop && fv > bestF)
            || (top == bestTop && fv == bestF && pri < bestPri)
            || (top == bestTop && fv == bestF && pri == bestPri && lagS < bestLagS);
        if (!better) return;
        found = true;
        bestF = fv; bestTop = top; bestBot = bot; bestPri = pri; bestLagS = lagS;
    }

    /// <summary>SELL: compute zone from Fp; update best if candidate beats current best (prefer lower bot).</summary>
    static void TryUpdateSell(
        double kLow, double fpv, int pri, int lagS,
        ref bool found, ref double bestF, ref double bestTop, ref double bestBot,
        ref int bestPri, ref int bestLagS)
    {
        var top = Math.Max(kLow, fpv);
        var bot = Math.Min(kLow, fpv);
        var better = !found
            || bot < bestBot
            || (bot == bestBot && fpv < bestF)
            || (bot == bestBot && fpv == bestF && pri < bestPri)
            || (bot == bestBot && fpv == bestF && pri == bestPri && lagS < bestLagS);
        if (!better) return;
        found = true;
        bestF = fpv; bestTop = top; bestBot = bot; bestPri = pri; bestLagS = lagS;
    }
}
