using System;
using System.Collections.Generic;
using System.Text;
using cAlgo.API;
using cAlgo.API.Internals;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Engines.AlertEvaluators;
using TradeAlert.Core.Models;
using TradeAlert.Core.Models.KeyLevel;
using TradeAlert.Core.Models.Alerts;
using TradeAlert.Core.Series;
using TradeAlert.Indicator.Host;

namespace TradeAlert.Indicator;

/// <summary>
/// One <see cref="PineStateEngine"/> per timeframe for compound alert evaluation.
/// HTF engines advance via synchronized temporal cursor; chart TF uses the host shell.
/// </summary>
public sealed class MtfEngineManager : ICompoundRuleEvalContext
{
    readonly AlertEngine _engine = new();
    readonly string _symbol;
    readonly double _tickSize;
    readonly Dictionary<string, MtfTfEntry> _entries = new(StringComparer.Ordinal);
    MarketData? _marketData;
    bool _alertTrackingDebug;
    bool _realZoneDebug;
    bool _htfTouchParityDebug;
    Action<IReadOnlyList<EventAlertTrackingLabel>>? _alertTrackingDraw;
    Action<string>? _alertTrackingLog;
    Action<bool, DateTime, double>? _m5CondFireDraw;
    int _currentChartBarIndex = -1;

    public MtfEngineManager(string symbol, double tickSize = 0.00001)
    {
        _symbol = symbol;
        _tickSize = tickSize;
    }

    public void ConfigureAlertTracking(
        bool enabled,
        Action<IReadOnlyList<EventAlertTrackingLabel>>? drawFn,
        Action<string>? logFn = null,
        Action<bool, DateTime, double>? condFireDraw = null)
    {
        _alertTrackingDebug = enabled;
        _alertTrackingDraw = drawFn;
        _alertTrackingLog = logFn;
        _m5CondFireDraw = condFireDraw;
    }

    public void ConfigureRealZoneDebug(bool enabled) => _realZoneDebug = enabled;

    /// <summary>Sync header + pivot key audit on HTF touch table (parity debug tiers 1+2).</summary>
    public void ConfigureHtfTouchParityDebug(bool enabled) => _htfTouchParityDebug = enabled;

    public bool HtfTouchParityDebugEnabled => _htfTouchParityDebug;

    public bool HasTfEntry(string tfToken) => _entries.ContainsKey(tfToken);

    public string Symbol => _symbol;

    /// <summary>
    /// Emit M5 alert-tracking labels for HTF bars already fed by compound (no re-feed).
    /// </summary>
    public void FlushPendingM5AlertTracking()
    {
        if (!_alertTrackingDebug || _alertTrackingDraw == null)
            return;

        foreach (var entry in _entries.Values)
        {
            if (entry.IsChartTfEntry || entry.TfToken != "5")
                continue;

            while (entry.AlertTrackEmitThroughIdx < entry.NextBarIdxToProcess)
            {
                EmitAlertTrackingForClosedHtfBar(entry, entry.AlertTrackEmitThroughIdx);
                entry.AlertTrackEmitThroughIdx++;
            }
        }
    }

    /// <summary>
    /// Feed M5 bars up to chart close when compound rules are off; otherwise only flush pending labels.
    /// </summary>
    public void SyncM5AlertTrackingToChartBarClose(
        DateTime chartBarCloseTimeChartLocal,
        bool realtimeChartEdge,
        bool compoundFeedsM5)
    {
        if (!_alertTrackingDebug || _alertTrackingDraw == null)
            return;

        if (compoundFeedsM5)
        {
            FlushPendingM5AlertTracking();
            return;
        }

        foreach (var entry in _entries.Values)
        {
            if (entry.IsChartTfEntry || entry.TfToken != "5")
                continue;

            while (entry.NextBarIdxToProcess < entry.TfBars.Count)
            {
                var idx = entry.NextBarIdxToProcess;
                if (!CanFeedClosedHtfBar(entry, idx, chartBarCloseTimeChartLocal, realtimeChartEdge))
                    break;

                FeedClosedHtfBar(entry, idx);
                EmitAlertTrackingForClosedHtfBar(entry, idx);
                entry.AlertTrackEmitThroughIdx = idx + 1;
                entry.NextBarIdxToProcess++;
            }
        }
    }

    void EmitAlertTrackingForClosedHtfBar(MtfTfEntry entry, int barIndex)
    {
        var barOpenLocal = ChartTimeFromBars.NormalizeBarOpenTime(entry.TfBars.OpenTimes[barIndex]);
        _alertTrackingLog?.Invoke(
            $"[M5Track] emit bar={barIndex} t={barOpenLocal:HH:mm} pivots={entry.Shell.State.BuildPivotEntries(barIndex).Count}");
        var ctx = BuildHtfEvaluationContext(
            entry,
            barIndex,
            barOpenLocal,
            entry.TfBars.ClosePrices[barIndex],
            entry.TfBars.HighPrices[barIndex],
            entry.TfBars.LowPrices[barIndex],
            isBarClosed: true,
            isRealtime: false,
            isNewBarOnRealtimeForming: false,
            chartM15CloseEdgeInjected: false,
            touchLtfOpenPrice: null);
        MaybeEmitEventTracking(in ctx);
        // Always capture emit diagnostics (independent of log/draw subscriptions)
        var pivotsCount = entry.Shell.State.BuildPivotEntries(barIndex).Count;
        if (entry.Shell.EventDetectionCache != null)
        {
            entry.Shell.EventDetectionCache.TryGet(barIndex, entry.TfToken, isM15: false, out var emitCounts);
            RecordDiagEmit(entry.TfToken, barIndex, barOpenLocal,
                emitCounts.BuyHL, emitCounts.SellHL, emitCounts.BuyNG, emitCounts.SellNG, pivotsCount);

            if (_alertTrackingLog != null)
            {
                _alertTrackingLog.Invoke(
                    $"[M5Track] result bar={barIndex} buyHL={emitCounts.BuyHL} sellHL={emitCounts.SellHL} buyNG={emitCounts.BuyNG} sellNG={emitCounts.SellNG}");
            }
            if (_m5CondFireDraw != null)
            {
                if (emitCounts.BuyHL + emitCounts.BuyNG > 0)
                    _m5CondFireDraw.Invoke(true, barOpenLocal, entry.TfBars.HighPrices[barIndex]);
                if (emitCounts.SellHL + emitCounts.SellNG > 0)
                    _m5CondFireDraw.Invoke(false, barOpenLocal, entry.TfBars.LowPrices[barIndex]);
            }
        }
        else
        {
            RecordDiagEmit(entry.TfToken, barIndex, barOpenLocal, 0, 0, 0, 0, pivotsCount);
        }
    }

    public void RegisterChartTfShell(string tfToken, TradeAlertIndicator shell, Bars bars, TimeSpan tfPeriod)
    {
        _entries[tfToken] = new MtfTfEntry
        {
            Shell = shell,
            TfBars = bars,
            TfToken = tfToken,
            TfPeriod = tfPeriod,
            IsChartTfEntry = true,
            NextBarIdxToProcess = 0,
            AlertTrackEmitThroughIdx = 0,
            ConditionCache = new Dictionary<AlertConditionId, MtfConditionHit>(),
        };
    }

    public void InitializeHtfEngines(
        IEnumerable<string> requiredTfTokens,
        string chartTfToken,
        MarketData marketData,
        in Loop6HostParameterSnapshot parameters,
        Action<string>? logFn) =>
        InitializeHtfEnginesCore(requiredTfTokens, chartTfToken, marketData, null, in parameters, logFn);

    /// <summary>
    /// Symbol-specific overload for multi-symbol scanner cBot.
    /// Uses <c>MarketData.GetBars(timeFrame, symbolName)</c> instead of
    /// the chart-default <c>GetBars(timeFrame)</c>.
    /// </summary>
    public void InitializeHtfEnginesForSymbol(
        IEnumerable<string> requiredTfTokens,
        string chartTfToken,
        MarketData marketData,
        string symbolName,
        in Loop6HostParameterSnapshot parameters,
        Action<string>? logFn) =>
        InitializeHtfEnginesCore(requiredTfTokens, chartTfToken, marketData, symbolName, in parameters, logFn);

    void InitializeHtfEnginesCore(
        IEnumerable<string> requiredTfTokens,
        string chartTfToken,
        MarketData marketData,
        string? symbolName,
        in Loop6HostParameterSnapshot parameters,
        Action<string>? logFn)
    {
        _marketData = marketData;

        foreach (var tfToken in requiredTfTokens)
        {
            if (tfToken == chartTfToken)
                continue;

            if (_entries.ContainsKey(tfToken))
                continue;

            if (!Loop6TimeFrameLookup.TryResolve(tfToken, out var timeFrame, out var period))
            {
                logFn?.Invoke($"[Compound] Unknown TF token '{tfToken}' — skipped");
                continue;
            }

            var sym = ResolveSymbolName(symbolName);
            var tfBars = ChartSymbolMarketGuard.GetBars(marketData, timeFrame, sym, logFn);

            if (tfBars == null || tfBars.Count == 0)
            {
                logFn?.Invoke($"[Compound] No bars for TF '{tfToken}' — skipped");
                continue;
            }

            var hostCtx = new IndicatorHostContext
            {
                Symbol = _symbol,
                ChartTimeframeToken = tfToken,
                HostChartIsRealtime = false,
            };

            var shell = new TradeAlertIndicator(hostCtx, VoidAlertFireSink.Instance);
            Loop6ParameterBridge.Apply(shell, in parameters);
            ApplyTfModeFlags(shell, tfToken);

            var ltfCollector = new HostLtfCollector();
            ltfCollector.Configure(timeFrame, tfToken);
            if (ltfCollector.HasLtfMapping)
            {
                ltfCollector.BindSymbol(sym, logFn);
                ltfCollector.EnsureLtfBarsForSymbol(marketData, sym);
            }
            shell.State.ObLtfMappingResolved = ltfCollector.HasLtfMapping;

            _entries[tfToken] = new MtfTfEntry
            {
                Shell = shell,
                TfBars = tfBars,
                TfToken = tfToken,
                TfPeriod = period,
                IsChartTfEntry = false,
                NextBarIdxToProcess = 0,
                AlertTrackEmitThroughIdx = 0,
                ConditionCache = new Dictionary<AlertConditionId, MtfConditionHit>(),
                LtfCollector = ltfCollector.HasLtfMapping ? ltfCollector : null,
            };
        }
    }

    /// <summary>Read-only access to a TF shell's Pine state (chart or HTF entry).</summary>
    public bool TryGetState(string tfToken, out PineStateEngine state)
    {
        if (_entries.TryGetValue(tfToken, out var entry))
        {
            state = entry.Shell.State;
            return true;
        }

        state = null!;
        return false;
    }

    /// <summary>OHLC series for chart or HTF shell at <paramref name="tfToken"/>.</summary>
    public bool TryGetSeriesBuffer(string tfToken, out SeriesBuffer buffer)
    {
        if (_entries.TryGetValue(tfToken, out var entry))
        {
            buffer = entry.Shell.Series.Buffer;
            return true;
        }

        buffer = null!;
        return false;
    }

    /// <summary>Idempotent HTF registration for gate zone reads (reuses compound init path).</summary>
    public void EnsureTfEnginesRegistered(
        IEnumerable<string> tfTokens,
        string chartTfToken,
        MarketData marketData,
        string? symbolName,
        in Loop6HostParameterSnapshot parameters,
        Action<string>? logFn) =>
        InitializeHtfEnginesCore(tfTokens, chartTfToken, marketData, symbolName, in parameters, logFn);

    /// <summary>
    /// Feed HTF shells up to chart bar close for gate zone collection.
    /// Does not update compound condition cache — state sync only.
    /// </summary>
    public void SyncHtfEngineBarsForGate(DateTime chartBarCloseTimeChartLocal, bool realtimeChartEdge)
    {
        foreach (var entry in _entries.Values)
        {
            if (entry.IsChartTfEntry)
                continue;

            while (entry.NextBarIdxToProcess < entry.TfBars.Count)
            {
                var idx = entry.NextBarIdxToProcess;
                if (!CanFeedClosedHtfBar(entry, idx, chartBarCloseTimeChartLocal, realtimeChartEdge))
                    break;

                FeedClosedHtfBar(entry, idx);
                entry.NextBarIdxToProcess++;
            }
        }
    }

