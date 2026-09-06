using System;
using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Internals;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models;
using TradeAlert.Core.Models.Alerts;
using TradeAlert.Core.Models.KeyLevel;
using TradeAlert.Core.Series;
using TradeAlert.Indicator.Host;

namespace TradeAlert.Indicator;

/// <summary>
/// Headless, per-symbol signal host for the multi-symbol scanner cBot.
/// Owns one <see cref="TradeAlertIndicator"/> shell + <see cref="MtfEngineManager"/> +
/// <see cref="CompoundFireOrchestrator"/> for a single (symbol, timeframe) pair.
///
/// There is no Chart, no drawing, no cAlgo indicator API — only bar feeds and compound
/// rule evaluation. Call <see cref="Initialize"/> once from <c>OnStart</c>, then call
/// <see cref="Tick"/> on every <c>OnTimer</c> event.
/// </summary>
public sealed class PerSymbolSignalHost : IDisposable
{
    // ── Identity ─────────────────────────────────────────────────────────────
    public string    SymbolName     { get; }
    public TimeFrame ChartTimeFrame { get; }

    /// <summary>
    /// Read-only engine state for the chart-TF shell (pivots / keylevel boxes / OB pool).
    /// Consumers (e.g. backtest trading cBot) must NOT mutate this — it is exposed only so
    /// trade-plan builders can read swing/keylevel/OB geometry after <see cref="Tick"/>.
    /// </summary>
    public PineStateEngine State => _shell.State;

    // ── Infrastructure ────────────────────────────────────────────────────────
    readonly TradeAlertIndicator  _shell;
    readonly MtfEngineManager     _mtfManager;
    readonly CompoundFireOrchestrator _compoundFire;
    readonly HostLtfCollector       _ltfCollector = new();
    readonly Bars                 _bars;
    readonly MarketData           _marketData;
    readonly string               _chartTfToken;
    readonly Loop6HostParameterSnapshot _snapshot;
    readonly CompoundEvalSyncMode _syncMode;
    readonly int                  _eventValidBars;
    readonly bool                 _injectM15CloseEdge;
    readonly int                  _warmupBars;
    readonly PerSymbolSignalHostMode _mode;
    readonly bool                 _visualReplayUseM5Bars;
    readonly TimeSpan             _visualReplaySubBarPeriod;
    readonly Action<string>?      _log;

    // ── Bar tracking ──────────────────────────────────────────────────────────
    int  _nextBarToProcess;       // next bar index to feed as closed
    int  _lastLiveBarCount;       // bars.Count when live bar was last processed
    int  _realtimeHits;           // increments each tick while last bar is the same
    bool _initialized;
    bool _disposed;
    int  _lastClosedBarProcessedThisTick = -1;
    int  _lastChartOnBarIdx = -1;       // DEBUG: detect chart-shell double-OnBar (BacktestBarClose)

    // ── Bar-open-time tracker for IsNew detection ─────────────────────────────
    DateTime _lastLiveBarOpenTime;
    readonly Loop6LastBarOpenTracker _replayOpenTracker = new();
    Bars? _replaySubBars;

    /// <summary>First chart bar index replayed during <see cref="Initialize"/> warmup.</summary>
    public int WarmupStartBar { get; private set; } = -1;

    /// <summary>Last closed chart bar index replayed during warmup (inclusive).</summary>
    public int WarmupEndBar { get; private set; } = -1;

    /// <summary>Next chart bar index <see cref="Tick"/> will treat as newly closed.</summary>
    public int NextBarToProcess => _nextBarToProcess;

    /// <summary>Chart shell series current evaluation bar (absolute chart index).</summary>
    public int ChartShellLastBarIndex => _shell.Series.Buffer.CurrentEvaluationBarIndex;

    /// <summary>Chart bar index closed and compound-evaluated on the last <see cref="Tick"/>; -1 if none.</summary>
    public int LastClosedBarProcessedThisTick => _lastClosedBarProcessedThisTick;

    /// <summary>Bar index of the most recently pushed pivot on the chart shell, or -1.</summary>
    public int GetLatestPivotBarIndex()
    {
        var pivots = _shell.State.Pivots;
        if (pivots.Count == 0)
            return -1;
        return pivots.GetSnapshot(pivots.Count - 1).BarIndex;
    }

