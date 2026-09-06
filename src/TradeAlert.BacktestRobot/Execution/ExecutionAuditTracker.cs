using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using cAlgo.API;
using TradeAlert.BacktestRobot.Execution.Analytics;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>Tracks planned vs broker RR/TP/PnL and aggregates close statistics for OnStop summary.</summary>
public sealed class ExecutionAuditTracker
{
    public const double RrMismatchThreshold = 0.05;

    readonly Dictionary<string, PlannedAuditSnapshot> _plannedByLabel = new(StringComparer.Ordinal);
    readonly Dictionary<string, PositionProtectionSnapshot> _protectionByLabel = new(StringComparer.Ordinal);
    readonly HashSet<string> _orderAudited = new(StringComparer.Ordinal);
    readonly HashSet<string> _positionAudited = new(StringComparer.Ordinal);
    readonly HashSet<string> _pendingRemoved = new(StringComparer.Ordinal);
    readonly HashSet<string> _closeAudited = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> _pendingCloseReason = new(StringComparer.Ordinal);

    public const double ProtectionMismatchPipsThreshold = 1.0;

    int _rrMismatchCount;
    int _tpProfitTooLargeCount;
    int _closeAuditMissingCount;
    int _tpOvershootCount;
    int _protectionTpMismatchCount;
    int _protectionSlMismatchCount;
    int _protectionSyncOkCount;
    int _protectionSyncRejectCount;

    readonly HashSet<string> _protectionAudited = new(StringComparer.Ordinal);

    public int CloseAuditCount => _closeAudited.Count;
    public int CloseAuditMissingCount => _closeAuditMissingCount;

    public bool TryGetPlanned(string label, out PlannedAuditSnapshot planned)
    {
        if (label != null && _plannedByLabel.TryGetValue(label, out var p))
        {
            planned = p;
            return true;
        }

        planned = null!;
        return false;
    }
    double _tpOvershootPipsSum;
    double _maxTpOvershootPips;

    double _rawNetSum;
    double _clampedNetSum;
    readonly List<double> _rawWinNets = new();
    readonly List<double> _clampedWinNets = new();
    readonly List<double> _rawLossNets = new();
    readonly List<double> _clampedLossNets = new();
    readonly List<double> _winnerRNet = new();
    readonly List<double> _loserRNet = new();