    public void ApplyParametersToHtfShells(in Loop6HostParameterSnapshot parameters)
    {
        foreach (var entry in _entries.Values)
        {
            if (entry.IsChartTfEntry)
                continue;

            Loop6ParameterBridge.Apply(entry.Shell, in parameters);
            ApplyTfModeFlags(entry.Shell, entry.TfToken);
            if (entry.LtfCollector != null && _marketData != null)
            {
                entry.LtfCollector.EnsureLtfBarsForSymbol(_marketData, _symbol);
                entry.Shell.State.ObLtfMappingResolved = entry.LtfCollector.HasLtfMapping;
            }
        }
    }

    public int AdvanceHtfEnginesToChartBarCloseTime(
        DateTime chartBarCloseTimeChartLocal,
        IReadOnlyDictionary<string, AlertConditionId[]> trackedConditionsPerTf,
        bool realtimeChartEdge,
        string? countClosedBarsForTfToken = null,
        Action<string, int, DateTime>? onHtfBarClosedEval = null)
    {
        var closedForToken = 0;

        foreach (var entry in _entries.Values)
        {
            if (entry.IsChartTfEntry)
                continue;

            if (!trackedConditionsPerTf.TryGetValue(entry.TfToken, out var conds) || conds.Length == 0)
                continue;

            while (entry.NextBarIdxToProcess < entry.TfBars.Count)
            {
                var idx = entry.NextBarIdxToProcess;
                if (!CanFeedClosedHtfBar(entry, idx, chartBarCloseTimeChartLocal, realtimeChartEdge))
                    break;

                if (_alertTrackingDebug && entry.TfToken == "5")
                    entry.Shell.EventDetectionCache?.Reset();

                FeedClosedHtfBar(entry, idx);
                RecordDiagFeed(entry.TfToken, idx);
                if (_alertTrackingDebug && entry.TfToken == "5")
                    EmitAlertTrackingForClosedHtfBar(entry, idx);

                UpdateConditionCacheForEntry(
                    entry,
                    idx,
                    entry.TfBars.OpenTimes[idx],
                    isBarClosed: true,
                    isRealtime: false,
                    isNewBarOnRealtimeForming: false,
                    chartM15CloseEdgeInjected: false,
                    conds,
                    evalChartCloseTime: chartBarCloseTimeChartLocal);
                entry.NextBarIdxToProcess++;

                if (_alertTrackingDebug && entry.TfToken == "5")
                    entry.AlertTrackEmitThroughIdx = entry.NextBarIdxToProcess;

                if (countClosedBarsForTfToken != null
                    && string.Equals(entry.TfToken, countClosedBarsForTfToken, StringComparison.Ordinal))
                    closedForToken++;

                onHtfBarClosedEval?.Invoke(entry.TfToken, idx, entry.TfBars.OpenTimes[idx]);
            }
        }

        return closedForToken;
    }

    /// <summary>
    /// HTF forming-bar sync for chart TF &lt; HTF: run <see cref="PineStateEngine.OnBar"/> on the
    /// HTF forming bar using full platform OHLC (Pine parity with native HTF chart — repaint OK).
    /// </summary>
    public void SyncHtfFormingBarsToChartCloseTime(
        DateTime chartBarCloseTimeChartLocal,
        IReadOnlyDictionary<string, AlertConditionId[]> trackedConditionsPerTf,
        bool realtimeChartEdge,
        bool chartM15CloseEdgeInjected = false)
    {
        foreach (var entry in _entries.Values)
        {
            if (entry.IsChartTfEntry)
                continue;

            if (!trackedConditionsPerTf.TryGetValue(entry.TfToken, out var conds) || conds.Length == 0)
                continue;

            var formingIdx = entry.NextBarIdxToProcess;
            if (formingIdx < 0 || formingIdx >= entry.TfBars.Count)
                continue;

            var htfOpen = ChartTimeFromBars.NormalizeBarOpenTime(entry.TfBars.OpenTimes[formingIdx]);
            var htfClose = htfOpen.Add(entry.TfPeriod);
            if (chartBarCloseTimeChartLocal <= htfOpen)
                continue;
            if (chartBarCloseTimeChartLocal >= htfClose)
                continue;

            if (!FeedFormingHtfBarPlatformParity(entry, formingIdx, realtimeChartEdge))
                continue;

            UpdateConditionCacheForEntry(
                entry,
                formingIdx,
                entry.TfBars.OpenTimes[formingIdx],
                isBarClosed: false,
                isRealtime: true,
                isNewBarOnRealtimeForming: false,
                chartM15CloseEdgeInjected,
                conds,
                reevalStateOnBarClose: true,
                evalChartCloseTime: chartBarCloseTimeChartLocal,
                barHighOverride: entry.LastFormingPartialHigh,
                barLowOverride: entry.LastFormingPartialLow,
                barCloseOverride: entry.LastFormingPartialClose);
        }
    }

    /// <summary>
    /// Pine parity: feed chart OHLC extremes into RealZone + STATE pass (Touch/Real) before
    /// bar-close eval. In BacktestBarClose there is no tick stream — using the closed bar's
    /// High/Low approximates intra-bar touch the same way VisualBacktesting ticks do.
    /// </summary>
    public void UpdateRealtimePriceFromChart(
        int chartBarIndex,
        double chartHigh,
        double chartLow,
        double tickSize,
        IReadOnlyDictionary<string, AlertConditionId[]> trackedConditionsPerTf,
        bool chartM15CloseEdgeInjected,
        bool updateChartTfEntry = false,
        DateTime? chartBarCloseTimeChartLocal = null)
    {
        foreach (var entry in _entries.Values)
        {
            if (!trackedConditionsPerTf.TryGetValue(entry.TfToken, out var conds) || conds.Length == 0)
                continue;

            entry.Shell.State.TickSize = tickSize;

            int barIdx;
            double barHigh;
            double barLow;
            DateTime barOpenRaw;

            if (entry.IsChartTfEntry)
            {
                if (!updateChartTfEntry
                    || chartBarIndex < 0
                    || chartBarIndex >= entry.TfBars.Count)
                    continue;

                barIdx     = chartBarIndex;
                barHigh    = chartHigh;
                barLow     = chartLow;
                barOpenRaw = entry.TfBars.OpenTimes[barIdx];
            }
            else
            {
                if (entry.TfBars.Count == 0 || entry.NextBarIdxToProcess == 0)
                    continue;

                barIdx = entry.TfBars.Count - 1;
                if (chartBarCloseTimeChartLocal.HasValue
                    && entry.LastFormingSyncBarIdx == barIdx
                    && entry.LastFormingSyncClip == chartBarCloseTimeChartLocal.Value)
                    continue;

                if (entry.LastFormingSyncBarIdx == barIdx && entry.HasFormingShellSync)
                {
                    barHigh = entry.LastFormingPartialHigh;
                    barLow  = entry.LastFormingPartialLow;
                }
                else
                {
                    barHigh = entry.TfBars.HighPrices.LastValue;
                    barLow  = entry.TfBars.LowPrices.LastValue;
                }

                barOpenRaw = entry.TfBars.OpenTimes[barIdx];
            }

            entry.Shell.State.RealZone.OnRealtimeBarPrice(barHigh, barLow);

            double? hiOverride = null;
            double? loOverride = null;
            double? clOverride = null;
            if (!entry.IsChartTfEntry
                && entry.LastFormingSyncBarIdx == barIdx
                && entry.HasFormingShellSync)
            {
                hiOverride = entry.LastFormingPartialHigh;
                loOverride = entry.LastFormingPartialLow;
                clOverride = entry.LastFormingPartialClose;
            }

            UpdateConditionCacheForEntry(
                entry,
                barIdx,
                barOpenRaw,
                isBarClosed: false,
                isRealtime: true,
                isNewBarOnRealtimeForming: false,
                chartM15CloseEdgeInjected,
                conds,
                reevalStateOnBarClose: true,
                evalChartCloseTime: chartBarCloseTimeChartLocal,
                barHighOverride: hiOverride,
                barLowOverride: loOverride,
                barCloseOverride: clOverride);
        }
    }

    /// <summary>
    /// Tag the current chart bar index up-front so HTF (e.g. M5) event fires that happen
    /// during the same RunOneTick get stamped with the CURRENT chart bar in
    /// <see cref="MtfConditionHit.LastFiredChartBarIndex"/>. Without this, in
    /// BacktestBarClose mode the M5 advance runs before <see cref="UpdateConditionCache"/>
    /// for the chart TF and would otherwise stamp events with the previous chart bar —
    /// causing <see cref="CompoundEvalSyncMode.TradingView"/> AND-check to fail
    /// (<c>lastFiredChartBarIndex == currentChartBarIndex</c> never matches).
    /// </summary>
    public void SetCurrentChartBarIndex(int chartBarIndex) =>
        _currentChartBarIndex = chartBarIndex;

    /// <summary>Last evaluated / current sync bar index for a TF engine (e.g. M5 on M15 chart).</summary>
    public int GetSyncBarIndex(string tfToken) =>
        _entries.TryGetValue(tfToken, out var entry) ? CurrentBarIndexForSync(entry) : -1;

    public void UpdateConditionCache(
        string tfToken,
        int sourceBarIndex,
        DateTime sourceBarOpenTimeRaw,
        bool isBarClosed,
        bool isRealtime,
        bool isNewBarOnRealtimeForming,
        bool chartM15CloseEdgeInjected,
        IReadOnlyList<AlertConditionId> conditions,
        bool reevalStateOnBarClose = true)
    {
        _currentChartBarIndex = sourceBarIndex;

        if (!_entries.TryGetValue(tfToken, out var entry))
            return;

        UpdateConditionCacheForEntry(
            entry,
            sourceBarIndex,
            sourceBarOpenTimeRaw,
            isBarClosed,
            isRealtime,
            isNewBarOnRealtimeForming,
            chartM15CloseEdgeInjected,
            conditions,
            reevalStateOnBarClose);
    }

    public bool IsConditionActive(
        string tfToken,
        AlertConditionId condId,
        CompoundEvalSyncMode syncMode,
        int eventValidBars)
    {
        if (!_entries.TryGetValue(tfToken, out var entry))
            return false;

        if (!entry.ConditionCache.TryGetValue(condId, out var hit))
            return false;

        if (CompoundRuleParser.IsStateCondition(condId))
            return hit.IsCurrentlyActive;

        if (hit.LastFiredBarIndex < 0)
            return false;

        // Min-TF engines (M5 on M15 chart): window in native TF bars for M5 event legs (R1/R2).
        // Other HTF engines keep chart-bar window for Pine parity on slower chart TF.
        if (!entry.IsChartTfEntry)
        {
            if (string.Equals(tfToken, "5", StringComparison.Ordinal))
            {
                var currentM5 = CurrentBarIndexForSync(entry);
                return CompoundEvalSync.IsEventActiveOnCurrentBar(
                    syncMode, hit.LastFiredBarIndex, currentM5, eventValidBars);
            }

            return CompoundEvalSync.IsEventActiveOnCurrentBar(
                syncMode, hit.LastFiredChartBarIndex, _currentChartBarIndex, eventValidBars);
        }

        var currentIdx = CurrentBarIndexForSync(entry);
        return CompoundEvalSync.IsEventActiveOnCurrentBar(
            syncMode, hit.LastFiredBarIndex, currentIdx, eventValidBars);
    }

    public string FormatConditionStatus(
        string tfToken,
        AlertConditionId condId,
        CompoundEvalSyncMode syncMode,
        int eventValidBars)
    {
        if (!IsConditionActive(tfToken, condId, syncMode, eventValidBars))
            return "[--]";

        if (!_entries.TryGetValue(tfToken, out var entry))
            return "[--]";

        if (!entry.ConditionCache.TryGetValue(condId, out var hit))
            return "[--]";

        if (CompoundRuleParser.IsStateCondition(condId))
            return "[OK] (state)";

        return entry.IsChartTfEntry
            ? $"[OK] bar#{hit.LastFiredBarIndex}"
            : $"[OK] m5bar#{hit.LastFiredBarIndex} @M15bar#{hit.LastFiredChartBarIndex}";
    }