    public PerSymbolSignalHost(
        string symbolName,
        double tickSize,
        TimeFrame scanTimeFrame,
        Bars chartBars,
        MarketData marketData,
        in Loop6HostParameterSnapshot snapshot,
        string r1, string r2, string r3, string r4, string r5, string r6,
        CompoundEvalSyncMode syncMode,
        int eventValidBars,
        bool injectM15CloseEdge,
        int warmupBars,
        Action<string>? log = null,
        PerSymbolSignalHostMode mode = PerSymbolSignalHostMode.ScannerRealtime,
        CompoundWindowMode compoundWindowMode = CompoundWindowMode.SourceOneShot,
        bool visualReplayUseM5Bars = false)
    {
        SymbolName     = symbolName;
        ChartTimeFrame = scanTimeFrame;
        _bars          = chartBars;
        _marketData    = marketData;
        _snapshot      = snapshot;
        _syncMode      = syncMode;
        _eventValidBars = eventValidBars;
        _injectM15CloseEdge = injectM15CloseEdge;
        _warmupBars    = warmupBars;
        _mode          = mode;
        _visualReplayUseM5Bars = visualReplayUseM5Bars;
        _visualReplaySubBarPeriod = visualReplayUseM5Bars
            ? TimeSpan.FromMinutes(5)
            : TimeSpan.FromMinutes(1);
        _log           = log;
        _chartTfToken  = Loop6ChartTimeframeToken.FromBarsTimeFrame(scanTimeFrame);

        var hostCtx = new IndicatorHostContext
        {
            Symbol               = symbolName,
            ChartTimeframeToken  = _chartTfToken,
            HostChartIsRealtime  = false,
        };

        _shell = new TradeAlertIndicator(hostCtx, VoidAlertFireSink.Instance);
        Loop6ParameterBridge.Apply(_shell, in snapshot);
        _shell.State.TickSize  = tickSize;
        _shell.State.IsM5Mode  = _chartTfToken == "5";
        _shell.State.IsH4Chart = _chartTfToken == "240";

        _ltfCollector.Configure(ChartTimeFrame, _chartTfToken);
        _ltfCollector.BindSymbol(symbolName, _mode == PerSymbolSignalHostMode.BacktestBarClose
            || _mode == PerSymbolSignalHostMode.BacktestVisualReplay ? _log : null);
        _ltfCollector.EnsureLtfBarsForSymbol(marketData, symbolName);
        SyncObLtfMappingResolved();
        SyncLtfSnapshotFetcher();

        if (_mode == PerSymbolSignalHostMode.BacktestVisualReplay)
            _replaySubBars = ChartSymbolMarketGuard.GetBars(
                marketData,
                visualReplayUseM5Bars ? TimeFrame.Minute5 : TimeFrame.Minute,
                symbolName,
                _log);

        _mtfManager   = new MtfEngineManager(symbolName, tickSize);
        _compoundFire = new CompoundFireOrchestrator();

        var activeRules = _compoundFire.Initialize(r1, r2, r3, r4, r5, r6,
            msg => _log?.Invoke($"[{symbolName}] {msg}"));
        _compoundFire.SetWindowMode(compoundWindowMode);

        if (activeRules.Count > 0)
        {
            var barPeriod = Loop6TimeFrameLookup.TryResolve(_chartTfToken, out _, out var p) ? p : TimeSpan.FromMinutes(15);
            _mtfManager.RegisterChartTfShell(_chartTfToken, _shell, chartBars, barPeriod);
            _mtfManager.InitializeHtfEnginesForSymbol(
                CompoundRuleParser.CollectRequiredTfTokens(activeRules),
                _chartTfToken,
                marketData,
                symbolName,
                in snapshot,
                msg => _log?.Invoke($"[{symbolName}] {msg}"));
        }
    }

