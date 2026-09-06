using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>
/// Thin wrapper over the cBot trading API. Only ever touches orders whose label starts with the
/// configured prefix and that belong to the chart symbol, so it never disturbs other robots' trades.
/// </summary>
public sealed class TradeExecutionService
{
    readonly cAlgo.API.Robot _robot;
    readonly string _symbolName;
    readonly string _labelPrefix;
    readonly Action<string> _log;

    public TradeExecutionService(cAlgo.API.Robot robot, string symbolName, string labelPrefix, Action<string> log)
    {
        _robot = robot;
        _symbolName = symbolName;
        _labelPrefix = labelPrefix;
        _log = log;
    }

    bool IsOurs(string? label) =>
        !string.IsNullOrEmpty(label) && label!.StartsWith(_labelPrefix, StringComparison.Ordinal);

    public IEnumerable<Position> OurPositions() =>
        _robot.Positions.Where(p => p.SymbolName == _symbolName && IsOurs(p.Label));

    public IEnumerable<PendingOrder> OurPendingOrders() =>
        _robot.PendingOrders.Where(o => o.SymbolName == _symbolName && IsOurs(o.Label));

    /// <summary>All labels currently live as either a position or a pending order.</summary>
    public ISet<string> LiveLabels()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in OurPositions())
            if (p.Label != null) set.Add(p.Label);
        foreach (var o in OurPendingOrders())
            if (o.Label != null) set.Add(o.Label);
        return set;
    }

    public bool HasAnyLive() => OurPositions().Any() || OurPendingOrders().Any();

    public PlaceOrderResult PlaceLimit(TradePlan plan, double volumeUnits)
    {
        var tradeType = plan.IsBuy ? TradeType.Buy : TradeType.Sell;
        var slPips = Math.Round(plan.StopLossPips, 1);
        var tpPips = Math.Round(plan.TakeProfitPips, 1);

        // The pips-based overload is the long-standing Strategy-Tester-friendly API; the soft
        // deprecation only swaps pip→absolute semantics in a newer overload. Pips are correct here.
#pragma warning disable CS0618
        var result = _robot.PlaceLimitOrder(
            tradeType,
            _symbolName,
            volumeUnits,
            plan.EntryLimit,
            plan.Label,
            slPips,
            tpPips);
#pragma warning restore CS0618

        if (!result.IsSuccessful)
        {
            _log($"[Exec] PlaceLimit FAILED {plan.Label}: {result.Error}");
            return PlaceOrderResult.Failed(result.Error.ToString());
        }

        var rr = slPips > 0 ? Math.Round(tpPips / slPips, 2) : 0;
        _log($"[Exec] LIMIT {plan.Label} {(plan.IsBuy ? "BUY" : "SELL")} vol={volumeUnits} @ {plan.EntryLimit:0.#####} SL={slPips}p TP={tpPips}p RR={rr}");
        return PlaceOrderResult.Ok(result.PendingOrder);
    }

    /// <summary>
    /// Place a limit order with NO take-profit (TP will be set later when Swing-2 confirms).
    /// Uses <c>null</c> for the TP pips parameter (Strategy Tester + live support confirmed in cTrader 4).
    /// </summary>
    public PlaceOrderResult PlaceLimitNoTp(TradePlan plan, double volumeUnits)
    {
        var tradeType = plan.IsBuy ? TradeType.Buy : TradeType.Sell;
        var slPips = Math.Round(plan.StopLossPips, 1);

#pragma warning disable CS0618
        var result = _robot.PlaceLimitOrder(
            tradeType,
            _symbolName,
            volumeUnits,
            plan.EntryLimit,
            plan.Label,
            slPips,
            null);   // no TP — will be set via ModifyTakeProfitPrice once Swing 2 confirms
#pragma warning restore CS0618

        if (!result.IsSuccessful)
        {
            _log($"[Exec] PlaceLimitNoTp FAILED {plan.Label}: {result.Error}");
            return PlaceOrderResult.Failed(result.Error.ToString());
        }

        _log($"[Exec] LIMIT-NOTP {plan.Label} {(plan.IsBuy ? "BUY" : "SELL")} vol={volumeUnits} @ {plan.EntryLimit:0.#####} SL={slPips}p TP=none (awaiting C)");
        return PlaceOrderResult.Ok(result.PendingOrder);
    }

    /// <summary>Market entry at current bid/ask; SL/TP pips from fill price to plan absolute levels.</summary>
    public PlaceOrderResult PlaceMarket(TradePlan plan, double volumeUnits, double fillPrice, double pipSize)
    {
        var tradeType = plan.IsBuy ? TradeType.Buy : TradeType.Sell;
        var slPips = MarketFallbackGate.SlPipsFromEntry(fillPrice, plan.StopLoss, pipSize);
        var tpPips = MarketFallbackGate.TpPipsFromEntry(fillPrice, plan.TakeProfit, pipSize);

#pragma warning disable CS0618
        var result = _robot.ExecuteMarketOrder(
            tradeType,
            _symbolName,
            volumeUnits,
            plan.Label,
            slPips,
            tpPips);
#pragma warning restore CS0618

        if (!result.IsSuccessful)
        {
            _log($"[Exec] PlaceMarket FAILED {plan.Label}: {result.Error}");
            return PlaceOrderResult.Failed(result.Error.ToString());
        }

        var rr = slPips > 0 ? Math.Round(tpPips / slPips, 2) : 0;
        _log($"[Exec] MARKET {plan.Label} {(plan.IsBuy ? "BUY" : "SELL")} vol={volumeUnits} @ market SL={slPips}p TP={tpPips}p RR={rr}");
        return PlaceOrderResult.OkPosition(result.Position);
    }

    /// <summary>Market entry with SL only — TP set later when swing-C confirms.</summary>
    public PlaceOrderResult PlaceMarketNoTp(TradePlan plan, double volumeUnits, double fillPrice, double pipSize)
    {
        var tradeType = plan.IsBuy ? TradeType.Buy : TradeType.Sell;
        var slPips = MarketFallbackGate.SlPipsFromEntry(fillPrice, plan.StopLoss, pipSize);

#pragma warning disable CS0618
        var result = _robot.ExecuteMarketOrder(
            tradeType,
            _symbolName,
            volumeUnits,
            plan.Label,
            slPips,
            null);
#pragma warning restore CS0618

        if (!result.IsSuccessful)
        {
            _log($"[Exec] PlaceMarketNoTp FAILED {plan.Label}: {result.Error}");
            return PlaceOrderResult.Failed(result.Error.ToString());
        }

        _log($"[Exec] MARKET-NOTP {plan.Label} {(plan.IsBuy ? "BUY" : "SELL")} vol={volumeUnits} @ market SL={slPips}p TP=none (awaiting C)");
        return PlaceOrderResult.OkPosition(result.Position);
    }

    public void CloseAndCancelByLabel(string label, string why)
    {
        foreach (var p in OurPositions().Where(p => p.Label == label).ToArray())
        {
            var r = _robot.ClosePosition(p);
            _log($"[Exec] CLOSE {label} ({why}) -> {(r.IsSuccessful ? "ok" : r.Error.ToString())}");
        }
        CancelPendingByLabel(label, why);
    }

    /// <summary>Cancel pending order only — does not close filled positions.</summary>
    public bool CancelPendingByLabel(string label, string why)
    {
        var cancelled = false;
        foreach (var o in OurPendingOrders().Where(o => o.Label == label).ToArray())
        {
            var r = _robot.CancelPendingOrder(o);
            _log($"[Exec] CANCEL {label} ({why}) -> {(r.IsSuccessful ? "ok" : r.Error.ToString())}");
            cancelled = r.IsSuccessful || cancelled;
        }
        return cancelled;
    }

    /// <summary>Close positions + cancel pendings of the opposite direction (flip support).</summary>
    public void CloseAndCancelOpposite(bool newIsBuy, string why)
    {
        var oppType = newIsBuy ? TradeType.Sell : TradeType.Buy;
        foreach (var p in OurPositions().Where(p => p.TradeType == oppType).ToArray())
        {
            var r = _robot.ClosePosition(p);
            _log($"[Exec] FLIP-CLOSE {p.Label} ({why}) -> {(r.IsSuccessful ? "ok" : r.Error.ToString())}");
        }
        foreach (var o in OurPendingOrders().Where(o => o.TradeType == oppType).ToArray())
        {
            var r = _robot.CancelPendingOrder(o);
            _log($"[Exec] FLIP-CANCEL {o.Label} ({why}) -> {(r.IsSuccessful ? "ok" : r.Error.ToString())}");
        }
    }

    public void CloseAndCancelAll(string why)
    {
        foreach (var p in OurPositions().ToArray())
        {
            var r = _robot.ClosePosition(p);
            _log($"[Exec] CLOSE-ALL {p.Label} ({why}) -> {(r.IsSuccessful ? "ok" : r.Error.ToString())}");
        }
        foreach (var o in OurPendingOrders().ToArray())
        {
            var r = _robot.CancelPendingOrder(o);
            _log($"[Exec] CANCEL-ALL {o.Label} ({why}) -> {(r.IsSuccessful ? "ok" : r.Error.ToString())}");
        }
    }

    /// <summary>
    /// Close all bot positions and cancel all bot pending orders scoped to <paramref name="symbolName"/>.
    /// Because this service is already scoped to a single symbol + label prefix, this is equivalent to
    /// <see cref="CloseAndCancelAll"/> but makes the symbol guard explicit for news-force-close callers.
    /// Manual trades and other robots' trades are never touched.
    /// </summary>
    public (int PositionsClosed, int PendingsCancelled) CloseAndCancelForSymbol(string symbolName, string why)
    {
        if (!string.Equals(symbolName, _symbolName, StringComparison.OrdinalIgnoreCase))
        {
            _log($"[Exec] CloseAndCancelForSymbol SKIP: requested '{symbolName}' but service is scoped to '{_symbolName}'");
            return (0, 0);
        }
        var positionsClosed  = 0;
        var pendingsCancelled = 0;
        foreach (var p in OurPositions().ToArray())
        {
            var r = _robot.ClosePosition(p);
            _log($"[Exec] NEWS-CLOSE {p.Label} ({why}) -> {(r.IsSuccessful ? "ok" : r.Error.ToString())}");
            if (r.IsSuccessful) positionsClosed++;
        }
        foreach (var o in OurPendingOrders().ToArray())
        {
            var r = _robot.CancelPendingOrder(o);
            _log($"[Exec] NEWS-CANCEL {o.Label} ({why}) -> {(r.IsSuccessful ? "ok" : r.Error.ToString())}");
            if (r.IsSuccessful) pendingsCancelled++;
        }
        return (positionsClosed, pendingsCancelled);
    }

    public bool HasOppositeLive(bool newIsBuy)
    {
        var oppType = newIsBuy ? TradeType.Sell : TradeType.Buy;
        return OurPositions().Any(p => p.TradeType == oppType)
            || OurPendingOrders().Any(o => o.TradeType == oppType);
    }

    public Position? TryGetPositionByLabel(string label) =>
        OurPositions().FirstOrDefault(p => p.Label == label);

    public bool HasPendingByLabel(string label) =>
        OurPendingOrders().Any(o => o.Label == label);

    public sealed class ModifyTpResult
    {
        public bool Success { get; init; }
        public string? Error { get; init; }
    }

    /// <summary>Modify take-profit on an open position, keeping the existing stop-loss.</summary>
    public ModifyTpResult TryModifyTakeProfit(Position position, double newTakeProfit)
    {
        var sl = position.StopLoss;
#pragma warning disable CS0618
        var result = _robot.ModifyPosition(position, sl, newTakeProfit);
#pragma warning restore CS0618
        if (!result.IsSuccessful)
            return new ModifyTpResult { Success = false, Error = result.Error.ToString() };

        _log($"[Exec] MODIFY-TP {position.Label} tp={newTakeProfit:0.#####} -> ok");
        return new ModifyTpResult { Success = true };
    }

    /// <summary>Modify both stop-loss and take-profit to absolute prices on an open position.</summary>
    public ModifyTpResult TryModifyProtection(Position position, double? newStopLoss, double? newTakeProfit)
    {
#pragma warning disable CS0618
        var result = _robot.ModifyPosition(position, newStopLoss, newTakeProfit);
#pragma warning restore CS0618
        if (!result.IsSuccessful)
            return new ModifyTpResult { Success = false, Error = result.Error.ToString() };

        _log($"[Exec] MODIFY-PROTECTION {position.Label} sl={newStopLoss:0.#####} tp={newTakeProfit:0.#####} -> ok");
        return new ModifyTpResult { Success = true };
    }

    /// <summary>Modify take-profit on a still-pending limit order, keeping its target/stop.</summary>
    public ModifyTpResult TryModifyPendingTakeProfit(PendingOrder order, double newTakeProfit)
    {
        var result = order.ModifyTakeProfitPrice(newTakeProfit);
        if (!result.IsSuccessful)
            return new ModifyTpResult { Success = false, Error = result.Error.ToString() };

        _log($"[Exec] MODIFY-TP-PENDING {order.Label} tp={newTakeProfit:0.#####} -> ok");
        return new ModifyTpResult { Success = true };
    }

    public PendingOrder? TryGetPendingByLabel(string label) =>
        OurPendingOrders().FirstOrDefault(o => o.Label == label);
}