    /// <summary>Returns the HTF bar index at which the given event condition last fired. Used for per-event compound dedup.</summary>
    public int GetLastFiredBarIndex(string tfToken, AlertConditionId condId)
    {
        if (!_entries.TryGetValue(tfToken, out var entry)) return -1;
        if (!entry.ConditionCache.TryGetValue(condId, out var hit)) return -1;
        return hit.LastFiredBarIndex;
    }

    public bool IsStateConditionActive(string tfToken, AlertConditionId condId)
    {
        if (!_entries.TryGetValue(tfToken, out var entry))
            return false;

        return entry.ConditionCache.TryGetValue(condId, out var hit) && hit.IsCurrentlyActive;
    }

    public bool TryGetEventFiredBarIndex(string tfToken, AlertConditionId condId, out int firedBarIndex)
    {
        firedBarIndex = GetLastFiredBarIndex(tfToken, condId);
        return firedBarIndex >= 0;
    }

    public bool TryBuildSourceEventCandidate(
        string tfToken,
        int barIndex,
        out CompoundSourceEventCandidate candidate)
    {
        candidate = null!;
        if (!_entries.TryGetValue(tfToken, out var entry))
            return false;

        if (barIndex < 0 || barIndex >= entry.TfBars.Count)
            return false;

        var openLocal = ChartTimeFromBars.NormalizeBarOpenTime(entry.TfBars.OpenTimes[barIndex]);
        candidate = new CompoundSourceEventCandidate
        {
            Symbol = _symbol,
            SourceTfToken = tfToken,
            SourceBarIndex = barIndex,
            SourceBarOpenTimeUtc = openLocal,
            SourceBarCloseTimeUtc = openLocal.Add(entry.TfPeriod),
            Open = entry.TfBars.OpenPrices[barIndex],
            High = entry.TfBars.HighPrices[barIndex],
            Low = entry.TfBars.LowPrices[barIndex],
            Close = entry.TfBars.ClosePrices[barIndex],
        };
        return true;
    }

    /// <summary>
    /// Refresh state condition caches for all dependent TFs at the source bar close snapshot.
    /// Uses partial HTF OHLC when the HTF bar has not fully closed at snapshot time.
    /// </summary>
    public void UpdateStateCachesAtSourceClose(
        CompoundSourceEventCandidate candidate,
        IReadOnlyDictionary<string, AlertConditionId[]> trackedConditionsPerTf,
        bool chartM15CloseEdgeInjected,
        bool realtimeChartEdge)
    {
        if (candidate == null)
            return;

        var snapshotTime = candidate.SourceBarCloseTimeUtc;
        _currentChartBarIndex = Math.Max(_currentChartBarIndex, 0);

        foreach (var kv in trackedConditionsPerTf)
        {
            var tfToken = kv.Key;
            var conds = kv.Value;
            if (conds.Length == 0 || !_entries.TryGetValue(tfToken, out var entry))
                continue;

            if (!TryResolveSnapshotBar(
                    entry, candidate, snapshotTime,
                    out var barIdx, out var isClosedBar, out var o, out var h, out var l, out var c,
                    out var touchLtfOpen))
                continue;

            if (!entry.IsChartTfEntry && !isClosedBar)
            {
                FeedFormingHtfBarAtSnapshot(entry, barIdx, snapshotTime, o, h, l, c, realtimeChartEdge);
            }

            entry.Shell.State.RealZone.OnRealtimeBarPrice(h, l);

            var barOpenRaw = entry.TfBars.OpenTimes[barIdx];
            UpdateConditionCacheForEntry(
                entry,
                barIdx,
                barOpenRaw,
                isBarClosed: isClosedBar,
                isRealtime: !isClosedBar,
                isNewBarOnRealtimeForming: false,
                chartM15CloseEdgeInjected,
                conds,
                reevalStateOnBarClose: true,
                evalChartCloseTime: snapshotTime,
                barOpenOverride: o,
                barHighOverride: h,
                barLowOverride: l,
                barCloseOverride: c,
                touchLtfOpenOverride: touchLtfOpen);
        }
    }

    public string FormatStateRefreshLine(string tfToken, AlertConditionId condId)
    {
        if (!IsStateConditionActive(tfToken, condId))
            return $"{tfToken}.{CompoundRuleParser.PineNameFor(condId)}=--";

        return $"{tfToken}.{CompoundRuleParser.PineNameFor(condId)}=OK";
    }

    // ===== Diagnostic counters (per-TF) =====
    readonly Dictionary<string, int> _diagFeedTotal = new(StringComparer.Ordinal);
    readonly Dictionary<string, int> _diagFeedLastIdx = new(StringComparer.Ordinal);
    readonly Dictionary<string, int> _diagFeedThisPass = new(StringComparer.Ordinal);
    readonly Dictionary<string, int> _diagEmitLastIdx = new(StringComparer.Ordinal);
    readonly Dictionary<string, DateTime> _diagEmitLastTime = new(StringComparer.Ordinal);
    readonly Dictionary<string, int> _diagEmitLastBuyHL = new(StringComparer.Ordinal);
    readonly Dictionary<string, int> _diagEmitLastSellHL = new(StringComparer.Ordinal);
    readonly Dictionary<string, int> _diagEmitLastBuyNG = new(StringComparer.Ordinal);
    readonly Dictionary<string, int> _diagEmitLastSellNG = new(StringComparer.Ordinal);
    readonly Dictionary<string, int> _diagEmitLastPivots = new(StringComparer.Ordinal);

    public void ResetDiagPassCounters()
    {
        var keys = new List<string>(_diagFeedThisPass.Keys);
        foreach (var k in keys) _diagFeedThisPass[k] = 0;
    }

    void RecordDiagFeed(string tf, int idx)
    {
        _diagFeedTotal[tf] = (_diagFeedTotal.TryGetValue(tf, out var t) ? t : 0) + 1;
        _diagFeedLastIdx[tf] = idx;
        _diagFeedThisPass[tf] = (_diagFeedThisPass.TryGetValue(tf, out var p) ? p : 0) + 1;
    }

    void RecordDiagEmit(string tf, int idx, DateTime t, int buyHL, int sellHL, int buyNG, int sellNG, int pivots)
    {
        _diagEmitLastIdx[tf] = idx;
        _diagEmitLastTime[tf] = t;
        _diagEmitLastBuyHL[tf] = buyHL;
        _diagEmitLastSellHL[tf] = sellHL;
        _diagEmitLastBuyNG[tf] = buyNG;
        _diagEmitLastSellNG[tf] = sellNG;
        _diagEmitLastPivots[tf] = pivots;
    }

    public struct DiagFeedEmit
    {
        public int FeedTotal;
        public int FeedLastIdx;
        public int FeedThisPass;
        public int EmitLastIdx;
        public DateTime EmitLastTime;
        public int EmitBuyHL, EmitSellHL, EmitBuyNG, EmitSellNG;
        public int EmitPivots;
    }

    public DiagFeedEmit GetDiagFeedEmit(string tfToken)
    {
        return new DiagFeedEmit
        {
            FeedTotal = _diagFeedTotal.TryGetValue(tfToken, out var ft) ? ft : 0,
            FeedLastIdx = _diagFeedLastIdx.TryGetValue(tfToken, out var fl) ? fl : -1,
            FeedThisPass = _diagFeedThisPass.TryGetValue(tfToken, out var fp) ? fp : 0,
            EmitLastIdx = _diagEmitLastIdx.TryGetValue(tfToken, out var el) ? el : -1,
            EmitLastTime = _diagEmitLastTime.TryGetValue(tfToken, out var et) ? et : DateTime.MinValue,
            EmitBuyHL = _diagEmitLastBuyHL.TryGetValue(tfToken, out var bh) ? bh : 0,
            EmitSellHL = _diagEmitLastSellHL.TryGetValue(tfToken, out var sh) ? sh : 0,
            EmitBuyNG = _diagEmitLastBuyNG.TryGetValue(tfToken, out var bn) ? bn : 0,
            EmitSellNG = _diagEmitLastSellNG.TryGetValue(tfToken, out var sn) ? sn : 0,
            EmitPivots = _diagEmitLastPivots.TryGetValue(tfToken, out var p) ? p : 0,
        };
    }

    // ===== Diagnostic accessors =====
    public bool TryGetDiagInfo(string tfToken, out MtfDiagInfo info)
    {
        info = default;
        if (!_entries.TryGetValue(tfToken, out var entry)) return false;
        info = new MtfDiagInfo
        {
            TfToken = entry.TfToken,
            IsChartTfEntry = entry.IsChartTfEntry,
            TfBarsCount = entry.TfBars.Count,
            TfBarsLastOpenTime = entry.TfBars.Count > 0
                ? ChartTimeFromBars.NormalizeBarOpenTime(entry.TfBars.OpenTimes[entry.TfBars.Count - 1])
                : DateTime.MinValue,
            NextBarIdxToProcess = entry.NextBarIdxToProcess,
            AlertTrackEmitThroughIdx = entry.AlertTrackEmitThroughIdx,
            LastEvaluatedBarIndex = entry.LastEvaluatedBarIndex,
            ShellStateLastEvalBar = entry.Shell.State.LastEvaluatedBarIndex,
            PivotsCount = entry.Shell.State.Pivots.Count,
            DedupABuy = entry.Shell.EventDedup?.TriggeredA_Buy_M5.Count ?? 0,
            DedupASell = entry.Shell.EventDedup?.TriggeredA_Sell_M5.Count ?? 0,
            DedupBBuy = entry.Shell.EventDedup?.TriggeredB_Buy_M5.Count ?? 0,
            DedupBSell = entry.Shell.EventDedup?.TriggeredB_Sell_M5.Count ?? 0,
            DedupCBuy = entry.Shell.EventDedup?.TriggeredC_Buy_M5.Count ?? 0,
            DedupCSell = entry.Shell.EventDedup?.TriggeredC_Sell_M5.Count ?? 0,
            AtrWilder14   = entry.Shell.State.DiagLastAtrWilder14,
            AtrKeylevel   = entry.Shell.State.DiagLastAtrKeylevel,
            AvgBody       = entry.Shell.State.DiagLastAvgBody,
            RawBody1      = entry.Shell.State.DiagLastRawBody1,
            RawRange1     = entry.Shell.State.DiagLastRawRange1,
            RawIsDoji1    = entry.Shell.State.DiagLastRawIsDoji1,
            RawClearBody1 = entry.Shell.State.DiagLastRawClearBody1,
            EffBody       = entry.Shell.State.DiagLastEffBody,
            EffIsDoji     = entry.Shell.State.DiagLastEffIsDoji,
            EffClearBody  = entry.Shell.State.DiagLastEffClearBody,
            AtrBarsSeen14 = entry.Shell.State.DiagAtrBarsSeen14,
            Ohlc1Open  = entry.Shell.State.DiagLastOhlc1Open,
            Ohlc1High  = entry.Shell.State.DiagLastOhlc1High,
            Ohlc1Low   = entry.Shell.State.DiagLastOhlc1Low,
            Ohlc1Close = entry.Shell.State.DiagLastOhlc1Close,
        };
        return true;
    }

    public bool TryGetConditionHit(string tfToken, AlertConditionId condId, out MtfConditionHit hit)
    {
        hit = default;
        if (!_entries.TryGetValue(tfToken, out var entry)) return false;
        return entry.ConditionCache.TryGetValue(condId, out hit);
    }

    /// <summary>Diagnostic-only: get the raw cTrader Bars reference for inspecting live OHLC.</summary>
    public Bars? GetTfBarsForDiag(string tfToken)
    {
        return _entries.TryGetValue(tfToken, out var entry) ? entry.TfBars : null;
    }