    // ── Initialize — backfill history ─────────────────────────────────────────
    /// <summary>
    /// Feeds historical bars into the engine (limited to <see cref="_warmupBars"/>).
    /// Must be called once from <c>OnStart</c> before the first <see cref="Tick"/>.
    /// </summary>
    public void Initialize()
    {
        if (_initialized)
            return;
        _initialized = true;

        if (_bars.Count == 0)
        {
            _log?.Invoke($"[{SymbolName}] Init: no bars — skipping backfill");
            _shell.Series.InitialBackfill(-1, _ => null);
            return;
        }

        var lastIdx   = _bars.Count - 1;
        // Feed all history bars except the very last (which is still forming).
        var startBar  = Math.Max(0, lastIdx - _warmupBars);
        var endBar    = lastIdx - 1;          // last closed bar (inclusive)

        WarmupStartBar = startBar;
        WarmupEndBar   = endBar;

        // Absolute chart bar indices — same convention as TradeAlertLoop6Host and Tick().
        _shell.Series.InitialBackfill(endBar, i =>
        {
            if (i < startBar)
                return null;

            var b = BarsToCoreAdapter.ToBarSnapshot(
                _bars.OpenTimes[i],
                _bars.OpenPrices[i],
                _bars.HighPrices[i],
                _bars.LowPrices[i],
                _bars.ClosePrices[i],
                (long)_bars.TickVolumes[i]);
            var ff = Loop6BarRuntimeMapper.ForHistoricalBackfillRow(i, endBar);
            return (b, ff);
        });

        // Pre-populate LTF for bars just before startBar so wick offset-2+ is available
        // at the very first bars of the replay (avoids HTF-only fallback on first ~WickAvgLen bars).
        var preWarmupStart = Math.Max(0, startBar - _shell.State.WickAvgLen - 2);
        if (preWarmupStart < startBar)
            BackfillMissingLtfSnapshots(preWarmupStart, startBar - 1);

        // Replay warmup bars: OnCalculateBar + OnBar at absolute index i (parity with Indicator).
        for (var i = startBar; i <= endBar; i++)
        {
            if (_mode == PerSymbolSignalHostMode.BacktestVisualReplay && _compoundFire.HasAnyRule)
            {
                _ = ProcessBacktestChartBar(i);
                continue;
            }

            var barSnap = BarsToCoreAdapter.ToBarSnapshot(
                _bars.OpenTimes[i],
                _bars.OpenPrices[i],
                _bars.HighPrices[i],
                _bars.LowPrices[i],
                _bars.ClosePrices[i],
                (long)_bars.TickVolumes[i]);

            var flags = Loop6BarRuntimeMapper.ForClosedChartBarBeforeLast();
            _shell.Series.OnCalculateBar(i, barSnap, flags);
            SyncObLtfMappingResolved();
            var ltfSnap = CollectLtfSnapshotForOnBar(i);
            _shell.State.OnBar(_shell.Series.Buffer, i, _shell.Render, ltfSnap);

            // Backtest: advance MTF condition cache bar-by-bar (matches Indicator Calculate).
            // Scanner: skip — live path catches up from attach; avoids spurious startup fires.
            if (_mode == PerSymbolSignalHostMode.BacktestBarClose && _compoundFire.HasAnyRule)
            {
                var barOpenLocal = ChartTimeFromBars.NormalizeBarOpenTime(_bars.OpenTimes[i]);
                var ctx = MakeFireContext(
                    index:                     i,
                    barOpenLocal:              barOpenLocal,
                    barPeriod:                 GetBarPeriod(),
                    liveFormingLastBar:        false,
                    isNewBarOnRealtimeForming: false,
                    isLastBar:                 false);
                _compoundFire.RunOneTick(_mtfManager, in ctx);
            }
        }

        BackfillMissingLtfSnapshots(startBar, endBar);
        _shell.State.ReconcileSuspiciousObLtfConfirm(endBar, _shell.Render);

        _nextBarToProcess = lastIdx;   // start Tick() from the live last bar
        _log?.Invoke(
            $"[{SymbolName}] Init OK | chartTf={_chartTfToken} mode={_mode} replaySubTf={VisualReplaySubTfLabel()} bars={_bars.Count} warmup={endBar - startBar + 1} startBar={startBar} endBar={endBar} nextBar={_nextBarToProcess} ltfReady={_ltfCollector.IsConfigured} subBarsReady={_replaySubBars is { Count: > 0 }}");
    }