    readonly Dictionary<string, CloseBucket> _buckets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TakeProfit"] = new(),
        ["StopLoss"] = new(),
        ["BBrokenTpEntry"] = new(),
        ["XRBarClose"] = new(),
        ["XRBarCloseNextOpen"] = new(),
        ["XRBarCloseActual"] = new(),
        ["SessionStop"] = new(),
        ["BBroken"] = new(),
        ["ManualClose"] = new(),
        ["Unknown"] = new(),
    };

    double _pipSize = 0.01;
    ProtectionAnchorMode _anchorMode = ProtectionAnchorMode.ActualFillRelative;
    InternalPerformanceConfig _internalConfig = InternalPerformanceConfig.Default;

    int _unknownCloseCount;
    double _internalNetSum;
    double _internalGrossRSum;
    double _internalNetRSum;
    double _totalCommission;
    double _commissionR;
    double _rawVsInternalNetDiffSum;
    readonly List<double> _internalWinNets = new();
    readonly List<double> _internalLossNets = new();
    readonly List<double> _internalWinR = new();
    readonly List<double> _internalLossR = new();
    readonly InternalStrategyMetricsEngine _strategyMetrics = new();
    InternalStrategyEvalContext _strategyEvalContext = new();
    VirtualXrConfig _virtualXrCfg = new();
    readonly Dictionary<string, VirtualXrPendingClose> _pendingVirtualXrClose = new(StringComparer.Ordinal);
    DateTime? _lastVirtualXrProcessedBarTime;
    int _xrTouchNoConfirmCount;
    int _xrConfirmedCount;

    public void SetPipSize(double pipSize) => _pipSize = pipSize > 0 ? pipSize : 0.01;

    public void SetVirtualXrConfig(in VirtualXrConfig config) => _virtualXrCfg = config;

    public VirtualXrConfig VirtualXrConfig => _virtualXrCfg;

    public int XrTouchNoConfirmCount => _xrTouchNoConfirmCount;

    public int XrConfirmedCount => _xrConfirmedCount;

    public bool ShouldProcessVirtualXrBar(DateTime barOpenTime)
    {
        if (_lastVirtualXrProcessedBarTime.HasValue
            && _lastVirtualXrProcessedBarTime.Value == barOpenTime)
            return false;

        _lastVirtualXrProcessedBarTime = barOpenTime;
        return true;
    }

    public bool IsVirtualXrActive(string label) =>
        _protectionByLabel.TryGetValue(label, out var p) && p.VirtualXrExitEnabled && !p.VirtualXrExitTriggered;

    public bool HasBBrokenMoveTpToEntry(string label) =>
        _protectionByLabel.TryGetValue(label, out var p) && p.BBrokenMoveTpToEntryApplied;

    public bool TryGetProtection(string label, out PositionProtectionSnapshot protection) =>
        _protectionByLabel.TryGetValue(label, out protection!);

    public void SetupVirtualXrForPosition(
        string label,
        double entry,
        double stopLoss,
        bool isBuy,
        in VirtualXrConfig cfg,
        Action<string> print)
    {
        if (string.IsNullOrEmpty(label) || !cfg.Enabled)
            return;

        if (!_protectionByLabel.TryGetValue(label, out var protection))
        {
            if (!_plannedByLabel.TryGetValue(label, out var planned))
                return;
            protection = ExecutionPnLCalculator.FromPlanned(planned, _anchorMode);
        }

        var sl = stopLoss > 0 ? stopLoss : protection.StopLoss;
        var riskDistance = Math.Abs(entry - sl);
        var virtualXrPrice = VirtualXrBarCloseEvaluator.ComputeVirtualXrPrice(entry, sl, cfg.TriggerR, isBuy);

        _protectionByLabel[label] = protection.WithVirtualXrPlan(
            enabled: true,
            brokerTpEnabled: cfg.UseBrokerTp,
            triggerR: cfg.TriggerR,
            virtualXrPrice: virtualXrPrice,
            fillMode: cfg.FillMode);

        print(VirtualXrExitLog.FormatExitPlan(
            label, cfg.Enabled, cfg.TriggerR, entry, sl, riskDistance, virtualXrPrice, cfg.FillMode, cfg.UseBrokerTp));
    }

    public void RecordVirtualXrTouchNoConfirm(string label, in VirtualXrBarOhlc bar)
    {
        _xrTouchNoConfirmCount++;
        if (_protectionByLabel.TryGetValue(label, out var protection))
            _protectionByLabel[label] = protection.WithVirtualXrTouched(true);
    }

    public void RecordVirtualXrConfirmed() => _xrConfirmedCount++;

    public bool HasPendingVirtualXrClose(string label) =>
        _pendingVirtualXrClose.ContainsKey(label);

    public void EnqueueVirtualXrClose(
        string label,
        string reason,
        in VirtualXrBarOhlc signalBar,
        double nextBarOpen)
    {
        _pendingVirtualXrClose[label] = new VirtualXrPendingClose
        {
            Label = label,
            Reason = reason,
            SignalBar = signalBar,
            NextBarOpen = nextBarOpen,
        };
    }

    public IReadOnlyList<VirtualXrPendingClose> DrainPendingVirtualXrCloses()
    {
        if (_pendingVirtualXrClose.Count == 0)
            return Array.Empty<VirtualXrPendingClose>();

        var list = _pendingVirtualXrClose.Values.ToList();
        _pendingVirtualXrClose.Clear();
        return list;
    }

    public void PrepareVirtualXrClose(
        string label,
        string reason,
        double effectiveClose,
        DateTime signalBarTime,
        double signalBarClose,
        bool touched)
    {
        if (_protectionByLabel.TryGetValue(label, out var protection))
        {
            _protectionByLabel[label] = protection.WithVirtualXrExit(
                effectiveClose, signalBarTime, signalBarClose, touched);
        }

        PrepareClose(label, reason);
    }

    public void RecordVirtualXrTouchNoConfirmLog(string label, in VirtualXrBarOhlc bar, Action<string> print)
    {
        if (!_protectionByLabel.TryGetValue(label, out var protection))
            return;

        RecordVirtualXrTouchNoConfirm(label, in bar);
        print(VirtualXrExitLog.FormatTouchNoConfirm(
            label, protection.VirtualXrTriggerR, protection.VirtualXrPrice, in bar));
    }

    public void SetStrategyEvalContext(in InternalStrategyEvalContext context)
    {
        _strategyEvalContext = context;
        _strategyMetrics.SetContext(in context);
    }

    public void SetProtectionAnchorMode(ProtectionAnchorMode mode) => _anchorMode = mode;

    public void SetInternalPerformanceConfig(in InternalPerformanceConfig config) => _internalConfig = config;

    public void RecordPlanned(
        in TradePlan plan,
        double cfgRr,
        bool expectTpOnPlace,
        double plannedRiskUsd,
        double volumeUnits)
    {
        var plannedRr = TradePlanRrLog.AppliedRr(plan.StopLossPips, plan.TakeProfitPips);
        var planned = new PlannedAuditSnapshot
        {
            Label = plan.Label,
            IsBuy = plan.IsBuy,
            PlannedEntry = plan.EntryLimit,
            PlannedSl = plan.StopLoss,
            PlannedTp = plan.TakeProfit,
            PlannedSlPips = plan.StopLossPips,
            PlannedTpPips = plan.TakeProfitPips,
            PlannedRr = plannedRr,
            CfgRr = cfgRr,
            PlannedRiskUsd = plannedRiskUsd,
            VolumeUnits = volumeUnits,
            ExpectTpOnPlace = expectTpOnPlace,
            SpreadPipsAtPlan = plan.SpreadPipsAtPlan,
            SpreadSourceAtPlan = plan.SpreadSourceAtPlan,
        };
        _plannedByLabel[plan.Label] = planned;
        _protectionByLabel[plan.Label] = ExecutionPnLCalculator.FromPlanned(planned, _anchorMode);
    }

    public void SyncProtectionFromPosition(Position position)
    {
        if (position.Label is null)
            return;

        _plannedByLabel.TryGetValue(position.Label, out var planned);
        _protectionByLabel.TryGetValue(position.Label, out var existing);
        var snapshot = ExecutionPnLCalculator.FromPosition(position, planned, _anchorMode);
        if (existing?.BBrokenMoveTpToEntryApplied == true)
            snapshot = snapshot.PreserveExitMetadata(existing);
        _protectionByLabel[position.Label] = snapshot;
    }

    public void SyncProtectionFromPending(PendingOrder order)
    {
        if (order.Label is null || !_plannedByLabel.TryGetValue(order.Label, out var planned))
            return;

        _protectionByLabel[order.Label] = ExecutionPnLCalculator.FromPending(order, planned, _anchorMode);
    }

    /// <summary>
    /// Audit fill-relative protection: compare broker SL/TP against the SL/TP expected from the
    /// actual fill price and planned pip distances (mode ActualFillRelative). Runs once per label.
    /// </summary>
    public void AuditProtectionActualFill(Position position, double pipSize, Action<string> print)
    {
        if (position.Label is null || _protectionAudited.Contains(position.Label))
            return;
        if (!_plannedByLabel.TryGetValue(position.Label, out var planned))
            return;

        _protectionAudited.Add(position.Label);

        var isBuy = position.TradeType == TradeType.Buy;
        var (expectedSl, expectedTp) = ExecutionPnLCalculator.ExpectedActualProtection(
            position.EntryPrice, planned.PlannedSlPips, planned.PlannedTpPips, pipSize, isBuy);

        var positionTp = position.TakeProfit ?? 0;
        var positionSl = position.StopLoss ?? 0;
        var tpMismatchPips = positionTp > 0 ? Math.Abs(positionTp - expectedTp) / pipSize : 0;
        var slMismatchPips = positionSl > 0 ? Math.Abs(positionSl - expectedSl) / pipSize : 0;

        print(TradePlanRrLog.FormatProtectionAudit(
            position.Label, planned.PlannedEntry, position.EntryPrice,
            expectedTp, positionTp, expectedSl, positionSl, tpMismatchPips, slMismatchPips));

        if (positionTp > 0 && tpMismatchPips > ProtectionMismatchPipsThreshold)
            _protectionTpMismatchCount++;
        if (positionSl > 0 && slMismatchPips > ProtectionMismatchPipsThreshold)
            _protectionSlMismatchCount++;
    }

    public void MarkProtectionAudited(string label)
    {
        if (!string.IsNullOrEmpty(label))
            _protectionAudited.Add(label);
    }

    public void RecordProtectionSync(bool success)
    {
        if (success)
            _protectionSyncOkCount++;
        else
            _protectionSyncRejectCount++;
    }

    /// <summary>
    /// Capture final protection from a (possibly just-closed) position. Works even when no plan
    /// was recorded for the label, so plan-missing closes can still be clamped against broker SL/TP.
    /// </summary>
    public void SyncProtectionFromClosedPosition(Position position)
    {
        if (position.Label is null)
            return;

        _plannedByLabel.TryGetValue(position.Label, out var planned);
        _protectionByLabel.TryGetValue(position.Label, out var existing);
        var snapshot = ExecutionPnLCalculator.FromPosition(position, planned, _anchorMode);
        if (existing?.BBrokenMoveTpToEntryApplied == true)
            snapshot = snapshot.PreserveExitMetadata(existing);
        _protectionByLabel[position.Label] = snapshot;
    }

    public void RecordBBrokenMoveTpToEntry(
        string label,
        double newTakeProfit,
        double originalTp,
        double stopLossAtMove,
        DateTime moveTime)
    {
        if (string.IsNullOrEmpty(label))
            return;

        if (!_protectionByLabel.TryGetValue(label, out var protection))
        {
            if (!_plannedByLabel.TryGetValue(label, out var planned))
                return;
            protection = ExecutionPnLCalculator.FromPlanned(planned, _anchorMode);
        }

        _protectionByLabel[label] = protection.WithBBrokenMoveTpToEntry(
            newTakeProfit, originalTp, stopLossAtMove, moveTime);
    }

    public void UpdateProtectionTakeProfit(string label, double newTakeProfit)
    {
        if (!_protectionByLabel.TryGetValue(label, out var protection))
            return;

        _protectionByLabel[label] = protection.WithTakeProfit(newTakeProfit);
    }

    public void RecordPendingRemoved(string label)
    {
        if (!string.IsNullOrEmpty(label))
            _pendingRemoved.Add(label);
    }

    public void PrepareClose(string label, string reason)
    {
        if (string.IsNullOrEmpty(label))
            return;
        _pendingCloseReason[label] = reason;
    }

    public IReadOnlyList<string> FormatPlanAbs(in TradePlan plan, double cfgRr, double plannedRiskUsd, double volumeUnits) =>
        TradePlanRrLog.FormatPlanAbs(in plan, cfgRr, plannedRiskUsd, volumeUnits);

    public IReadOnlyList<string> TryAuditOrder(
        PendingOrder order,
        in ExecutionAuditMoneyContext money,
        Action<string> print)
    {
        SyncProtectionFromPending(order);

        if (order.Label is null || _orderAudited.Contains(order.Label))
            return Array.Empty<string>();

        _orderAudited.Add(order.Label);
        _protectionByLabel.TryGetValue(order.Label, out var protection);
        var lines = TradePlanRrLog.FormatOrderAudit(order, _plannedByLabel.GetValueOrDefault(order.Label), in money);
        foreach (var line in lines)
            print(line);

        if (_plannedByLabel.TryGetValue(order.Label, out var planned) && planned.ExpectTpOnPlace)
        {
            var actualRr = TradePlanRrLog.ActualRrFromPrices(
                order.TargetPrice, order.StopLoss, order.TakeProfit, planned.IsBuy);
            MaybeWarnRrMismatch(order.Label, planned.PlannedRr, actualRr, print);
        }

        return lines;
    }

    public IReadOnlyList<string> TryAuditPosition(
        Position position,
        in ExecutionAuditMoneyContext money,
        Action<string> print)
    {
        SyncProtectionFromPosition(position);

        if (position.Label is null || _positionAudited.Contains(position.Label))
            return Array.Empty<string>();

        _positionAudited.Add(position.Label);
        _protectionByLabel.TryGetValue(position.Label, out var protection);
        var lines = TradePlanRrLog.FormatPositionAudit(position, protection, in money);
        foreach (var line in lines)
            print(line);

        if (_plannedByLabel.TryGetValue(position.Label, out var planned))
        {
            var actualRr = TradePlanRrLog.ActualRrFromPrices(
                position.EntryPrice, position.StopLoss, position.TakeProfit, planned.IsBuy);
            MaybeWarnRrMismatch(position.Label, planned.PlannedRr, actualRr, print);
        }

        return lines;
    }

    public void OnBookLabelRemoved(
        string label,
        History history,
        string symbolName,
        in ExecutionAuditMoneyContext money,
        Action<string> print)
    {
        if (string.IsNullOrEmpty(label) || _closeAudited.Contains(label))
            return;

        if (_pendingRemoved.Contains(label))
        {
            _pendingRemoved.Remove(label);
            return;
        }

        var trade = FindLatestClosedTrade(history, symbolName, label);
        if (trade == null)
        {
            // Removed from book but no history trade yet — only audit if a manual reason is pending.
            _pendingCloseReason.TryGetValue(label, out var pendingReason);
            if (pendingReason != null)
                AuditClose(label, ClosedTradeData.Empty, pendingReason, print);
            return;
        }

        var reason = ResolveCloseReason(label, trade);
        AuditClose(label, FromHistory(trade), reason, print);
    }

    public static ClosedTradeData FromHistory(HistoricalTrade trade) => new()
    {
        HasData = true,
        RawClose = trade.ClosingPrice,
        GrossProfit = trade.GrossProfit,
        Commission = trade.Commissions,
        NetProfit = trade.NetProfit,
        CloseTime = trade.ClosingTime,
    };

    public void AuditClose(
        string label,
        ClosedTradeData data,
        string brokerReason,
        Action<string> print,
        in ExcursionSnapshot excursion = default,
        bool hasExcursion = false)
    {
        if (_closeAudited.Contains(label))
            return;

        _closeAudited.Add(label);

        _pendingCloseReason.TryGetValue(label, out var handlerReason);
        _pendingCloseReason.Remove(label);

        var planMissing = !_plannedByLabel.ContainsKey(label);
        if (planMissing)
            _closeAuditMissingCount++;

        var protection = ResolveProtection(label);
        var rawClose = data.HasData ? data.RawClose : 0;

        var closeReason = InternalCloseReasonClassifier.Classify(
            rawClose,
            in protection,
            _internalConfig.CloseReasonTolerancePips,
            _pipSize,
            handlerReason ?? brokerReason);

        var internalScore = InternalRScoreCalculator.Compute(
            in protection, data, closeReason, in _internalConfig, _pipSize);

        var internalReason = closeReason;
        var effectiveClose = InternalCloseReasonClassifier.ResolveEffectiveClose(
            closeReason, in protection, rawClose);

        if (protection.BBrokenMoveTpToEntryApplied && internalReason == "TakeProfit")
            print(TradePlanRrLog.FormatBBrokenTpEntryMisclassifiedWarning(label));

        print(TradePlanRrLog.FormatCloseAudit(
            label, closeReason, internalReason, planMissing, in protection, in data, in internalScore, effectiveClose,
            in excursion, hasExcursion));
        print(TradePlanRrLog.FormatCloseCsv(
            label, closeReason, internalReason, in protection, in internalScore, data.HasData ? data.NetProfit : 0,
            in excursion, hasExcursion));

        if (closeReason == "Unknown")
        {
            _unknownCloseCount++;
            print(TradePlanRrLog.FormatUnknownCloseWarning(label));
        }

        var clampedPnl = ExecutionPnLCalculator.ComputeCloseAudit(
            in protection,
            rawClose,
            data.HasData ? data.GrossProfit : 0,
            data.HasData ? data.Commission : 0,
            data.HasData ? data.NetProfit : 0,
            closeReason,
            _pipSize);

        MaybeWarnTpProfitTooLarge(label, closeReason, clampedPnl, in protection, print);
        RecordInternalCloseStats(label, closeReason, in protection, in data, in internalScore, in clampedPnl);
    }

    PositionProtectionSnapshot ResolveProtection(string label)
    {
        if (_protectionByLabel.TryGetValue(label, out var protection))
            return protection;
        if (_plannedByLabel.TryGetValue(label, out var planned))
            return ExecutionPnLCalculator.FromPlanned(planned, _anchorMode);
        return new PositionProtectionSnapshot { Label = label, AnchorMode = _anchorMode };
    }

    void MaybeWarnRrMismatch(string label, double plannedRr, double actualRr, Action<string> print)
    {
        if (plannedRr <= 0 || actualRr <= 0)
            return;
        if (Math.Abs(actualRr - plannedRr) <= RrMismatchThreshold)
            return;

        _rrMismatchCount++;
        print(TradePlanRrLog.FormatRrMismatchWarning(label, plannedRr, actualRr));
    }

    void MaybeWarnTpProfitTooLarge(
        string label,
        string closeReason,
        in CloseAuditPnL pnl,
        in PositionProtectionSnapshot protection,
        Action<string> print)
    {
        if (!string.Equals(closeReason, "TakeProfit", StringComparison.Ordinal))
            return;
        if (protection.PlannedRiskUsd <= 0 || protection.PlannedRr <= 0)
            return;

        var expectedMaxNet = protection.PlannedRiskUsd * protection.PlannedRr * TradePlanRrLog.TpProfitTooLargeFactor;
        if (pnl.RawNet <= expectedMaxNet)
            return;

        _tpProfitTooLargeCount++;
        print(TradePlanRrLog.FormatTpProfitTooLargeWarning(label, pnl.RawNet, expectedMaxNet));
    }

    static HistoricalTrade? FindLatestClosedTrade(History history, string symbolName, string label)
    {
        HistoricalTrade? latest = null;
        foreach (var trade in history)
        {
            if (!string.Equals(trade.SymbolName, symbolName, StringComparison.Ordinal))
                continue;
            if (!string.Equals(trade.Label, label, StringComparison.Ordinal))
                continue;
            latest = trade;
        }

        return latest;
    }

    string ResolveCloseReason(string label, HistoricalTrade trade)
    {
        if (_pendingCloseReason.TryGetValue(label, out var manual))
            return manual;

        return HistoryCloseReasonClassifier.Classify(trade);
    }

    void RecordInternalCloseStats(
        string label,
        string closeReason,
        in PositionProtectionSnapshot protection,
        in ClosedTradeData data,
        in InternalCloseScore internalScore,
        in CloseAuditPnL clampedPnl)
    {
        var bucketKey = MapBucketKey(closeReason);
        if (!_buckets.TryGetValue(bucketKey, out var bucket))
            bucket = _buckets["Unknown"];
        bucket.Count++;
        bucket.Net += data.HasData ? data.NetProfit : 0;
        bucket.ClampedNet += clampedPnl.ClampedNet;
        bucket.InternalNet += internalScore.InternalNetUsd;
        bucket.InternalNetR += internalScore.InternalNetR;

        var rawNet = data.HasData ? data.NetProfit : 0;
        var commission = data.HasData ? data.Commission : 0;

        _rawNetSum += rawNet;
        _clampedNetSum += clampedPnl.ClampedNet;
        _internalNetSum += internalScore.InternalNetUsd;
        _internalGrossRSum += internalScore.InternalGrossR;
        _internalNetRSum += internalScore.InternalNetR;
        _totalCommission += commission;
        _rawVsInternalNetDiffSum += internalScore.RawVsInternalNetDiff;

        if (protection.PlannedRiskUsd > 0 && commission != 0)
            _commissionR += commission / protection.PlannedRiskUsd;

        if (clampedPnl.RawNet >= 0)
        {
            _rawWinNets.Add(clampedPnl.RawNet);
            _clampedWinNets.Add(clampedPnl.ClampedNet);
            _winnerRNet.Add(clampedPnl.ClampedR);
        }
        else
        {
            _rawLossNets.Add(clampedPnl.RawNet);
            _clampedLossNets.Add(clampedPnl.ClampedNet);
            _loserRNet.Add(clampedPnl.ClampedR);
        }

        if (internalScore.InternalNetUsd >= 0)
        {
            _internalWinNets.Add(internalScore.InternalNetUsd);
            _internalWinR.Add(internalScore.InternalNetR);
        }
        else
        {
            _internalLossNets.Add(internalScore.InternalNetUsd);
            _internalLossR.Add(internalScore.InternalNetR);
        }

        if (internalScore.InternalNetR > InternalStrategyMetricsEngine.BreakevenRTolerance)
            bucket.WinCount++;

        if (clampedPnl.HasTpOvershoot)
        {
            _tpOvershootCount++;
            _tpOvershootPipsSum += clampedPnl.TpOvershootPips;
            if (clampedPnl.TpOvershootPips > _maxTpOvershootPips)
                _maxTpOvershootPips = clampedPnl.TpOvershootPips;
        }

        var closeTime = data.CloseTime != default ? data.CloseTime : DateTime.UtcNow;
        _strategyMetrics.RecordClose(
            label,
            closeTime,
            protection.IsBuy,
            closeReason,
            in internalScore,
            data.HasData ? data.NetProfit : 0);
    }

    static string MapBucketKey(string closeReason) => closeReason switch
    {
        "TakeProfit" => "TakeProfit",
        "StopLoss" => "StopLoss",
        "BBrokenTpEntry" => "BBrokenTpEntry",
        "XRBarClose" => "XRBarClose",
        "XRBarCloseNextOpen" => "XRBarCloseNextOpen",
        "XRBarCloseActual" => "XRBarCloseActual",
        "SessionStop" => "SessionStop",
        "BBroken" => "BBroken",
        "ManualClose" => "ManualClose",
        _ => "Unknown",
    };

    public void PrintExecutionSummary(Action<string> print, int historyClosedCount)
    {
        print("[L6BT] Execution audit summary:");
        print($"[L6BT]   historyClosedCount={historyClosedCount} closeAuditCount={CloseAuditCount} closeAuditMissingCount={_closeAuditMissingCount}");
        print($"[L6BT]   rawNet={Fmt2(_rawNetSum)} clampedNet={Fmt2(_clampedNetSum)}");
        print($"[L6BT]   rawPF={FmtPf(_rawWinNets, _rawLossNets)} clampedPF={FmtPf(_clampedWinNets, _clampedLossNets)}");
        print($"[L6BT]   tpOvershootCount={_tpOvershootCount} avgTpOvershootPips={FormatAvgOvershoot()} maxTpOvershootPips={Fmt2(_maxTpOvershootPips)}");
        print($"[L6BT]   rrMismatch={_rrMismatchCount} tpProfitTooLarge={_tpProfitTooLargeCount}");
        print($"[L6BT]   protectionTpMismatchCount={_protectionTpMismatchCount} protectionSlMismatchCount={_protectionSlMismatchCount}");
        print($"[L6BT]   protectionSyncOkCount={_protectionSyncOkCount} protectionSyncRejectCount={_protectionSyncRejectCount}");

        PrintInternalRScoreSummary(print, historyClosedCount);
    }

    void PrintInternalRScoreSummary(Action<string> print, int historyClosedCount)
    {
        var closedCount = CloseAuditCount;
        var normalTakeProfitCount = _buckets["TakeProfit"].Count;
        var stopLossCount = _buckets["StopLoss"].Count;
        var bBrokenTpEntryCount = _buckets["BBrokenTpEntry"].Count;
        var bBrokenCount = _buckets["BBroken"].Count;
        var sessionStopCount = _buckets["SessionStop"].Count;
        var manualCloseCount = _buckets["ManualClose"].Count;
        var unknownCount = _buckets["Unknown"].Count;
        var internalPf = FmtPf(_internalWinNets, _internalLossNets);
        var internalWinRate = closedCount > 0
            ? Fmt2((double)_internalWinNets.Count / closedCount * 100) + "%"
            : "n/a";

        print("[L6BT] INTERNAL R-SCORE SUMMARY");
        print($"[L6BT]   closedCount={closedCount} closeAuditCount={closedCount} planMissingCount={_closeAuditMissingCount} unknownCloseCount={_unknownCloseCount}");
        print($"[L6BT]   normalTakeProfitCount={normalTakeProfitCount} stopLossCount={stopLossCount} bBrokenTpEntryCount={bBrokenTpEntryCount} bBrokenCount={bBrokenCount} sessionStopCount={sessionStopCount} manualCloseCount={manualCloseCount} unknownCount={unknownCount}");
        print($"[L6BT]   normalTakeProfitNetUsd={Fmt2(_buckets["TakeProfit"].InternalNet)} stopLossNetUsd={Fmt2(_buckets["StopLoss"].InternalNet)} bBrokenTpEntryNetUsd={Fmt2(_buckets["BBrokenTpEntry"].InternalNet)} bBrokenNetUsd={Fmt2(_buckets["BBroken"].InternalNet)} sessionStopNetUsd={Fmt2(_buckets["SessionStop"].InternalNet)} manualCloseNetUsd={Fmt2(_buckets["ManualClose"].InternalNet)} unknownNetUsd={Fmt2(_buckets["Unknown"].InternalNet)}");
        print($"[L6BT]   rawNet={Fmt2(_rawNetSum)} internalNetUsd={Fmt2(_internalNetSum)} rawVsInternalNetDiff={Fmt2(_rawVsInternalNetDiffSum)}");
        print($"[L6BT]   internalGrossR={Fmt2(_internalGrossRSum)} internalNetR={Fmt2(_internalNetRSum)} internalAvgR={FormatAvgR()} internalWinRate={internalWinRate} internalPF={internalPf}");
        print($"[L6BT]   internalAvgWinR={FormatAvg(_internalWinR)} internalAvgLossR={FormatAvg(_internalLossR)}");
        print($"[L6BT]   totalCommission={Fmt2(_totalCommission)} commissionR={Fmt2(_commissionR)}");
        print($"[L6BT]   protectionTpMismatchCount={_protectionTpMismatchCount} protectionSlMismatchCount={_protectionSlMismatchCount}");
        print($"[L6BT]   protectionSyncOkCount={_protectionSyncOkCount} protectionSyncRejectCount={_protectionSyncRejectCount}");
        PrintVirtualXrSummary(print);

        print(TradePlanRrLog.FormatInternalSummaryCsv(
            closedCount, normalTakeProfitCount, stopLossCount, bBrokenTpEntryCount, bBrokenCount,
            sessionStopCount, manualCloseCount, unknownCount,
            _internalNetSum, _internalNetRSum, ParsePf(internalPf), _rawNetSum, _rawVsInternalNetDiffSum));

        if (closedCount != historyClosedCount)
            print(TradePlanRrLog.FormatCloseAuditCountMismatchWarning(closedCount, historyClosedCount));

        if (_unknownCloseCount > 0)
            print(TradePlanRrLog.FormatUnknownClosesExistWarning(_unknownCloseCount));

        if (ShouldWarnRawInternalNetDiff())
            print(TradePlanRrLog.FormatRawInternalNetDiffWarning(_rawNetSum, _internalNetSum, _rawVsInternalNetDiffSum));

        var auditSnapshot = BuildStrategyAuditSnapshot(historyClosedCount);
        _strategyMetrics.PrintStrategyEvaluation(print, auditSnapshot);
        _strategyMetrics.PrintOptResultLine(print, auditSnapshot);
    }

    InternalStrategyAuditSnapshot BuildStrategyAuditSnapshot(int historyClosedCount)
    {
        var reasonCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var kv in _buckets)
            reasonCounts[kv.Key] = kv.Value.Count;

        var xrCount = _buckets["XRBarClose"].Count
                      + _buckets["XRBarCloseNextOpen"].Count
                      + _buckets["XRBarCloseActual"].Count;

        return new InternalStrategyAuditSnapshot
        {
            HistoryClosedCount = historyClosedCount,
            CloseAuditCount = CloseAuditCount,
            PlanMissingCount = _closeAuditMissingCount,
            UnknownCloseCount = _unknownCloseCount,
            ProtectionTpMismatchCount = _protectionTpMismatchCount,
            ProtectionSlMismatchCount = _protectionSlMismatchCount,
            ProtectionSyncOkCount = _protectionSyncOkCount,
            ProtectionSyncRejectCount = _protectionSyncRejectCount,
            RawNetSum = _rawNetSum,
            RawVsInternalNetDiffSum = _rawVsInternalNetDiffSum,
            ReasonCounts = reasonCounts,
            EnableVirtualXrBarCloseExit = _virtualXrCfg.Enabled,
            VirtualXrTriggerR = _virtualXrCfg.TriggerR,
            VirtualXrFillMode = _virtualXrCfg.FillMode.ToString(),
            XrBarCloseCount = xrCount,
            XrBarCloseNetR = SumXrBucketNetR(),
            XrTouchNoConfirmCount = _xrTouchNoConfirmCount,
        };
    }

    void PrintVirtualXrSummary(Action<string> print)
    {
        var xrCount = _buckets["XRBarClose"].Count
                      + _buckets["XRBarCloseNextOpen"].Count
                      + _buckets["XRBarCloseActual"].Count;
        var xrNetUsd = _buckets["XRBarClose"].InternalNet
                       + _buckets["XRBarCloseNextOpen"].InternalNet
                       + _buckets["XRBarCloseActual"].InternalNet;
        var xrNetR = SumXrBucketNetR();
        var xrWinRate = XrWinRate();

        print($"[L6BT]   xrBarCloseCount={xrCount} xrBarCloseNetUsd={Fmt2(xrNetUsd)} xrBarCloseNetR={Fmt4(xrNetR)} xrBarCloseAvgR={Fmt4(xrCount > 0 ? xrNetR / xrCount : 0)}");
        print($"[L6BT]   xrTouchNoConfirmCount={_xrTouchNoConfirmCount} xrConfirmedCount={_xrConfirmedCount} xrAvgR={Fmt4(xrCount > 0 ? xrNetR / xrCount : 0)} xrWinRate={xrWinRate}");
    }

    double SumXrBucketNetR() =>
        _buckets["XRBarClose"].InternalNetR
        + _buckets["XRBarCloseNextOpen"].InternalNetR
        + _buckets["XRBarCloseActual"].InternalNetR;

    string XrWinRate()
    {
        var xrCount = _buckets["XRBarClose"].Count
                      + _buckets["XRBarCloseNextOpen"].Count
                      + _buckets["XRBarCloseActual"].Count;
        if (xrCount == 0)
            return "n/a";

        var wins = _buckets["XRBarClose"].WinCount
                   + _buckets["XRBarCloseNextOpen"].WinCount
                   + _buckets["XRBarCloseActual"].WinCount;
        return Fmt2(wins * 100.0 / xrCount) + "%";
    }

    static string Fmt4(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    bool ShouldWarnRawInternalNetDiff()
    {
        var absDiff = Math.Abs(_rawVsInternalNetDiffSum);
        if (absDiff <= 0.01)
            return false;
        var threshold = Math.Max(100, Math.Abs(_internalNetSum) * 0.05);
        return absDiff > threshold;
    }

    string FormatAvgR() => CloseAuditCount > 0 ? Fmt2(_internalNetRSum / CloseAuditCount) : "n/a";

    static string FormatAvg(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return "n/a";
        return Fmt2(values.Sum() / values.Count);
    }

    static string FmtPf(IReadOnlyList<double> wins, IReadOnlyList<double> losses)
    {
        var winSum = wins.Sum();
        var lossSum = losses.Sum();
        if (lossSum >= 0 || wins.Count == 0)
            return "n/a";
        return Fmt2(winSum / Math.Abs(lossSum));
    }

    static string Fmt2(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    static double ParsePf(string pf) =>
        double.TryParse(pf, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    string FormatAvgOvershoot() =>
        _tpOvershootCount > 0
            ? Fmt2(_tpOvershootPipsSum / _tpOvershootCount)
            : "n/a";

    sealed class CloseBucket
    {
        public int Count { get; set; }
        public int WinCount { get; set; }
        public double Net { get; set; }
        public double ClampedNet { get; set; }
        public double InternalNet { get; set; }
        public double InternalNetR { get; set; }
    }
}

public sealed class PlannedAuditSnapshot
{
    public string Label { get; init; } = "";
    public bool IsBuy { get; init; }
    public double PlannedEntry { get; init; }
    public double PlannedSl { get; init; }
    public double PlannedTp { get; init; }
    public double PlannedSlPips { get; init; }
    public double PlannedTpPips { get; init; }
    public double PlannedRr { get; init; }
    public double CfgRr { get; init; }
    public double PlannedRiskUsd { get; init; }
    public double VolumeUnits { get; init; }
    public bool ExpectTpOnPlace { get; init; }
    public double SpreadPipsAtPlan { get; init; }
    public string SpreadSourceAtPlan { get; init; } = "";
}

public sealed class VirtualXrPendingClose
{
    public string Label { get; init; } = "";
    public string Reason { get; init; } = "";
    public VirtualXrBarOhlc SignalBar { get; init; }
    public double NextBarOpen { get; init; }
}