    /// <summary>For diagnostic only: count of pivots by flag state in the M5 shell.</summary>
    public bool TryGetPivotFlagDistribution(string tfToken, out int active, out int pending, out int broken, out int mainBroken)
    {
        active = pending = broken = mainBroken = 0;
        if (!_entries.TryGetValue(tfToken, out var entry)) return false;
        var pivs = entry.Shell.State.Pivots;
        for (var i = 0; i < pivs.Count; i++)
        {
            var f = pivs.GetFlag(i);
            if (f == 0 || f == 1) active++;
            else if (f == 3 || f == 7 || f == 50) pending++;
            else if (f == 2) broken++;
            else if (f == -1) mainBroken++;
        }
        return true;
    }

    void UpdateConditionCacheForEntry(
        MtfTfEntry entry,
        int sourceBarIndex,
        DateTime sourceBarOpenTimeRaw,
        bool isBarClosed,
        bool isRealtime,
        bool isNewBarOnRealtimeForming,
        bool chartM15CloseEdgeInjected,
        IReadOnlyList<AlertConditionId> conditions,
        bool reevalStateOnBarClose = true,
        DateTime? evalChartCloseTime = null,
        double? barOpenOverride = null,
        double? barHighOverride = null,
        double? barLowOverride = null,
        double? barCloseOverride = null,
        double? touchLtfOpenOverride = null)
    {
        if (conditions.Count == 0)
            return;

        // Pass A — STATE (Touch/Real): mọi tick realtime trên bar đang hình thành.
        if (isRealtime && !isBarClosed)
        {
            ApplyConditionCachePass(
                entry,
                sourceBarIndex,
                sourceBarOpenTimeRaw,
                isBarClosed: false,
                isRealtime: true,
                isNewBarOnRealtimeForming: false,
                chartM15CloseEdgeInjected,
                conditions,
                passIsBarCloseEval: false,
                passIsRealtimeStateEval: true,
                evalChartCloseTime,
                out _,
                barOpenOverride: barOpenOverride,
                barHighOverride: barHighOverride,
                barLowOverride: barLowOverride,
                barCloseOverride: barCloseOverride,
                touchLtfProbeOverride: touchLtfOpenOverride);
        }

        // Pass B — bar close: backtest / HTF feed / nến chart vừa đóng.
        // When intra-bar OHLC sim already primed STATE (BacktestBarClose), skip re-evaluating
        // STATE at close so a touch during [Low..High] is not wiped by close-only price.
        if (isBarClosed)
        {
            ApplyConditionCachePass(
                entry,
                sourceBarIndex,
                sourceBarOpenTimeRaw,
                isBarClosed: true,
                isRealtime: false,
                isNewBarOnRealtimeForming: false,
                chartM15CloseEdgeInjected,
                conditions,
                passIsBarCloseEval: true,
                passIsRealtimeStateEval: reevalStateOnBarClose,
                evalChartCloseTime,
                out var eventFireTouch,
                barOpenOverride: barOpenOverride,
                barHighOverride: barHighOverride,
                barLowOverride: barLowOverride,
                barCloseOverride: barCloseOverride,
                touchLtfProbeOverride: touchLtfOpenOverride);

            // Pass D — khi M5 event hoặc NG M15 fire: cập nhật touch H1/H4 bằng M5 high/low tại bar fire.
            if (eventFireTouch.HasValue)
                ApplyEventFireHtfTouchPass(eventFireTouch.Value, chartM15CloseEdgeInjected);
        }
        else if (isRealtime && isNewBarOnRealtimeForming && sourceBarIndex > 0)
        {
            // Pass C — realtime cạnh nến mới: EVENT/PKL trên nến vừa đóng (index-1).
            var closedIdx = sourceBarIndex - 1;
            ApplyConditionCachePass(
                entry,
                closedIdx,
                entry.TfBars.OpenTimes[closedIdx],
                isBarClosed: true,
                isRealtime: false,
                isNewBarOnRealtimeForming: false,
                chartM15CloseEdgeInjected,
                conditions,
                passIsBarCloseEval: true,
                passIsRealtimeStateEval: false,
                evalChartCloseTime: null,
                out _);
        }
        else if (isRealtime && !isBarClosed)
        {
            ApplyConditionCachePass(
                entry,
                sourceBarIndex,
                sourceBarOpenTimeRaw,
                isBarClosed: false,
                isRealtime: true,
                isNewBarOnRealtimeForming: false,
                chartM15CloseEdgeInjected,
                conditions,
                passIsBarCloseEval: false,
                passIsRealtimeStateEval: true,
                evalChartCloseTime,
                out _,
                barOpenOverride: barOpenOverride,
                barHighOverride: barHighOverride,
                barLowOverride: barLowOverride,
                barCloseOverride: barCloseOverride,
                touchLtfProbeOverride: touchLtfOpenOverride);
        }
    }

    void ApplyConditionCachePass(
        MtfTfEntry entry,
        int sourceBarIndex,
        DateTime sourceBarOpenTimeRaw,
        bool isBarClosed,
        bool isRealtime,
        bool isNewBarOnRealtimeForming,
        bool chartM15CloseEdgeInjected,
        IReadOnlyList<AlertConditionId> conditions,
        bool passIsBarCloseEval,
        bool passIsRealtimeStateEval,
        DateTime? evalChartCloseTime,
        out EventFireTouchProbe? eventFireTouch,
        bool passIsEventFireTouchEval = false,
        double? touchLtfProbeOverride = null,
        string? touchLtfProbeKindOverride = null,
        double? barOpenOverride = null,
        double? barHighOverride = null,
        double? barLowOverride = null,
        double? barCloseOverride = null)
    {
        eventFireTouch = null;
        var barOpenLocal = ChartTimeFromBars.NormalizeBarOpenTime(sourceBarOpenTimeRaw);
        var barOpen = barOpenOverride ?? entry.TfBars.OpenPrices[sourceBarIndex];
        var barHigh = barHighOverride ?? entry.TfBars.HighPrices[sourceBarIndex];
        var barLow = barLowOverride ?? entry.TfBars.LowPrices[sourceBarIndex];
        var barClose = barCloseOverride ?? entry.TfBars.ClosePrices[sourceBarIndex];
        double? touchLtfProbe;
        string? touchLtfProbeKind;
        if (touchLtfProbeOverride.HasValue)
        {
            touchLtfProbe = touchLtfProbeOverride;
            touchLtfProbeKind = touchLtfProbeKindOverride;
        }
        else
        {
            touchLtfProbe = ResolveTouchLtfOpenForBacktest(
                entry.TfToken, isBarClosed, isRealtime, barOpenLocal, entry.TfPeriod, evalChartCloseTime);
            touchLtfProbeKind = touchLtfProbe.HasValue ? "ltfOpenM5" : null;
        }

        AlertEvaluationContext ctx;
        if (entry.IsChartTfEntry)
        {
            ctx = Loop6EvaluationContextFactory.Build(
                entry.Shell,
                sourceBarIndex,
                entry.TfToken,
                barOpenLocal,
                barClose,
                barHigh,
                barLow,
                isBarClosed,
                isRealtime,
                isNewBarOnRealtimeForming,
                chartM15CloseEdgeInjected,
                touchLtfOpenPrice: touchLtfProbe,
                touchLtfProbeKind: touchLtfProbeKind,
                alertTrackingDebug: _alertTrackingDebug,
                eventDetectionCache: entry.Shell.EventDetectionCache,
                realZoneDebug: _realZoneDebug);
        }
        else
        {
            ctx = BuildHtfEvaluationContext(
                entry,
                sourceBarIndex,
                barOpenLocal,
                barClose,
                barHigh,
                barLow,
                isBarClosed,
                isRealtime,
                isNewBarOnRealtimeForming,
                chartM15CloseEdgeInjected,
                touchLtfProbe,
                touchLtfProbeKind);
        }

        foreach (var condId in conditions)
        {
            if (!_engine.Registry.TryGetValue(condId, out var def))
                continue;

            if (!AlertBarTiming.ShouldEvaluateOnPass(
                    def.TimingClass, passIsBarCloseEval, passIsRealtimeStateEval, passIsEventFireTouchEval))
                continue;

            var result = _engine.Evaluate(condId, in ctx);
            if (!entry.ConditionCache.TryGetValue(condId, out var hit))
                hit = new MtfConditionHit { LastFiredBarIndex = -1, LastFiredChartBarIndex = -1 };

            if (CompoundRuleParser.IsStateCondition(condId))
            {
                hit.IsCurrentlyActive = result.Fired;
                if (condId is AlertConditionId.CanBuyTouchM5 or AlertConditionId.CanSellTouchM5)
                {
                    var (_, probe, kind) = TouchRealEvaluators.ResolveZoneTouchT(in ctx, condId);
                    hit.LastTouchProbePrice = probe;
                    hit.LastTouchProbeKind = kind;
                }
            }
            else if (result.Fired)
            {
                hit.LastFiredBarIndex = sourceBarIndex;
                hit.LastFiredBarOpenTime = barOpenLocal;
                if (!entry.IsChartTfEntry)
                    hit.LastFiredChartBarIndex = _currentChartBarIndex;

                if (passIsBarCloseEval
                    && TryMapEventFireTouchProbe(
                        condId, entry, sourceBarIndex, evalChartCloseTime,
                        out var probePrice, out var probeKind, out var touchCondId))
                {
                    eventFireTouch = new EventFireTouchProbe
                    {
                        ProbePrice = probePrice,
                        ProbeKind = probeKind,
                        TouchCondId = touchCondId,
                    };
                }
            }

            entry.ConditionCache[condId] = hit;
        }

        // EVENT sync index — only bar-close pass; realtime STATE pass must not overwrite with forming bar.
        if (passIsBarCloseEval)
        {
            entry.LastEvaluatedBarIndex = sourceBarIndex;
            if (!entry.IsChartTfEntry)
                entry.Shell.State.UpdateKeyExtPrevAfterAlertEval(sourceBarIndex);
        }
    }

    /// <summary>Pass D: sau M5/M15 event fire, re-eval touch H1/H4 với M5 high/low probe.
    /// Also re-evals the paired CanBuyReal or CanSellReal so the opposite-direction touch check
    /// in ComputeRealFlagsDetail uses the same event-fire probe (M5 high for buy, M5 low for sell).</summary>
    void ApplyEventFireHtfTouchPass(EventFireTouchProbe probe, bool chartM15CloseEdgeInjected)
    {
        // Pair the event-fire touch probe with the corresponding Real condition so they share the same probe price.
        //   Buy event (M5 high) → re-eval CanBuyTouchM5 + CanBuyReal (canBuyReal opposite uses M5 high probe).
        //   Sell event (M5 low) → re-eval CanSellTouchM5 + CanSellReal (canSellReal opposite uses M5 low probe).
        var pairedRealCondId = probe.TouchCondId == AlertConditionId.CanBuyTouchM5
            ? AlertConditionId.CanBuyReal
            : AlertConditionId.CanSellReal;

        foreach (var htfEntry in _entries.Values)
        {
            if (htfEntry.IsChartTfEntry)
                continue;
            if (!TryParseTfMinutes(htfEntry.TfToken, out var evalMinutes) || evalMinutes <= 5)
                continue;
            var hasTouchCond = htfEntry.ConditionCache.ContainsKey(probe.TouchCondId);
            var hasRealCond  = htfEntry.ConditionCache.ContainsKey(pairedRealCondId);
            if (!hasTouchCond && !hasRealCond)
                continue;
            if (htfEntry.TfBars.Count == 0 || htfEntry.NextBarIdxToProcess == 0)
                continue;

            var barIdx = htfEntry.TfBars.Count - 1;
            var barOpenRaw = htfEntry.TfBars.OpenTimes[barIdx];
            var barOpenLocal = ChartTimeFromBars.NormalizeBarOpenTime(barOpenRaw);
            var barHigh = htfEntry.TfBars.HighPrices.LastValue;
            var barLow = htfEntry.TfBars.LowPrices.LastValue;
            var barClose = htfEntry.TfBars.ClosePrices.LastValue;

            // Build condition list: include whichever of touch / real are registered for this HTF entry.
            var passDConds = (hasTouchCond, hasRealCond) switch
            {
                (true, true)  => new[] { probe.TouchCondId, pairedRealCondId },
                (true, false) => new[] { probe.TouchCondId },
                _             => new[] { pairedRealCondId },
            };

            ApplyConditionCachePass(
                htfEntry,
                barIdx,
                barOpenRaw,
                isBarClosed: false,
                isRealtime: false,
                isNewBarOnRealtimeForming: false,
                chartM15CloseEdgeInjected,
                passDConds,
                passIsBarCloseEval: false,
                passIsRealtimeStateEval: false,
                evalChartCloseTime: null,
                out _,
                passIsEventFireTouchEval: true,
                touchLtfProbeOverride: probe.ProbePrice,
                touchLtfProbeKindOverride: probe.ProbeKind);
        }
    }