    // ── Tick — called every OnTimer ───────────────────────────────────────────
    /// <summary>
    /// Advances the signal engine to the current bar, evaluates compound rules,
    /// and returns any events that fired this tick.
    /// </summary>
    public IReadOnlyList<CompoundFireEvent> Tick()
    {
        if (!_initialized || _disposed || !_compoundFire.HasAnyRule)
            return Array.Empty<CompoundFireEvent>();

        if (_bars.Count < 1)
            return Array.Empty<CompoundFireEvent>();

        var lastIdx = _bars.Count - 1;
        var allEvents = new List<CompoundFireEvent>();
        _lastClosedBarProcessedThisTick = -1;

        // ── 1. Feed any newly closed bars ─────────────────────────────────────
        while (_nextBarToProcess < lastIdx)
        {
            var i = _nextBarToProcess;

            IReadOnlyList<CompoundFireEvent> events;
            if (_mode == PerSymbolSignalHostMode.BacktestVisualReplay)
            {
                events = ProcessBacktestChartBar(i);
            }
            else
            {
                var barSnap = BarsToCoreAdapter.ToBarSnapshot(
                    _bars.OpenTimes[i],
                    _bars.OpenPrices[i],
                    _bars.HighPrices[i],
                    _bars.LowPrices[i],
                    _bars.ClosePrices[i],
                    (long)_bars.TickVolumes[i]);

                var flags = Loop6BarRuntimeMapper.ForClosedChartBarBeforeLast();

                if (_mode == PerSymbolSignalHostMode.BacktestBarClose && i == _lastChartOnBarIdx)
                    _log?.Invoke($"[L6BT] ERROR duplicate chart OnBar idx={i}");
                _lastChartOnBarIdx = i;

                _shell.Series.OnCalculateBar(i, barSnap, flags);
                SyncObLtfMappingResolved();
                var ltfSnap = CollectLtfSnapshotForOnBar(i);
                _shell.State.OnBar(_shell.Series.Buffer, i, _shell.Render, ltfSnap);

                var barOpenLocal = ChartTimeFromBars.NormalizeBarOpenTime(_bars.OpenTimes[i]);
                var barPeriod    = GetBarPeriod();

                var ctx = MakeFireContext(
                    index:                     i,
                    barOpenLocal:              barOpenLocal,
                    barPeriod:                 barPeriod,
                    liveFormingLastBar:        false,
                    isNewBarOnRealtimeForming: false,
                    isLastBar:                 false);
                events = _compoundFire.RunOneTick(_mtfManager, in ctx);
            }

            allEvents.AddRange(events);

            if (_mode != PerSymbolSignalHostMode.BacktestVisualReplay)
                _shell.State.UpdateKeyExtPrevAfterAlertEval(i);

            _lastClosedBarProcessedThisTick = i;
            _nextBarToProcess++;
        }

        // ── 2. Feed live-forming last bar — ScannerRealtime ONLY ──────────────
        // BacktestBarClose MUST NOT OnBar the forming bar: it snapshots the pivot
        // flag to its confirmed value, so the next tick's closed-bar pass sees
        // prevFlag==2 && flagNow==2 and the M15 EVENT transition (flagPrev!=2 && flagNow==2)
        // is permanently masked → R3-R6 never fire. The closed-bar loop above already
        // processes each chart bar exactly once with a fresh prevFlag snapshot.
        if (_mode == PerSymbolSignalHostMode.ScannerRealtime)
        {
            var i         = lastIdx;
            var barOpenRaw = _bars.OpenTimes[i];

            // Detect when a genuinely new bar appears (open time changed).
            var isNewBarViaOt = barOpenRaw != _lastLiveBarOpenTime;
            if (isNewBarViaOt)
            {
                _lastLiveBarOpenTime = barOpenRaw;
                _realtimeHits = 0;   // reset; will count up from this tick
            }

            _realtimeHits++;

            var barSnap = BarsToCoreAdapter.ToBarSnapshot(
                _bars.OpenTimes[i],
                _bars.OpenPrices[i],
                _bars.HighPrices[i],
                _bars.LowPrices[i],
                _bars.ClosePrices[i],
                (long)_bars.TickVolumes[i]);

            var flags = _realtimeHits <= 1
                ? Loop6BarRuntimeMapper.ForAttachHistoryPhaseLastBar()
                : Loop6BarRuntimeMapper.ForLiveRealtimeFormingLastBar(isNewBarViaOt);

            _shell.Series.OnCalculateBar(i, barSnap, flags);
            SyncObLtfMappingResolved();
            LtfBarBundle? ltfSnap = null;
            if (ShouldCollectLtfSnapshot(i, lastIdx, flags))
                ltfSnap = CollectLtfSnapshotForOnBar(i);
            _shell.State.OnBar(_shell.Series.Buffer, i, _shell.Render, ltfSnap);

            // BacktestBarClose: compound FIRE only on closed bars (handled above).
            // ScannerRealtime: require >= 2 ticks on the same forming bar before firing.
            var shouldEvalCompound = _mode == PerSymbolSignalHostMode.ScannerRealtime
                ? _realtimeHits >= 2
                : false;

            if (shouldEvalCompound)
            {
                var barOpenLocal = ChartTimeFromBars.NormalizeBarOpenTime(barOpenRaw);
                var barPeriod    = GetBarPeriod();

                var ctx = MakeFireContext(
                    index:                     i,
                    barOpenLocal:              barOpenLocal,
                    barPeriod:                 barPeriod,
                    liveFormingLastBar:        true,
                    isNewBarOnRealtimeForming: isNewBarViaOt,
                    isLastBar:                 true);

                var events = _compoundFire.RunOneTick(_mtfManager, in ctx);
                allEvents.AddRange(events);
            }

            _lastLiveBarCount = _bars.Count;
        }

        return allEvents;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    IReadOnlyList<CompoundFireEvent> ProcessBacktestChartBar(int chartBarIndex)
    {
        if (_mode != PerSymbolSignalHostMode.BacktestVisualReplay)
            throw new InvalidOperationException("ProcessBacktestChartBar requires BacktestVisualReplay mode.");

        if (chartBarIndex == _lastChartOnBarIdx)
            _log?.Invoke($"[L6BT] ERROR duplicate chart OnBar idx={chartBarIndex}");
        _lastChartOnBarIdx = chartBarIndex;

        var barPeriod     = GetBarPeriod();
        var barOpenLocal  = ChartTimeFromBars.NormalizeBarOpenTime(_bars.OpenTimes[chartBarIndex]);
        var htfCloseLocal = barOpenLocal.Add(barPeriod);

        if (_replaySubBars == null || _replaySubBars.Count == 0)
        {
            _replaySubBars = ChartSymbolMarketGuard.GetBars(
                _marketData,
                _visualReplayUseM5Bars ? TimeFrame.Minute5 : TimeFrame.Minute,
                SymbolName,
                _log);
        }

        var subBarSteps = _replaySubBars is { Count: > 0 }
            ? BacktestVisualReplay.CollectSubBarIndicesInHtfBar(_replaySubBars, barOpenLocal, htfCloseLocal)
            : Array.Empty<int>();

        if (subBarSteps.Count == 0)
            return ProcessBacktestChartBarCloseOnly(chartBarIndex);

        var allEvents = new List<CompoundFireEvent>();
        _replayOpenTracker.SeedSilent(barOpenLocal);

        var runningHigh = _bars.OpenPrices[chartBarIndex];
        var runningLow  = _bars.OpenPrices[chartBarIndex];

        foreach (var subBarIdx in subBarSteps)
        {
            var isNewBar = _replayOpenTracker.TryConsumeNewBarOpen(barOpenLocal);
            var partial = BacktestVisualReplay.BuildPartialChartBarSnapshot(
                _bars, chartBarIndex, _replaySubBars!, subBarIdx, runningHigh, runningLow);
            runningHigh = partial.High;
            runningLow  = partial.Low;

            var formingFlags = Loop6BarRuntimeMapper.ForBacktestFormingLastBar(isNewBar);
            _shell.Series.OnCalculateBar(chartBarIndex, partial, formingFlags);
            SyncObLtfMappingResolved();

            LtfBarBundle? ltfSnap = null;
            if (ShouldCollectLtfSnapshot(chartBarIndex, _bars.Count - 1, formingFlags))
                ltfSnap = CollectLtfSnapshotForOnBar(chartBarIndex);

            _shell.State.OnBar(_shell.Series.Buffer, chartBarIndex, _shell.Render, ltfSnap);

            var simClose = BacktestVisualReplay.SubBarCloseLocal(_replaySubBars!, subBarIdx, _visualReplaySubBarPeriod);
            var ctx = MakeFireContext(
                index:                        chartBarIndex,
                barOpenLocal:                 barOpenLocal,
                barPeriod:                    barPeriod,
                liveFormingLastBar:           false,
                isNewBarOnRealtimeForming:    isNewBar,
                isLastBar:                    true,
                isVisualBacktesting:          true,
                simulatedChartCloseTimeLocal: simClose,
                replayChartHigh:              partial.High,
                replayChartLow:               partial.Low);

            allEvents.AddRange(_compoundFire.RunOneTick(_mtfManager, in ctx));
            _shell.State.UpdateKeyExtPrevAfterAlertEval(chartBarIndex);
        }

        allEvents.AddRange(ProcessBacktestChartBarCloseOnly(chartBarIndex));
        return allEvents;
    }

    IReadOnlyList<CompoundFireEvent> ProcessBacktestChartBarCloseOnly(int chartBarIndex)
    {
        var barSnap = BarsToCoreAdapter.ToBarSnapshot(
            _bars.OpenTimes[chartBarIndex],
            _bars.OpenPrices[chartBarIndex],
            _bars.HighPrices[chartBarIndex],
            _bars.LowPrices[chartBarIndex],
            _bars.ClosePrices[chartBarIndex],
            (long)_bars.TickVolumes[chartBarIndex]);

        var flags = Loop6BarRuntimeMapper.ForClosedChartBarBeforeLast();
        _shell.Series.OnCalculateBar(chartBarIndex, barSnap, flags);
        SyncObLtfMappingResolved();
        var ltfSnap = CollectLtfSnapshotForOnBar(chartBarIndex);
        _shell.State.OnBar(_shell.Series.Buffer, chartBarIndex, _shell.Render, ltfSnap);

        var barOpenLocal = ChartTimeFromBars.NormalizeBarOpenTime(_bars.OpenTimes[chartBarIndex]);
        var ctx = MakeFireContext(
            index:                     chartBarIndex,
            barOpenLocal:              barOpenLocal,
            barPeriod:                 GetBarPeriod(),
            liveFormingLastBar:        false,
            isNewBarOnRealtimeForming: false,
            isLastBar:                 false,
            isVisualBacktesting:       false);

        var events = _compoundFire.RunOneTick(_mtfManager, in ctx);
        _shell.State.UpdateKeyExtPrevAfterAlertEval(chartBarIndex);
        return events;
    }

    CompoundFireOrchestrator.FireRunContext MakeFireContext(
        int index,
        DateTime barOpenLocal,
        TimeSpan barPeriod,
        bool liveFormingLastBar,
        bool isNewBarOnRealtimeForming,
        bool isLastBar,
        bool isVisualBacktesting = false,
        DateTime? simulatedChartCloseTimeLocal = null,
        double? replayChartHigh = null,
        double? replayChartLow = null)
    {
        var isRealtimeMode = _mode == PerSymbolSignalHostMode.ScannerRealtime;
        var hits = isRealtimeMode ? _realtimeHits : 0;

        return new CompoundFireOrchestrator.FireRunContext(
            symbol:                       SymbolName,
            bars:                         _bars,
            chartTfToken:                 _chartTfToken,
            index:                        index,
            symbolTickSize:               _shell.State.TickSize,
            barOpenTimeLocal:             barOpenLocal,
            barPeriod:                    barPeriod,
            liveFormingLastBar:           liveFormingLastBar,
            isNewBarOnRealtimeForming:    isNewBarOnRealtimeForming,
            isBackfillCompleted:          _shell.Series.BackfillCompleted,
            realtimeLastBarHits:          hits,
            isLastBar:                    isLastBar,
            isRealtimeMode:               isRealtimeMode,
            isVisualBacktesting:          isVisualBacktesting,
            injectM15CloseEdge:           _injectM15CloseEdge,
            syncMode:                     _syncMode,
            eventValidBars:               _eventValidBars,
            simulatedChartCloseTimeLocal: simulatedChartCloseTimeLocal,
            replayChartHigh:              replayChartHigh,
            replayChartLow:               replayChartLow);
    }

    void SyncObLtfMappingResolved() =>
        _shell.State.ObLtfMappingResolved = _ltfCollector.HasLtfMapping;

    void SyncLtfSnapshotFetcher()
    {
        _shell.State.LtfSnapshotFetcher = htfBar =>
        {
            if (!_ltfCollector.HasLtfMapping)
                return null;
            return _ltfCollector.CollectForClosedHtfBarWithRebind(_marketData, _bars, htfBar);
        };
    }

    void BackfillMissingLtfSnapshots(int startBar, int endBar)
    {
        if (!_ltfCollector.HasLtfMapping)
            return;

        var buf = _shell.State.LtfBuffer;
        var filled = 0;
        for (var htfBar = startBar; htfBar <= endBar; htfBar++)
        {
            if (buf.HasBar(htfBar))
                continue;

            var snap = _ltfCollector.CollectForClosedHtfBarWithRebind(_marketData, _bars, htfBar);
            if (snap == null)
                continue;

            buf.PushSnapshot(htfBar, snap);
            filled++;
        }

        if (filled > 0)
            _log?.Invoke($"[{SymbolName}] LTF backfill filled={filled} range={startBar}..{endBar} flat={buf.FlatCount}");
    }

    /// <summary>LTF của HTF bar <c>onBarIndex - 1</c> (bar A trong OnBar).</summary>
    LtfBarBundle? CollectLtfSnapshotForOnBar(int onBarIndex)
    {
        if (onBarIndex < 1 || !_ltfCollector.HasLtfMapping)
            return null;
        return _ltfCollector.CollectForClosedHtfBarWithRebind(_marketData, _bars, onBarIndex - 1);
    }

    static bool ShouldCollectLtfSnapshot(int index, int lastIdx, BarRuntimeFlags flags) =>
        flags.IsConfirmed
        || (flags.IsRealtime && flags.IsLast && index == lastIdx && index >= 1);

    TimeSpan GetBarPeriod() =>
        Loop6TimeFrameLookup.TryResolve(_chartTfToken, out _, out var p) ? p : TimeSpan.FromMinutes(15);

    string VisualReplaySubTfLabel() =>
        _mode == PerSymbolSignalHostMode.BacktestVisualReplay
            ? (_visualReplayUseM5Bars ? "M5" : "M1")
            : "n/a";

    // ── Gate zone read API (BacktestRobot) ────────────────────────────────────

    /// <summary>Read-only Pine state for chart or HTF shell at <paramref name="tfToken"/>.</summary>
    public bool TryGetState(string tfToken, out PineStateEngine state) =>
        _mtfManager.TryGetState(tfToken, out state);

    /// <summary>OHLC series buffer for chart or HTF shell at <paramref name="tfToken"/>.</summary>
    public bool TryGetSeriesBuffer(string tfToken, out SeriesBuffer buffer) =>
        _mtfManager.TryGetSeriesBuffer(tfToken, out buffer);

    /// <summary>Ensure H4/H1/M15/M5 shells exist even when compound rules omit a TF.</summary>
    public void EnsureGateTfEngines(IEnumerable<string> gateTfTokens, in Loop6HostParameterSnapshot snapshot)
    {
        if (!_mtfManager.HasTfEntry(_chartTfToken))
        {
            _mtfManager.RegisterChartTfShell(_chartTfToken, _shell, _bars, GetBarPeriod());
        }

        _mtfManager.EnsureTfEnginesRegistered(
            gateTfTokens,
            _chartTfToken,
            _marketData,
            SymbolName,
            in snapshot,
            _log);
    }

    /// <summary>Sync HTF shells to chart bar close for gate zone reads (after Tick).</summary>
    public void SyncGateTfStates(DateTime chartBarCloseLocal, bool realtimeEdge) =>
        _mtfManager.SyncHtfEngineBarsForGate(chartBarCloseLocal, realtimeEdge);

    // ── Compound diagnostics (BacktestRobot) ─────────────────────────────────

    /// <summary>
    /// Which slots (0-based) have an active (non-empty, parsed) compound rule.
    /// </summary>
    public string CompoundMinTfToken   => _compoundFire.MinTfToken;
    public int    CompoundMinTfMinutes => _compoundFire.MinTfMinutes;

    public IReadOnlyList<int> ActiveCompoundSlots()
    {
        var result = new List<int>();
        var rules = _compoundFire.RulesBySlot;
        for (var i = 0; i < rules.Count; i++)
            if (rules[i] != null)
                result.Add(i);
        return result;
    }

    /// <summary>
    /// Human-readable condition status for a single (TF, condition) pair.
    /// Returns "[--]" when not active, "[OK] ..." when active.
    /// </summary>
    public string FormatConditionStatus(string tfToken, AlertConditionId condId) =>
        _mtfManager.FormatConditionStatus(tfToken, condId, _syncMode, _eventValidBars);

    /// <summary>Compact leg snapshot for chart debug labels, e.g. <c>M5:cond+ M5:real+ H1:touch-</c>.</summary>
    public string FormatRuleLegSnapshot(int slotIndex)
    {
        var rules = _compoundFire.RulesBySlot;
        if (slotIndex < 0 || slotIndex >= rules.Count || rules[slotIndex] is not { } rule)
            return "";

        var sb = new System.Text.StringBuilder();
        foreach (var entry in rule.Entries)
        {
            var status = _mtfManager.FormatConditionStatus(
                entry.TfToken, entry.ConditionId, _syncMode, _eventValidBars);
            var ok = status.StartsWith("[OK]", StringComparison.Ordinal);
            if (sb.Length > 0)
                sb.Append(' ');
            sb.Append(entry.TfToken).Append(':').Append(ShortLegName(entry.ConditionId))
                .Append(ok ? '+' : '-');
        }

        return sb.ToString();
    }

    static string ShortLegName(AlertConditionId id) =>
        id switch
        {
            AlertConditionId.CondBuyEventM5 or AlertConditionId.CondSellEventM5 => "cond",
            AlertConditionId.CondBuyEventHLM15 or AlertConditionId.CondSellEventHLM15 => "hlM15",
            AlertConditionId.CondBuyEventNGM15 or AlertConditionId.CondSellEventNGM15 => "ngM15",
            AlertConditionId.CondBuyEventHLNGM15 or AlertConditionId.CondSellEventHLNGM15 => "hlngM15",
            AlertConditionId.CanBuyReal or AlertConditionId.CanSellReal => "real",
            AlertConditionId.CanBuyTouchM5 or AlertConditionId.CanSellTouchM5 => "touch",
            AlertConditionId.CanBuyRealAndM15CloseNow or AlertConditionId.CanSellRealAndM15CloseNow => "realM15",
            _ => CompoundRuleParser.PineNameFor(id),
        };

    /// <summary>
    /// Forward a per-symbol diagnostic logger to the compound orchestrator so it can
    /// emit <c>[StageDiag]</c> lines describing stage create / confirm / dedup / expire.
    /// </summary>
    public void SetCompoundDiagnosticLogger(Action<string>? log, bool enabled = true) =>
        _compoundFire.SetDiagnosticLogger(log, enabled);

    public void SetCompoundAuditLogger(Action<string>? log, bool enabled = true) =>
        _compoundFire.SetCompoundAuditLogger(log, enabled);

    /// <summary>
    /// Register a callback invoked (synchronously, during <c>RunOneTick</c>) when a new
    /// <see cref="CompoundWindowMode.SetupAtCondFire"/> anchor is created at cond-fire bar N.
    /// At call time, the engine state reflects bar N — the bot can resolve B/D geometry immediately.
    /// </summary>
    public void SetAnchorCreatedCallback(Action<CompoundEventAnchor>? callback) =>
        _compoundFire.SetAnchorCreatedCallback(callback);

    /// <summary>
    /// Forward fire-debug logger — emits <c>[FireDbg]</c> with cache vs fresh leg eval and RealZone detail.
    /// </summary>
    public void SetCompoundFireDebugLogger(Action<string>? log, bool enabled = true)
    {
        _compoundFire.SetFireDebugLogger(log, enabled);
        _mtfManager.ConfigureHtfTouchParityDebug(enabled);
    }

    /// <summary>
    /// Full H1 (or any TF) Touch zone catalog at a chart bar — for FireDbg deferred-split analysis.
    /// </summary>
    public string? DescribeTfZoneCatalogAtChartBar(
        string tfToken,
        int chartBarIndex,
        double chartClose,
        double? replayHigh = null,
        double? replayLow = null,
        AlertConditionId touchCondId = AlertConditionId.CanSellTouchM5)
    {
        var minute = chartBarIndex >= 0 && chartBarIndex < _bars.Count
            ? ChartTimeFromBars.NormalizeBarOpenTime(_bars.OpenTimes[chartBarIndex]).Minute
            : 0;
        var m15Edge = AlertBarTiming.ComputeM15CloseEdge(
            _chartTfToken, isBarClosed: true, isRealtime: false,
            isNewBarOnRealtimeForming: false, minute, _injectM15CloseEdge);

        return _mtfManager.DescribeTfZoneCatalogFireDebug(
            tfToken,
            chartBarIndex,
            isBarClosed: true,
            isRealtime: false,
            isNewBarOnRealtimeForming: false,
            m15Edge,
            replayHigh,
            replayLow,
            chartClose,
            touchCondId);
    }

    /// <summary>
    /// H1/H4 zone table + cached M5 probes at a chart bar — FireDbg deferred-split / manual analysis.
    /// </summary>
    public string? DescribeHtfTouchProbeTableAtChartBar(
        int chartBarIndex,
        double chartClose,
        double? replayHigh = null,
        double? replayLow = null)
    {
        var minute = chartBarIndex >= 0 && chartBarIndex < _bars.Count
            ? ChartTimeFromBars.NormalizeBarOpenTime(_bars.OpenTimes[chartBarIndex]).Minute
            : 0;
        var m15Edge = AlertBarTiming.ComputeM15CloseEdge(
            _chartTfToken, isBarClosed: true, isRealtime: false,
            isNewBarOnRealtimeForming: false, minute, _injectM15CloseEdge);
        var barOpen = chartBarIndex >= 0 && chartBarIndex < _bars.Count
            ? ChartTimeFromBars.NormalizeBarOpenTime(_bars.OpenTimes[chartBarIndex])
            : DateTime.MinValue;
        var barCloseTime = barOpen != DateTime.MinValue
            ? barOpen.Add(GetBarPeriod())
            : (DateTime?)null;

        return _mtfManager.DescribeHtfTouchProbeTableFireDebug(
            _chartTfToken,
            chartBarIndex,
            isBarClosed: true,
            isRealtime: false,
            isNewBarOnRealtimeForming: false,
            m15Edge,
            barCloseTime,
            replayHigh,
            replayLow,
            chartClose);
    }

    /// <summary>Read access to the compound fire stage ledger (for diagnostics / tests).</summary>
    public IReadOnlyList<CompoundFireStageRecord> CompoundStageLedger => _compoundFire.StageLedger;
    public int CompoundStagedPendingCount => _compoundFire.StagedPendingCount;
    public CompoundWindowMode CompoundWindowMode => _compoundFire.WindowMode;
    public int CompoundAnchorPendingCount => _compoundFire.AnchorPendingCount;
    public IReadOnlyList<CompoundEventAnchor> CompoundAnchorLedger => _compoundFire.AnchorLedger;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _shell.OnStopOrReload();
    }
}