    bool TryMapEventFireTouchProbe(
        AlertConditionId firedCondId,
        MtfTfEntry entry,
        int sourceBarIndex,
        DateTime? evalChartCloseTime,
        out double probePrice,
        out string probeKind,
        out AlertConditionId touchCondId)
    {
        probePrice = 0;
        probeKind = "";
        touchCondId = default;

        switch (firedCondId)
        {
            case AlertConditionId.CondBuyEventM5 when entry.TfToken == "5":
                probePrice = entry.TfBars.HighPrices[sourceBarIndex];
                probeKind = "ltfHighM5";
                touchCondId = AlertConditionId.CanBuyTouchM5;
                return true;

            case AlertConditionId.CondSellEventM5 when entry.TfToken == "5":
                probePrice = entry.TfBars.LowPrices[sourceBarIndex];
                probeKind = "ltfLowM5";
                touchCondId = AlertConditionId.CanSellTouchM5;
                return true;

            case AlertConditionId.CondBuyEventNGM15
                or AlertConditionId.CondBuyEventHLNGM15 when entry.TfToken == "15":
                if (!TryResolveLtfBarExtreme(
                        "5",
                        entry.TfBars.OpenTimes[sourceBarIndex],
                        entry.TfPeriod,
                        useHigh: true,
                        out probePrice,
                        evalChartCloseTime))
                    return false;
                probeKind = "ltfHighM5";
                touchCondId = AlertConditionId.CanBuyTouchM5;
                return true;

            case AlertConditionId.CondSellEventNGM15
                or AlertConditionId.CondSellEventHLNGM15 when entry.TfToken == "15":
                if (!TryResolveLtfBarExtreme(
                        "5",
                        entry.TfBars.OpenTimes[sourceBarIndex],
                        entry.TfPeriod,
                        useHigh: false,
                        out probePrice,
                        evalChartCloseTime))
                    return false;
                probeKind = "ltfLowM5";
                touchCondId = AlertConditionId.CanSellTouchM5;
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// M5 high/low của nến LTF cuối cùng trong [htfOpen, htfClose) trước <paramref name="findLastBefore"/>.
    /// </summary>
    bool TryResolveLtfBarExtreme(
        string ltfToken,
        DateTime htfBarOpenRaw,
        TimeSpan htfPeriod,
        bool useHigh,
        out double extremePrice,
        DateTime? findLastBefore = null)
    {
        extremePrice = 0;
        if (!_entries.TryGetValue(ltfToken, out var ltfEntry) || ltfEntry.TfBars.Count == 0)
            return false;

        var htfOpen = ChartTimeFromBars.NormalizeBarOpenTime(htfBarOpenRaw);
        var htfClose = htfOpen.Add(htfPeriod);
        var bars = ltfEntry.TfBars;
        var lastIdx = -1;

        for (var i = 0; i < bars.Count; i++)
        {
            var ot = ChartTimeFromBars.NormalizeBarOpenTime(bars.OpenTimes[i]);
            if (ot < htfOpen)
                continue;
            if (ot >= htfClose)
                break;
            if (findLastBefore.HasValue && ot >= findLastBefore.Value)
                break;

            lastIdx = i;
        }

        if (lastIdx < 0)
            return false;

        extremePrice = useHigh ? bars.HighPrices[lastIdx] : bars.LowPrices[lastIdx];
        return extremePrice > 0;
    }

    /// <summary>
    /// Feed HTF forming bar with full cTrader platform OHLC — same source as native HTF chart shell.
    /// </summary>
    bool FeedFormingHtfBarPlatformParity(MtfTfEntry entry, int formingIdx, bool useLiveFormingFlags)
    {
        if (entry.LastFormingSyncBarIdx == formingIdx && entry.LastFormingSyncFullPlatform)
            return true;

        var open = entry.TfBars.OpenPrices[formingIdx];
        var high = entry.TfBars.HighPrices[formingIdx];
        var low = entry.TfBars.LowPrices[formingIdx];
        var close = entry.TfBars.ClosePrices[formingIdx];
        if (high <= 0 || low <= 0 || open <= 0 || close <= 0 || high < low)
            return false;

        var isNewBar = entry.LastFormingSyncBarIdx != formingIdx;
        var flags = useLiveFormingFlags
            ? Loop6BarRuntimeMapper.ForLiveRealtimeFormingLastBar(isNewBar)
            : Loop6BarRuntimeMapper.ForBacktestFormingLastBar(isNewBar);

        var shell = entry.Shell;
        var barSnap = BarsToCoreAdapter.ToBarSnapshot(
            entry.TfBars.OpenTimes[formingIdx],
            open,
            high,
            low,
            close,
            (long)entry.TfBars.TickVolumes[formingIdx]);

        if (!shell.Series.BackfillCompleted)
            shell.Series.InitialBackfill(-1, _ => null);

        shell.Series.OnCalculateBar(formingIdx, barSnap, flags);
        shell.State.TickSize = _tickSize;
        ApplyTfModeFlags(shell, entry.TfToken);
        shell.State.OnBar(shell.Series.Buffer, formingIdx, shell.Render, ltfSnapshotForClosedHtfBar: null);
        shell.Render.ClearCommands();

        entry.LastFormingSyncBarIdx = formingIdx;
        entry.LastFormingSyncClip = default;
        entry.LastFormingSyncFullPlatform = true;
        entry.LastFormingPartialHigh = high;
        entry.LastFormingPartialLow = low;
        entry.LastFormingPartialClose = close;
        return true;
    }

    void FeedClosedHtfBar(MtfTfEntry entry, int barIndex)
    {
        entry.LastFormingSyncBarIdx = -1;
        entry.LastFormingSyncClip = default;
        entry.LastFormingSyncFullPlatform = false;
        entry.LastFormingPartialHigh = 0;
        entry.LastFormingPartialLow = 0;
        entry.LastFormingPartialClose = 0;

        var shell = entry.Shell;
        var barSnap = BarsToCoreAdapter.ToBarSnapshot(
            entry.TfBars.OpenTimes[barIndex],
            entry.TfBars.OpenPrices[barIndex],
            entry.TfBars.HighPrices[barIndex],
            entry.TfBars.LowPrices[barIndex],
            entry.TfBars.ClosePrices[barIndex],
            (long)entry.TfBars.TickVolumes[barIndex]);

        var lastIdx = entry.TfBars.Count - 1;
        var flags = Loop6BarRuntimeMapper.ForHistoricalBackfillRow(barIndex, lastIdx);

        if (!shell.Series.BackfillCompleted)
            shell.Series.InitialBackfill(-1, _ => null);

        shell.Series.OnCalculateBar(barIndex, barSnap, flags);
        shell.State.TickSize = _tickSize;
        ApplyTfModeFlags(shell, entry.TfToken);

        LtfBarBundle? ltfSnap = null;
        if (entry.LtfCollector != null && _marketData != null)
            ltfSnap = entry.LtfCollector.CollectForClosedHtfBarWithRebind(_marketData, entry.TfBars, barIndex);

        shell.State.OnBar(shell.Series.Buffer, barIndex, shell.Render, ltfSnapshotForClosedHtfBar: ltfSnap);
        shell.Render.ClearCommands();
    }

    AlertEvaluationContext BuildHtfEvaluationContext(
        MtfTfEntry entry,
        int sourceBarIndex,
        DateTime barOpenLocal,
        double barClose,
        double barHigh,
        double barLow,
        bool isBarClosed,
        bool isRealtime,
        bool isNewBarOnRealtimeForming,
        bool chartM15CloseEdgeInjected,
        double? touchLtfOpenPrice,
        string? touchLtfProbeKind = null)
    {
        var shell = entry.Shell;
        var tfTok = entry.TfToken;
        var zoneState = shell.State.BuildZoneState(barClose);
        var realtimeFilter = shell.State.BuildRealtimeFilterState(barHigh, barLow, zoneState);
        var pivotEntries = shell.State.BuildPivotEntries(sourceBarIndex);
        var minute = barOpenLocal.Minute;
        var m15Edge = AlertBarTiming.ComputeM15CloseEdge(
            tfTok,
            isBarClosed,
            isRealtime,
            isNewBarOnRealtimeForming,
            minute,
            chartM15CloseEdgeInjected);

        return new AlertEvaluationContext
        {
            Symbol = _symbol,
            ChartTimeframeToken = tfTok,
            SourceBarIndex = sourceBarIndex,
            PineBarIndex = sourceBarIndex,
            SourceBarOpenTimeChartLocal = barOpenLocal,
            SourceBarHigh = barHigh,
            SourceBarLow = barLow,
            IsCurrentBar = !isBarClosed,
            IsBarClosed = isBarClosed,
            IsRealtime = isRealtime,
            EvaluationOffset = isBarClosed ? 1 : 0,
            M15CloseEdgeInjected = m15Edge,

            EventRawDetectionAvailable = true,
            ActiveZoneCollectionAvailable = true,
            RealtimeFilterStateAvailable = true,

            PivotEntries = pivotEntries,
            EventDedup = shell.EventDedup,
            IsM5JustClosed = AlertBarTiming.IsM5JustClosedGate(tfTok, isBarClosed, isNewBarOnRealtimeForming),
            IsM15JustClosed = AlertBarTiming.IsM15JustClosedGate(
                tfTok, isBarClosed, isNewBarOnRealtimeForming, minute),
            ZoneState = zoneState,
            RealtimeFilter = realtimeFilter,
            TouchLtfOpenPrice = touchLtfOpenPrice,
            TouchLtfProbeKind = touchLtfProbeKind,
            AlertTrackingDebug = _alertTrackingDebug,
            EventDetectionCache = shell.EventDetectionCache,
            RealZoneDebug = _realZoneDebug,
        };
    }

    void MaybeEmitEventTracking(in AlertEvaluationContext ctx)
    {
        if (!_alertTrackingDebug || _alertTrackingDraw == null || ctx.EventDetectionCache == null)
            return;

        if (ctx.IsM5JustClosed)
        {
            EventFamilyEvaluators.ResolveRawCounts(in ctx, isM15: false, alertTrackingDebug: true, ctx.EventDetectionCache);
            ctx.EventDetectionCache.TryEmitTracking(
                ctx.SourceBarIndex, ctx.ChartTimeframeToken, isM15: false, _alertTrackingDraw);
        }
    }

    /// <summary>
    /// Backtest HTF: open của nến LTF trong khoảng [htfOpen, htfClose).
    /// Khi <paramref name="findLastBefore"/> được cung cấp, trả về open của nến LTF
    /// <b>cuối cùng</b> có openTime &lt; <paramref name="findLastBefore"/> (probe gần nhất với
    /// thời điểm eval). Khi null, trả về nến LTF <b>đầu tiên</b> (hành vi cũ).
    /// </summary>
    public bool TryResolveLtfBarOpen(
        string ltfToken,
        DateTime htfBarOpenRaw,
        TimeSpan htfPeriod,
        out double openPrice,
        DateTime? findLastBefore = null)
    {
        openPrice = 0;
        if (!_entries.TryGetValue(ltfToken, out var ltfEntry) || ltfEntry.TfBars.Count == 0)
            return false;

        var htfOpen = ChartTimeFromBars.NormalizeBarOpenTime(htfBarOpenRaw);
        var htfClose = htfOpen.Add(htfPeriod);
        var bars = ltfEntry.TfBars;

        for (var i = 0; i < bars.Count; i++)
        {
            var ot = ChartTimeFromBars.NormalizeBarOpenTime(bars.OpenTimes[i]);
            if (ot < htfOpen)
                continue;
            if (ot >= htfClose)
                break;
            if (findLastBefore.HasValue && ot >= findLastBefore.Value)
                break;

            openPrice = bars.OpenPrices[i];
            if (!findLastBefore.HasValue)
                return true; // original: return first match immediately
            // else: keep scanning to find the LAST valid bar before findLastBefore
        }

        return openPrice != 0;
    }

    /// <summary>Public wrapper for single-alert host on HTF chart backtest.</summary>
    public double? ResolveTouchLtfOpenForBacktestPublic(
        string evalTfToken,
        DateTime barOpenLocal,
        TimeSpan tfPeriod,
        DateTime? evalChartCloseTime = null) =>
        ResolveTouchLtfOpenForBacktest(evalTfToken, isBarClosed: true, isRealtime: false, barOpenLocal, tfPeriod, evalChartCloseTime);

    double? ResolveTouchLtfOpenForBacktest(
        string evalTfToken,
        bool isBarClosed,
        bool isRealtime,
        DateTime barOpenLocal,
        TimeSpan tfPeriod,
        DateTime? evalChartCloseTime = null)
    {
        if (!isBarClosed || isRealtime)
            return null;

        if (!TryParseTfMinutes(evalTfToken, out var evalMinutes) || evalMinutes <= 5)
            return null;

        return TryResolveLtfBarOpen("5", barOpenLocal, tfPeriod, out var m5Open, evalChartCloseTime)
            ? m5Open
            : null;
    }

    static bool TryParseTfMinutes(string tfToken, out int minutes)
    {
        if (int.TryParse(tfToken, out minutes))
            return true;

        return tfToken switch
        {
            "60" => Assign(60, out minutes),
            "240" => Assign(240, out minutes),
            "1440" => Assign(1440, out minutes),
            _ => false,
        };

        static bool Assign(int value, out int target)
        {
            target = value;
            return true;
        }
    }

    string? ResolveSymbolName(string? symbolName) =>
        !string.IsNullOrWhiteSpace(symbolName) ? symbolName.Trim()
        : !string.IsNullOrWhiteSpace(_symbol) ? _symbol
        : null;

    static void ApplyTfModeFlags(TradeAlertIndicator shell, string tfToken)
    {
        shell.State.IsM5Mode = tfToken == "5";
        shell.State.IsH4Chart = tfToken == "240";
    }

    /// <summary>
    /// Realtime: never feed <c>TfBars.Count-1</c> (forming). Its scheduled close can be &lt;= chart HTF close
    /// while OHLC is still incomplete — feeding early skips the real bar-close eval.
    /// </summary>
    internal static bool CanFeedClosedHtfBar(
        MtfTfEntry entry,
        int idx,
        DateTime chartBarCloseTimeChartLocal,
        bool realtimeChartEdge)
    {
        if (idx < 0 || idx >= entry.TfBars.Count)
            return false;

        var openLocal = ChartTimeFromBars.NormalizeBarOpenTime(entry.TfBars.OpenTimes[idx]);
        var closeLocal = openLocal.Add(entry.TfPeriod);
        if (closeLocal > chartBarCloseTimeChartLocal)
            return false;

        if (realtimeChartEdge && idx >= entry.TfBars.Count - 1)
            return false;

        // Only reject mathematically-invalid OHLC (default-init or corrupted).
        // DO NOT use strict `high <= low`: exotic low-liquidity pairs (e.g. EURNOK) can have
        // legitimate flat M5 bars where H==L==O==C, and rejecting them would `break` the feed
        // loop and freeze all subsequent bars permanently. False-positive pivot breaks on flat
        // bars are guarded separately inside PineStateEngine (rawClearBody/effClearBody require
        // rawBody > 0 && avgBody > 0).
        var high  = entry.TfBars.HighPrices[idx];
        var low   = entry.TfBars.LowPrices[idx];
        var open  = entry.TfBars.OpenPrices[idx];
        var close = entry.TfBars.ClosePrices[idx];
        if (high <= 0 || low <= 0 || open <= 0 || close <= 0)
            return false;
        if (high < low)
            return false;

        return true;
    }

    static int CurrentBarIndexForSync(MtfTfEntry entry)
    {
        if (entry.LastEvaluatedBarIndex >= 0)
            return entry.LastEvaluatedBarIndex;

        if (!entry.IsChartTfEntry && entry.NextBarIdxToProcess > 0)
            return entry.NextBarIdxToProcess - 1;

        return entry.TfBars.Count > 0 ? entry.TfBars.Count - 1 : -1;
    }

    /// <summary>
    /// Multi-line FireDbg table: M5 last closed bar + chart TF + H1/H4 zone lists + cached M5 probes.
    /// </summary>
    public string? DescribeHtfTouchProbeTableFireDebug(
        string chartTfToken,
        int chartBarIndex,
        bool isBarClosed,
        bool isRealtime,
        bool isNewBarOnRealtimeForming,
        bool chartM15CloseEdgeInjected,
        DateTime? chartBarCloseTime = null,
        double? chartReplayHigh = null,
        double? chartReplayLow = null,
        double? chartReplayClose = null)
    {
        var sb = new System.Text.StringBuilder();

        TryAppendM5ClosedBarSection(sb);

        var anySection = false;
        if (!string.IsNullOrWhiteSpace(chartTfToken))
        {
            anySection |= TryAppendHtfTouchTableSection(
                sb, chartTfToken, FormatTfDebugLabel(chartTfToken), chartTfToken, chartBarIndex, isBarClosed, isRealtime,
                isNewBarOnRealtimeForming, chartM15CloseEdgeInjected, chartBarCloseTime,
                chartReplayHigh, chartReplayLow, chartReplayClose);
        }

        if (!string.Equals(chartTfToken, "60", StringComparison.Ordinal))
        {
            anySection |= TryAppendHtfTouchTableSection(
                sb, "60", "H1", chartTfToken, chartBarIndex, isBarClosed, isRealtime,
                isNewBarOnRealtimeForming, chartM15CloseEdgeInjected, chartBarCloseTime,
                chartReplayHigh, chartReplayLow, chartReplayClose);
        }

        if (!string.Equals(chartTfToken, "240", StringComparison.Ordinal))
        {
            anySection |= TryAppendHtfTouchTableSection(
                sb, "240", "H4", chartTfToken, chartBarIndex, isBarClosed, isRealtime,
                isNewBarOnRealtimeForming, chartM15CloseEdgeInjected, chartBarCloseTime,
                chartReplayHigh, chartReplayLow, chartReplayClose);
        }

        return anySection || sb.Length > 0 ? sb.ToString().TrimEnd() : null;
    }

    internal static string FormatTfDebugLabel(string tfToken) =>
        tfToken switch
        {
            "1" => "M1",
            "5" => "M5",
            "15" => "M15",
            "60" => "H1",
            "240" => "H4",
            "1440" => "D1",
            _ => tfToken.StartsWith("M", StringComparison.OrdinalIgnoreCase) ? tfToken : $"TF{tfToken}",
        };

    bool TryAppendM5ClosedBarSection(StringBuilder sb)
    {
        if (!_entries.TryGetValue("5", out var m5Entry) || m5Entry.NextBarIdxToProcess <= 0)
            return false;

        var barIdx = m5Entry.NextBarIdxToProcess - 1;
        if (barIdx < 0 || barIdx >= m5Entry.TfBars.Count)
            return false;

        ZoneStateDebugFormatter.AppendM5BarTable(
            sb,
            barIdx,
            ChartTimeFromBars.NormalizeBarOpenTime(m5Entry.TfBars.OpenTimes[barIdx]),
            m5Entry.TfBars.OpenPrices[barIdx],
            m5Entry.TfBars.HighPrices[barIdx],
            m5Entry.TfBars.LowPrices[barIdx],
            m5Entry.TfBars.ClosePrices[barIdx]);
        return true;
    }

    bool TryAppendHtfTouchTableSection(
        StringBuilder sb,
        string tfToken,
        string tfLabel,
        string chartTfToken,
        int chartBarIndex,
        bool isBarClosed,
        bool isRealtime,
        bool isNewBarOnRealtimeForming,
        bool chartM15CloseEdgeInjected,
        DateTime? chartBarCloseTime,
        double? chartReplayHigh,
        double? chartReplayLow,
        double? chartReplayClose)
    {
        if (!_entries.TryGetValue(tfToken, out var entry))
            return false;

        var barIdx = ResolveHtfDebugBarIndex(entry, chartBarIndex, useFormingBar: true);
        if (barIdx < 0 || barIdx >= entry.TfBars.Count)
            return false;

        var barOpenLocal = ChartTimeFromBars.NormalizeBarOpenTime(entry.TfBars.OpenTimes[barIdx]);
        var isFormingBar = !entry.IsChartTfEntry && barIdx == entry.TfBars.Count - 1
            && entry.NextBarIdxToProcess > 0;
        var usesPartialOhlc = !entry.IsChartTfEntry
            && entry.LastFormingSyncBarIdx == barIdx
            && entry.HasFormingShellSync;
        var barHigh = entry.IsChartTfEntry && chartReplayHigh.HasValue
            ? chartReplayHigh.Value
            : usesPartialOhlc
                ? entry.LastFormingPartialHigh
                : isFormingBar ? entry.TfBars.HighPrices.LastValue : entry.TfBars.HighPrices[barIdx];
        var barLow = entry.IsChartTfEntry && chartReplayLow.HasValue
            ? chartReplayLow.Value
            : usesPartialOhlc
                ? entry.LastFormingPartialLow
                : isFormingBar ? entry.TfBars.LowPrices.LastValue : entry.TfBars.LowPrices[barIdx];

        var usesChartReplayClose = chartReplayClose.HasValue && !entry.IsChartTfEntry && !usesPartialOhlc;
        var barClose = entry.IsChartTfEntry && chartReplayClose.HasValue
            ? chartReplayClose.Value
            : usesPartialOhlc
                ? entry.LastFormingPartialClose
                : usesChartReplayClose
                    ? chartReplayClose!.Value
                    : isFormingBar ? entry.TfBars.ClosePrices.LastValue : entry.TfBars.ClosePrices[barIdx];
        var barCloseSrc = entry.IsChartTfEntry && chartReplayClose.HasValue
            ? "chart-replay"
            : usesPartialOhlc
                ? (entry.LastFormingSyncFullPlatform ? "htf-platform" : "htf-forming-partial")
                : usesChartReplayClose
                    ? "chart-replay-HTF"
                    : isFormingBar ? "htf-forming" : "htfBar";

        var evalBarClosed = isFormingBar ? false : isBarClosed;
        var evalRealtime = isFormingBar ? false : isRealtime;
        var touchLtfOpen = ResolveTouchLtfOpenForBacktest(
            entry.TfToken, evalBarClosed, evalRealtime, barOpenLocal, entry.TfPeriod, chartBarCloseTime);

        var ctx = BuildHtfEvaluationContext(
            entry,
            barIdx,
            barOpenLocal,
            barClose,
            barHigh,
            barLow,
            evalBarClosed,
            evalRealtime,
            isNewBarOnRealtimeForming,
            chartM15CloseEdgeInjected,
            touchLtfOpen,
            touchLtfOpen.HasValue ? "ltfOpenM5" : null);

        if (ctx.ZoneState == null)
            return false;

        entry.ConditionCache.TryGetValue(AlertConditionId.CanBuyTouchM5, out var buyHit);
        entry.ConditionCache.TryGetValue(AlertConditionId.CanSellTouchM5, out var sellHit);

        double buyProbe;
        string? buyKind;
        int buyEvalT;
        if (buyHit.HasLastTouchProbe)
        {
            buyProbe = buyHit.LastTouchProbePrice;
            buyKind = buyHit.LastTouchProbeKind;
            buyEvalT = TouchRealEvaluators.CheckTouchZonesCombined(buyProbe, ctx.ZoneState);
        }
        else
        {
            (buyEvalT, buyProbe, buyKind) = TouchRealEvaluators.ResolveZoneTouchT(in ctx, AlertConditionId.CanBuyTouchM5);
        }

        double sellProbe;
        string? sellKind;
        int sellEvalT;
        if (sellHit.HasLastTouchProbe)
        {
            sellProbe = sellHit.LastTouchProbePrice;
            sellKind = sellHit.LastTouchProbeKind;
            sellEvalT = TouchRealEvaluators.CheckTouchZonesCombined(sellProbe, ctx.ZoneState);
        }
        else
        {
            (sellEvalT, sellProbe, sellKind) = TouchRealEvaluators.ResolveZoneTouchT(in ctx, AlertConditionId.CanSellTouchM5);
        }

        ZoneStateDebugFormatter.AppendHtfZoneTableHeader(
            sb, tfLabel, barIdx, barOpenLocal, barHigh, barLow, barClose, ctx.ZoneState.ZoneTouchResult);

        if (_htfTouchParityDebug)
        {
            var fedLast = entry.NextBarIdxToProcess > 0 ? entry.NextBarIdxToProcess - 1 : -1;
            var shellStateBar = entry.Shell.State.LastEvaluatedBarIndex;
            var canFeedDisp = chartBarCloseTime.HasValue
                && CanFeedClosedHtfBar(entry, barIdx, chartBarCloseTime.Value, realtimeChartEdge: false);
            var stateSynced = usesPartialOhlc
                && entry.Shell.State.LastEvaluatedBarIndex >= barIdx;
            var stateLag = !entry.IsChartTfEntry && isFormingBar && fedLast >= 0 && barIdx > fedLast
                && !stateSynced;

            ZoneStateDebugFormatter.AppendHtfSyncHeader(
                sb,
                FormatTfDebugLabel(chartTfToken),
                chartBarCloseTime,
                entry.IsChartTfEntry,
                dispBar: barIdx,
                fedLast,
                nextBar: entry.NextBarIdxToProcess,
                lastEvalBar: entry.LastEvaluatedBarIndex,
                shellStateBar,
                isFormingBar,
                canFeedDisp,
                barCloseSrc,
                stateLag);

            ZoneCollectorDebug.AppendPivotKeyAudit(
                sb,
                entry.Shell.State.Pivots,
                barLow,
                barHigh,
                buyProbe,
                sellProbe);
        }

        ZoneStateDebugFormatter.AppendHtfZoneTableBody(
            sb,
            ctx.ZoneState,
            buyProbe,
            buyKind,
            buyEvalT,
            buyHit.IsCurrentlyActive,
            sellProbe,
            sellKind,
            sellEvalT,
            sellHit.IsCurrentlyActive);

        return true;
    }

    static int ResolveHtfDebugBarIndex(MtfTfEntry entry, int chartBarIndex, bool useFormingBar)
    {
        if (entry.IsChartTfEntry)
            return chartBarIndex;

        if (useFormingBar && entry.TfBars.Count > 0 && entry.NextBarIdxToProcess > 0)
            return entry.TfBars.Count - 1;

        if (entry.LastEvaluatedBarIndex >= 0)
            return entry.LastEvaluatedBarIndex;

        return entry.TfBars.Count > 0 ? entry.TfBars.Count - 1 : -1;
    }

    /// <summary>
    /// Full Touch zone catalog on one TF at compound FIRE — all active GKL/GOB/RKL/ROB + REAL band.
    /// </summary>
    public string? DescribeTfZoneCatalogFireDebug(
        string tfToken,
        int chartBarIndex,
        bool isBarClosed,
        bool isRealtime,
        bool isNewBarOnRealtimeForming,
        bool chartM15CloseEdgeInjected,
        double? chartReplayHigh = null,
        double? chartReplayLow = null,
        double? chartReplayClose = null,
        AlertConditionId touchCondId = AlertConditionId.CanSellTouchM5)
    {
        if (!_entries.TryGetValue(tfToken, out var entry))
            return null;

        var barIdx = entry.IsChartTfEntry
            ? chartBarIndex
            : (entry.LastEvaluatedBarIndex >= 0
                ? entry.LastEvaluatedBarIndex
                : entry.TfBars.Count > 0 ? entry.TfBars.Count - 1 : -1);

        if (barIdx < 0 || barIdx >= entry.TfBars.Count)
            return null;

        var barOpenRaw = entry.TfBars.OpenTimes[barIdx];
        var barOpenLocal = ChartTimeFromBars.NormalizeBarOpenTime(barOpenRaw);
        var barHigh = entry.IsChartTfEntry && chartReplayHigh.HasValue
            ? chartReplayHigh.Value
            : entry.TfBars.HighPrices[barIdx];
        var barLow = entry.IsChartTfEntry && chartReplayLow.HasValue
            ? chartReplayLow.Value
            : entry.TfBars.LowPrices[barIdx];
        var barClose = chartReplayClose ?? entry.TfBars.ClosePrices[barIdx];
        var touchLtfOpen = ResolveTouchLtfOpenForBacktest(
            entry.TfToken, isBarClosed, isRealtime, barOpenLocal, entry.TfPeriod);

        var ctx = entry.IsChartTfEntry
            ? Loop6EvaluationContextFactory.Build(
                entry.Shell,
                barIdx,
                entry.TfToken,
                barOpenLocal,
                barClose,
                barHigh,
                barLow,
                isBarClosed,
                isRealtime,
                isNewBarOnRealtimeForming,
                chartM15CloseEdgeInjected,
                touchLtfOpenPrice: touchLtfOpen,
                alertTrackingDebug: _alertTrackingDebug,
                eventDetectionCache: entry.Shell.EventDetectionCache,
                realZoneDebug: true)
            : BuildHtfEvaluationContext(
                entry,
                barIdx,
                barOpenLocal,
                barClose,
                barHigh,
                barLow,
                isBarClosed,
                isRealtime,
                isNewBarOnRealtimeForming,
                chartM15CloseEdgeInjected,
                touchLtfOpen);

        if (ctx.ZoneState == null)
            return null;

        var (evalT, probe, probeKind) = TouchRealEvaluators.ResolveZoneTouchT(in ctx, touchCondId);
        var realDetail = TouchRealEvaluators.ComputeRealFlagsDetail(in ctx);
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        var sb = new System.Text.StringBuilder();
        sb.Append("bar#").Append(barIdx);
        sb.Append(" O=").Append(barOpenLocal.ToString("s"));
        sb.Append(" H=").Append(barHigh.ToString("F5", inv));
        sb.Append(" L=").Append(barLow.ToString("F5", inv));
        sb.Append(" C=").Append(barClose.ToString("F5", inv));
        sb.Append(' ');
        sb.Append(ZoneStateDebugFormatter.FormatFullCatalog(
            ctx.ZoneState,
            probe,
            probeKind,
            realDetail.DebugText,
            evalT,
            chartReplayClose,
            chartReplayClose.HasValue ? "m15Close" : null,
            chartReplayHigh,
            chartReplayLow));

        return sb.ToString();
    }

    /// <summary>
    /// One-line leg diagnosis for compound FIRE debug: cache vs fresh eval, sync window, RealZone detail.
    /// </summary>
    public string DescribeLegFireDebug(
        string tfToken,
        AlertConditionId condId,
        CompoundEvalSyncMode syncMode,
        int eventValidBars,
        int chartBarIndex,
        bool isBarClosed,
        bool isRealtime,
        bool isNewBarOnRealtimeForming,
        bool chartM15CloseEdgeInjected,
        double? chartReplayHigh = null,
        double? chartReplayLow = null)
    {
        if (!_entries.TryGetValue(tfToken, out var entry))
            return $"{tfToken}:{condId}=MISSING-TF";

        if (!entry.ConditionCache.TryGetValue(condId, out var hit))
            hit = new MtfConditionHit { LastFiredBarIndex = -1, LastFiredChartBarIndex = -1 };

        var cacheActive = IsConditionActive(tfToken, condId, syncMode, eventValidBars);
        var barIdx = entry.IsChartTfEntry
            ? chartBarIndex
            : (entry.LastEvaluatedBarIndex >= 0
                ? entry.LastEvaluatedBarIndex
                : entry.TfBars.Count > 0 ? entry.TfBars.Count - 1 : -1);

        if (barIdx < 0 || barIdx >= entry.TfBars.Count)
            return $"{tfToken}:{condId} cache={(cacheActive ? "OK" : "--")} no-bar";

        var barOpenRaw = entry.TfBars.OpenTimes[barIdx];
        var barOpenLocal = ChartTimeFromBars.NormalizeBarOpenTime(barOpenRaw);
        var barHigh = entry.IsChartTfEntry && chartReplayHigh.HasValue
            ? chartReplayHigh.Value
            : entry.TfBars.HighPrices[barIdx];
        var barLow = entry.IsChartTfEntry && chartReplayLow.HasValue
            ? chartReplayLow.Value
            : entry.TfBars.LowPrices[barIdx];
        var barClose = entry.TfBars.ClosePrices[barIdx];
        var touchLtfOpen = ResolveTouchLtfOpenForBacktest(
            entry.TfToken, isBarClosed, isRealtime, barOpenLocal, entry.TfPeriod);

        var ctx = entry.IsChartTfEntry
            ? Loop6EvaluationContextFactory.Build(
                entry.Shell,
                barIdx,
                entry.TfToken,
                barOpenLocal,
                barClose,
                barHigh,
                barLow,
                isBarClosed,
                isRealtime,
                isNewBarOnRealtimeForming,
                chartM15CloseEdgeInjected,
                touchLtfOpenPrice: touchLtfOpen,
                alertTrackingDebug: _alertTrackingDebug,
                eventDetectionCache: entry.Shell.EventDetectionCache,
                realZoneDebug: true)
            : BuildHtfEvaluationContext(
                entry,
                barIdx,
                barOpenLocal,
                barClose,
                barHigh,
                barLow,
                isBarClosed,
                isRealtime,
                isNewBarOnRealtimeForming,
                chartM15CloseEdgeInjected,
                touchLtfOpen);

        var freshFired = _engine.Registry.TryGetValue(condId, out _)
            && _engine.Evaluate(condId, in ctx).Fired;

        var sb = new System.Text.StringBuilder();
        sb.Append(tfToken).Append(':').Append(condId);
        sb.Append(" cache=").Append(cacheActive ? "OK" : "--");
        sb.Append(" fresh=").Append(freshFired ? "OK" : "--");

        if (cacheActive != freshFired)
            sb.Append(" MISMATCH");

        if (CompoundRuleParser.IsStateCondition(condId))
        {
            sb.Append(" bar#").Append(barIdx);
            sb.Append(" H=").Append(barHigh.ToString("F5", System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(" L=").Append(barLow.ToString("F5", System.Globalization.CultureInfo.InvariantCulture));

            if (condId is AlertConditionId.CanBuyReal or AlertConditionId.CanSellReal
                or AlertConditionId.CanBuyRealAndM15CloseNow or AlertConditionId.CanSellRealAndM15CloseNow)
            {
                var detail = TouchRealEvaluators.ComputeRealFlagsDetail(in ctx);
                if (!string.IsNullOrEmpty(detail.DebugText))
                    sb.Append(" | ").Append(detail.DebugText);
            }
            else if (condId is AlertConditionId.CanBuyTouchM5 or AlertConditionId.CanSellTouchM5)
            {
                var touch = TouchRealEvaluators.ComputeTouchDetail(in ctx, condId);
                sb.Append(' ').Append(TouchRealEvaluators.FormatTouchDebugText(in touch, condId));
            }
            else if (ctx.ZoneState != null)
            {
                sb.Append(" touchT=").Append(ctx.ZoneState.ZoneTouchResult);
            }

            return sb.ToString();
        }

        // EVENT leg — explain sync window vs never-fired.
        if (hit.LastFiredBarIndex < 0)
        {
            sb.Append(" never-fired");
            return sb.ToString();
        }

        var currentSyncIdx = entry.IsChartTfEntry
            ? CurrentBarIndexForSync(entry)
            : _currentChartBarIndex;
        var windowOk = CompoundEvalSync.IsEventActiveOnCurrentBar(
            syncMode, entry.IsChartTfEntry ? hit.LastFiredBarIndex : hit.LastFiredChartBarIndex,
            currentSyncIdx, eventValidBars);

        sb.Append(" lastFired=").Append(hit.LastFiredBarIndex);
        if (!entry.IsChartTfEntry)
            sb.Append("@chart=").Append(hit.LastFiredChartBarIndex);
        sb.Append(" cur=").Append(currentSyncIdx);
        sb.Append(" sync=").Append(syncMode);
        sb.Append(" win=").Append(eventValidBars);
        sb.Append(" window=").Append(windowOk ? "OK" : "EXPIRED");
        if (syncMode == CompoundEvalSyncMode.TradingView && hit.LastFiredChartBarIndex != _currentChartBarIndex && !entry.IsChartTfEntry)
            sb.Append(" tvNeedChart=").Append(_currentChartBarIndex);

        return sb.ToString();
    }

    bool TryResolveSnapshotBar(
        MtfTfEntry entry,
        CompoundSourceEventCandidate candidate,
        DateTime snapshotCloseTime,
        out int barIdx,
        out bool isClosedBar,
        out double open,
        out double high,
        out double low,
        out double close,
        out double? touchLtfOpen)
    {
        barIdx = -1;
        isClosedBar = false;
        open = high = low = close = 0;
        touchLtfOpen = null;

        if (string.Equals(entry.TfToken, candidate.SourceTfToken, StringComparison.Ordinal))
        {
            barIdx = candidate.SourceBarIndex;
            open = candidate.Open;
            high = candidate.High;
            low = candidate.Low;
            close = candidate.Close;
            isClosedBar = true;
            if (entry.TfToken == "5")
                touchLtfOpen = candidate.Open;
            return barIdx >= 0 && barIdx < entry.TfBars.Count;
        }

        if (!TryFindBarIndexAtSnapshot(entry, snapshotCloseTime, out barIdx, out isClosedBar))
            return false;

        if (isClosedBar)
        {
            open = entry.TfBars.OpenPrices[barIdx];
            high = entry.TfBars.HighPrices[barIdx];
            low = entry.TfBars.LowPrices[barIdx];
            close = entry.TfBars.ClosePrices[barIdx];
        }
        else if (string.Equals(candidate.SourceTfToken, "5", StringComparison.Ordinal))
        {
            var htfOpen = ChartTimeFromBars.NormalizeBarOpenTime(entry.TfBars.OpenTimes[barIdx]);
            if (!TryAggregatePartialFromM5(htfOpen, snapshotCloseTime, out open, out high, out low, out close, out var touchOpen))
                return false;
            touchLtfOpen = touchOpen;
        }
        else if (entry.LtfCollector != null && _marketData != null)
        {
            var htfOpen = ChartTimeFromBars.NormalizeBarOpenTime(entry.TfBars.OpenTimes[barIdx]);
            var bundle = entry.LtfCollector.CollectForFormingHtfBarUpTo(
                _marketData, entry.TfBars, barIdx, snapshotCloseTime);
            if (bundle == null || !HtfFormingBarPartial.TryAggregateFromLtfBundle(
                    entry.TfBars.OpenPrices[barIdx], bundle, out high, out low, out close))
            {
                open = entry.TfBars.OpenPrices[barIdx];
                high = entry.TfBars.HighPrices[barIdx];
                low = entry.TfBars.LowPrices[barIdx];
                close = entry.TfBars.ClosePrices[barIdx];
            }
            else
            {
                open = entry.TfBars.OpenPrices[barIdx];
            }

            touchLtfOpen = candidate.SourceTfToken == "15" ? candidate.Open : null;
        }
        else
        {
            open = entry.TfBars.OpenPrices[barIdx];
            high = entry.TfBars.HighPrices[barIdx];
            low = entry.TfBars.LowPrices[barIdx];
            close = entry.TfBars.ClosePrices[barIdx];
        }

        if (!touchLtfOpen.HasValue && entry.TfToken != "5")
        {
            var barOpenLocal = ChartTimeFromBars.NormalizeBarOpenTime(entry.TfBars.OpenTimes[barIdx]);
            touchLtfOpen = ResolveTouchLtfOpenForBacktest(
                entry.TfToken, isClosedBar, isRealtime: false, barOpenLocal, entry.TfPeriod, snapshotCloseTime);
        }

        return true;
    }

    bool TryFindBarIndexAtSnapshot(
        MtfTfEntry entry,
        DateTime snapshotCloseTime,
        out int barIdx,
        out bool isClosedBar)
    {
        barIdx = -1;
        isClosedBar = false;
        if (entry.TfBars.Count == 0)
            return false;

        for (var i = entry.TfBars.Count - 1; i >= 0; i--)
        {
            var openLocal = ChartTimeFromBars.NormalizeBarOpenTime(entry.TfBars.OpenTimes[i]);
            var closeLocal = openLocal.Add(entry.TfPeriod);
            if (closeLocal <= snapshotCloseTime)
            {
                barIdx = i;
                isClosedBar = true;
                return true;
            }

            if (openLocal < snapshotCloseTime && snapshotCloseTime < closeLocal)
            {
                barIdx = i;
                isClosedBar = false;
                return true;
            }
        }

        return false;
    }

    bool TryAggregatePartialFromM5(
        DateTime htfOpen,
        DateTime snapshotCloseTime,
        out double open,
        out double high,
        out double low,
        out double close,
        out double touchOpen)
    {
        open = high = low = close = touchOpen = 0;
        if (!_entries.TryGetValue("5", out var m5Entry) || m5Entry.TfBars.Count == 0)
            return false;

        var n = m5Entry.TfBars.Count;
        var openTimes = new DateTime[n];
        var o = new double[n];
        var h = new double[n];
        var l = new double[n];
        var c = new double[n];
        for (var i = 0; i < n; i++)
        {
            openTimes[i] = m5Entry.TfBars.OpenTimes[i];
            o[i] = m5Entry.TfBars.OpenPrices[i];
            h[i] = m5Entry.TfBars.HighPrices[i];
            l[i] = m5Entry.TfBars.LowPrices[i];
            c[i] = m5Entry.TfBars.ClosePrices[i];
        }

        return HtfPartialOhlcFromM5.TryAggregate(
            openTimes, o, h, l, c,
            m5Entry.TfPeriod,
            htfOpen,
            snapshotCloseTime,
            ChartTimeFromBars.NormalizeBarOpenTime,
            out open,
            out high,
            out low,
            out close,
            out touchOpen);
    }

    bool FeedFormingHtfBarAtSnapshot(
        MtfTfEntry entry,
        int formingIdx,
        DateTime snapshotTime,
        double open,
        double high,
        double low,
        double close,
        bool useLiveFormingFlags)
    {
        if (formingIdx < 0 || formingIdx >= entry.TfBars.Count)
            return false;
        if (high <= 0 || low <= 0 || open <= 0 || close <= 0 || high < low)
            return false;

        var isNewBar = entry.LastFormingSyncBarIdx != formingIdx
            || entry.LastFormingSyncClip != snapshotTime;
        var flags = useLiveFormingFlags
            ? Loop6BarRuntimeMapper.ForLiveRealtimeFormingLastBar(isNewBar)
            : Loop6BarRuntimeMapper.ForBacktestFormingLastBar(isNewBar);

        var shell = entry.Shell;
        var barSnap = BarsToCoreAdapter.ToBarSnapshot(
            entry.TfBars.OpenTimes[formingIdx],
            open,
            high,
            low,
            close,
            (long)entry.TfBars.TickVolumes[formingIdx]);

        if (!shell.Series.BackfillCompleted)
            shell.Series.InitialBackfill(-1, _ => null);

        shell.Series.OnCalculateBar(formingIdx, barSnap, flags);
        shell.State.TickSize = _tickSize;
        ApplyTfModeFlags(shell, entry.TfToken);
        shell.State.OnBar(shell.Series.Buffer, formingIdx, shell.Render, ltfSnapshotForClosedHtfBar: null);
        shell.Render.ClearCommands();

        entry.LastFormingSyncBarIdx = formingIdx;
        entry.LastFormingSyncClip = snapshotTime;
        entry.LastFormingSyncFullPlatform = false;
        entry.LastFormingPartialHigh = high;
        entry.LastFormingPartialLow = low;
        entry.LastFormingPartialClose = close;
        return true;
    }
}

public struct MtfConditionHit
{
    public bool IsCurrentlyActive;
    public int LastFiredBarIndex;
    public DateTime LastFiredBarOpenTime;
    /// <summary>M15 chart bar index when this HTF event condition last fired. Used for window sync. -1 = never.</summary>
    public int LastFiredChartBarIndex;
    /// <summary>Last M5 (or LTF) probe price used when touch STATE was evaluated.</summary>
    public double LastTouchProbePrice;
    /// <summary>Debug label — ltfOpenM5, ltfHighM5, ltfLowM5, close, …</summary>
    public string? LastTouchProbeKind;
    public bool HasLastTouchProbe => LastTouchProbePrice > 0;
}

public struct MtfDiagInfo
{
    public string TfToken;
    public bool IsChartTfEntry;
    public int TfBarsCount;
    public DateTime TfBarsLastOpenTime;
    public int NextBarIdxToProcess;
    public int AlertTrackEmitThroughIdx;
    public int LastEvaluatedBarIndex;
    public int ShellStateLastEvalBar;
    public int PivotsCount;
    public int DedupABuy, DedupASell, DedupBBuy, DedupBSell, DedupCBuy, DedupCSell;

    // ATR / clear-body diagnostics from the LAST OnBar() invocation
    public double AtrWilder14;
    public double AtrKeylevel;
    public double AvgBody;
    public double RawBody1;
    public double RawRange1;
    public bool RawIsDoji1;
    public bool RawClearBody1;
    public double EffBody;
    public bool EffIsDoji;
    public bool EffClearBody;
    public int AtrBarsSeen14;
    public double Ohlc1Open, Ohlc1High, Ohlc1Low, Ohlc1Close;
}

internal readonly struct EventFireTouchProbe
{
    public double ProbePrice { get; init; }
    public string ProbeKind { get; init; }
    public AlertConditionId TouchCondId { get; init; }
}

internal sealed class MtfTfEntry
{
    public TradeAlertIndicator Shell = null!;
    public Bars TfBars = null!;
    public string TfToken = "";
    public TimeSpan TfPeriod;
    public int NextBarIdxToProcess;
    public int AlertTrackEmitThroughIdx;
    public int LastEvaluatedBarIndex = -1;
    public Dictionary<AlertConditionId, MtfConditionHit> ConditionCache = null!;
    public bool IsChartTfEntry;
    public HostLtfCollector? LtfCollector;
    public int LastFormingSyncBarIdx = -1;
    public DateTime LastFormingSyncClip;
    public bool LastFormingSyncFullPlatform;
    public double LastFormingPartialHigh;
    public double LastFormingPartialLow;
    public double LastFormingPartialClose;
    public bool HasFormingShellSync =>
        LastFormingSyncBarIdx >= 0
        && (LastFormingSyncFullPlatform || LastFormingSyncClip > DateTime.MinValue);
}

/// <summary>TF token to cTrader <see cref="TimeFrame"/> and bar period.</summary>
internal static class Loop6TimeFrameLookup
{
    public static bool TryResolve(string tfToken, out TimeFrame timeFrame, out TimeSpan period)
    {
        timeFrame = TimeFrame.Minute;
        period = TimeSpan.FromMinutes(1);

        switch (tfToken)
        {
            case "1":
                timeFrame = TimeFrame.Minute;
                period = TimeSpan.FromMinutes(1);
                return true;
            case "2":
                timeFrame = TimeFrame.Minute2;
                period = TimeSpan.FromMinutes(2);
                return true;
            case "5":
                timeFrame = TimeFrame.Minute5;
                period = TimeSpan.FromMinutes(5);
                return true;
            case "15":
                timeFrame = TimeFrame.Minute15;
                period = TimeSpan.FromMinutes(15);
                return true;
            case "30":
                timeFrame = TimeFrame.Minute30;
                period = TimeSpan.FromMinutes(30);
                return true;
            case "60":
                timeFrame = TimeFrame.Hour;
                period = TimeSpan.FromHours(1);
                return true;
            case "240":
                timeFrame = TimeFrame.Hour4;
                period = TimeSpan.FromHours(4);
                return true;
            case "1440":
                timeFrame = TimeFrame.Daily;
                period = TimeSpan.FromDays(1);
                return true;
            default:
                return false;
        }
    }
}
