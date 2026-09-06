using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;
using TradeAlert.Core.Models.Alerts;

namespace TradeAlert.Indicator;

/// <summary>
/// Owns the compound R1–R6 FIRE state and algorithm extracted from
/// <c>TradeAlertLoop6Host</c>. Both the indicator host and the multi-symbol
/// scanner cBot call this orchestrator — there is one canonical implementation.
///
/// The indicator host continues to call <see cref="RunOneTick"/> and then
/// Print/DrawCompoundPanel from its own methods; the cBot collects the returned
/// <see cref="CompoundFireEvent"/> list and routes them to email.
///
/// Cross-TF staging ledger (chart slower than rule min TF, e.g. M15 chart / M5 rules):
/// min-TF sync records pending (slot + event + bar + time); later chart bars retry confirm
/// within EventValidWindow until emitted or expired.
///
/// SYNC NOTICE: the logic inside <see cref="RunOneTick"/>,
/// <see cref="MayFire"/>, and <see cref="EvaluateRules"/> was ported verbatim
/// from the corresponding methods in <c>TradeAlertLoop6Host</c>. If the
/// algorithm changes, update both files.
/// </summary>
public sealed class CompoundFireOrchestrator
{
    // ── State (mirrors TradeAlertLoop6Host compound fields) ──────────────────
    CompoundAlertRule?[] _rulesBySlot = new CompoundAlertRule?[CompoundAlertPresets.Count];
    int[] _lastFiredChartBarIdx = Array.Empty<int>();
    int[] _lastEventBarIdx      = Array.Empty<int>();
    int _minTfMinutes   = 5;
    string _minTfToken  = "5";
    Dictionary<string, AlertConditionId[]> _trackedConditionsPerTf = new();
    CompoundPrimarySource[] _primarySourcesBySlot = Array.Empty<CompoundPrimarySource>();
    readonly HashSet<string> _sourceTfTokens = new(StringComparer.Ordinal);
    readonly HashSet<string> _emittedSourceEventKeys = new(StringComparer.Ordinal);
    CompoundWindowMode _windowMode = CompoundWindowMode.SourceOneShot;
    readonly CompoundEventAnchorLedger _anchorLedger = new();
    /// <summary>
    /// Invoked when a new anchor is created in <see cref="CompoundWindowMode.SetupAtCondFire"/> mode.
    /// The callback receives the anchor immediately after creation (legs set, geometry bar = StagedAtChartBarIndex).
    /// The caller (bot) should resolve B/D at that bar and cache them keyed by <see cref="CompoundEventAnchor.SourceEventKey"/>.
    /// </summary>
    Action<CompoundEventAnchor>? _onAnchorCreatedCallback;

    readonly List<CompoundFireEvent> _pendingEvents = new();
    readonly CompoundFireStageLedger _stageLedger = new();

    Action<string>? _diagLog;
    bool _diagLogEnabled;
    Action<string>? _compoundAuditLog;
    bool _compoundAuditEnabled = true;
    Action<string>? _fireDebugLog;
    bool _fireDebugEnabled;
    readonly Dictionary<(int Slot, int EventBar, int ChartBar, string Sig), bool> _fireDbgConfirmDedup = new();

    // ── Public read accessors ─────────────────────────────────────────────────
    /// <summary>All ledger rows (pending, emitted, expired).</summary>
    public int StageLedgerCount => _stageLedger.Count;

    public int StagedPendingCount => _stageLedger.PendingCount;

    public IReadOnlyList<CompoundFireStageRecord> StageLedger => _stageLedger.Snapshot();

    public CompoundWindowMode WindowMode => _windowMode;

    public int AnchorPendingCount => _anchorLedger.PendingCount;

    public IReadOnlyList<CompoundEventAnchor> AnchorLedger => _anchorLedger.Snapshot();

    public void SetWindowMode(CompoundWindowMode mode) => _windowMode = mode;

    /// <summary>
    /// Register a callback invoked when a new anchor is first created in
    /// <see cref="CompoundWindowMode.SetupAtCondFire"/> mode (cond-fire bar N).
    /// Pass <c>null</c> to clear.
    /// </summary>
    public void SetAnchorCreatedCallback(Action<CompoundEventAnchor>? callback) =>
        _onAnchorCreatedCallback = callback;

    /// <summary>True when ledger already recorded emit for (slot, eventBar).</summary>
    public bool IsStageEventEmitted(int slotIndex, int eventBarIndex) =>
        _stageLedger.IsEventEmitted(slotIndex, eventBarIndex);

    public bool HasAnyRule
    {
        get
        {
            foreach (var r in _rulesBySlot)
                if (r != null) return true;
            return false;
        }
    }

    /// <summary>Slot array exposed for <c>DrawCompoundPanel</c>.</summary>
    public IReadOnlyList<CompoundAlertRule?> RulesBySlot => _rulesBySlot;

    public IReadOnlyDictionary<string, AlertConditionId[]> TrackedConditionsPerTf
        => _trackedConditionsPerTf;

    public string MinTfToken   => _minTfToken;
    public int    MinTfMinutes => _minTfMinutes;

    /// <summary>
    /// Wire a diagnostic logger (e.g. the host's Log delegate). When set, the orchestrator
    /// emits <c>[StageDiag]</c> lines for stage create / refresh / confirm / dedup / expire,
    /// so the user can trace why a rule did or did not produce a FIRE for a given chart bar.
    /// </summary>
    public void SetDiagnosticLogger(Action<string>? log, bool enabled = true)
    {
        _diagLog = log;
        _diagLogEnabled = enabled && log != null;
    }

    /// <summary>
    /// Emits mandatory COMPOUND_* audit lines (RULE_SNAPSHOT, NO_EMIT_REASON, SIGNAL_EMIT).
    /// Enabled by default when <paramref name="log"/> is non-null.
    /// </summary>
    public void SetCompoundAuditLogger(Action<string>? log, bool enabled = true)
    {
        _compoundAuditLog = log;
        _compoundAuditEnabled = enabled && log != null;
    }

    /// <summary>
    /// Emits <c>[FireDbg]</c> lines comparing cache vs fresh leg eval, RealZone detail, and sync timing
    /// when compound FIRE is blocked despite panel STAGE / minLegsOk.
    /// </summary>
    public void SetFireDebugLogger(Action<string>? log, bool enabled = true)
    {
        _fireDebugLog = log;
        _fireDebugEnabled = enabled && log != null;
    }

    // ── Initialisation ────────────────────────────────────────────────────────
    /// <summary>
    /// Parses the six rule strings, derives <c>MinTf</c>, builds the condition
    /// tracking map, and initialises the dedup arrays.
    /// Call once per indicator/cBot start, before the first tick.
    /// </summary>
    public List<CompoundAlertRule> Initialize(
        string r1, string r2, string r3, string r4, string r5, string r6,
        Action<string>? log = null)
    {
        Array.Clear(_rulesBySlot, 0, _rulesBySlot.Length);
        var ruleStrings = new[] { r1, r2, r3, r4, r5, r6 };
        var activeRules = new List<CompoundAlertRule>();

        for (var i = 0; i < ruleStrings.Length; i++)
        {
            if (!CompoundRuleParser.TryParse(
                    ruleStrings[i], i, out var rule, out var err,
                    CompoundAlertPresets.DisplayName(i)))
            {
                log?.Invoke($"[Compound] Rule {i + 1} parse error: {err}");
                continue;
            }

            if (rule.IsEmpty)
                continue;

            foreach (var entry in rule.Entries)
            {
                var warn = CompoundRuleParser.ValidateTfConditionCompat(entry);
                if (warn != null)
                    log?.Invoke($"[Compound] WARN R{i + 1}: {warn}");
            }

            _rulesBySlot[i] = rule;
            activeRules.Add(rule);
        }

        if (activeRules.Count == 0)
            return activeRules;

        CompoundEvalSync.TryGetMinTfFromRules(activeRules, out _minTfMinutes, out _minTfToken);
        _trackedConditionsPerTf = CompoundRuleParser.BuildTrackedConditionsPerTf(activeRules);

        _primarySourcesBySlot = new CompoundPrimarySource[CompoundAlertPresets.Count];
        _sourceTfTokens.Clear();
        for (var i = 0; i < _rulesBySlot.Length; i++)
        {
            var rule = _rulesBySlot[i];
            if (rule == null)
                continue;

            var primary = CompoundRuleSourceResolver.Resolve(rule);
            _primarySourcesBySlot[i] = primary;
            _sourceTfTokens.Add(primary.TfToken);

            if (primary.UsedChartTfFallback)
                log?.Invoke($"[Compound] WARN {rule.RuleName}: no event condition — chart TF fallback");
        }

        _emittedSourceEventKeys.Clear();
        _anchorLedger.Clear();

        _lastFiredChartBarIdx = new int[CompoundAlertPresets.Count];
        _lastEventBarIdx      = new int[CompoundAlertPresets.Count];
        for (var i = 0; i < _lastFiredChartBarIdx.Length; i++)
        {
            _lastFiredChartBarIdx[i] = -1;
            _lastEventBarIdx[i]      = -1;
        }

        _stageLedger.Clear();

        return activeRules;
    }

    // ── Run context ───────────────────────────────────────────────────────────
    /// <summary>
    /// All state that <c>RunOneTick</c> needs from the host/cBot.
    /// Mirrors the parameters scattered across <c>RunCompoundAlerts</c>,
    /// <c>MayFireCompound</c>, and the cTrader indicator API.
    /// </summary>
    public readonly struct FireRunContext
    {
        public readonly string Symbol;
        public readonly Bars   Bars;
        public readonly string ChartTfToken;
        public readonly int    Index;
        public readonly double SymbolTickSize;
        public readonly DateTime BarOpenTimeLocal;  // ChartTimeFromBars.NormalizeBarOpenTime(…)
        public readonly TimeSpan BarPeriod;
        public readonly bool LiveFormingLastBar;
        public readonly bool IsNewBarOnRealtimeForming;
        public readonly bool IsBackfillCompleted;
        public readonly int  RealtimeLastBarHits;
        public readonly bool IsLastBar;
        public readonly bool IsRealtimeMode;          // RunningMode == RunningMode.RealTime
        public readonly bool IsVisualBacktesting;     // RunningMode == RunningMode.VisualBacktesting
        public readonly bool InjectM15CloseEdge;
        public readonly CompoundEvalSyncMode SyncMode;
        public readonly int EventValidBars;

        /// <summary>
        /// When set, HTF engines advance only through this simulated chart-local close time
        /// (VisualBacktesting M1 replay — one or few min-TF bars per pass).
        /// </summary>
        public readonly DateTime? SimulatedChartCloseTimeLocal;

        /// <summary>Partial chart OHLC during intra-bar replay (overrides <see cref="Bars"/> extremes).</summary>
        public readonly double? ReplayChartHigh;
        public readonly double? ReplayChartLow;

        public FireRunContext(
            string symbol,
            Bars bars,
            string chartTfToken,
            int index,
            double symbolTickSize,
            DateTime barOpenTimeLocal,
            TimeSpan barPeriod,
            bool liveFormingLastBar,
            bool isNewBarOnRealtimeForming,
            bool isBackfillCompleted,
            int realtimeLastBarHits,
            bool isLastBar,
            bool isRealtimeMode,
            bool isVisualBacktesting,
            bool injectM15CloseEdge,
            CompoundEvalSyncMode syncMode,
            int eventValidBars,
            DateTime? simulatedChartCloseTimeLocal = null,
            double? replayChartHigh = null,
            double? replayChartLow = null)
        {
            Symbol                     = symbol ?? "";
            Bars                       = bars;
            ChartTfToken               = chartTfToken;
            Index                      = index;
            SymbolTickSize             = symbolTickSize;
            BarOpenTimeLocal           = barOpenTimeLocal;
            BarPeriod                  = barPeriod;
            LiveFormingLastBar         = liveFormingLastBar;
            IsNewBarOnRealtimeForming  = isNewBarOnRealtimeForming;
            IsBackfillCompleted        = isBackfillCompleted;
            RealtimeLastBarHits        = realtimeLastBarHits;
            IsLastBar                  = isLastBar;
            IsRealtimeMode             = isRealtimeMode;
            IsVisualBacktesting        = isVisualBacktesting;
            InjectM15CloseEdge         = injectM15CloseEdge;
            SyncMode                   = syncMode;
            EventValidBars             = eventValidBars;
            SimulatedChartCloseTimeLocal = simulatedChartCloseTimeLocal;
            ReplayChartHigh            = replayChartHigh;
            ReplayChartLow             = replayChartLow;
        }
    }

    // ── Main entry point ──────────────────────────────────────────────────────
    /// <summary>
    /// Runs one compound evaluation tick (mirrors <c>RunCompoundAlerts</c>):
    /// <list type="number">
    ///   <item>Advance HTF engines; stage ledger rows on each min-TF close.</item>
    ///   <item>Update realtime price on HTF entries if on last bar.</item>
    ///   <item>Update condition cache for the chart TF.</item>
    ///   <item>Retry confirm all pending ledger rows within event window; try fire at chart close.</item>
    /// </list>
    /// Returns the events that fired this tick.
    /// The caller is responsible for Print/email/panel — this method never draws or logs.
    /// </summary>
    /// <param name="mtf">The per-symbol <see cref="MtfEngineManager"/>.</param>
    /// <param name="ctx">Snapshot of host/cBot state for this tick.</param>
    /// <param name="onMinTfClosedCallback">
    /// Invoked after each closed min-TF bar (e.g. so the indicator can redraw
    /// the compound panel mid-pass). Pass <c>null</c> from the cBot.
    /// </param>
    public IReadOnlyList<CompoundFireEvent> RunOneTick(
        MtfEngineManager mtf,
        in FireRunContext ctx,
        Action? onMinTfClosedCallback = null)
    {
        _pendingEvents.Clear();

        if (!HasAnyRule)
            return _pendingEvents;

        mtf.SetCurrentChartBarIndex(ctx.Index);

        var sourceCandidates = new List<CompoundSourceEventCandidate>();

        var barCloseTime  = ctx.SimulatedChartCloseTimeLocal ?? ctx.BarOpenTimeLocal.Add(ctx.BarPeriod);
        var isBarClosed   = !ctx.LiveFormingLastBar;
        var isRealtime    = ctx.LiveFormingLastBar;
        var isIntraBarReplay = ctx.SimulatedChartCloseTimeLocal.HasValue;
        var chartM15Edge = AlertBarTiming.ComputeM15CloseEdge(
            ctx.ChartTfToken,
            isBarClosed,
            isRealtime,
            ctx.IsNewBarOnRealtimeForming,
            ctx.BarOpenTimeLocal.Minute,
            ctx.InjectM15CloseEdge);
        var chartRealtimeEdge = ctx.LiveFormingLastBar || ctx.IsVisualBacktesting;

        // A. Advance HTF engines — collect source-close candidates only (no fire in callback).
        var minTfBarsClosedThisPass = mtf.AdvanceHtfEnginesToChartBarCloseTime(
            barCloseTime,
            _trackedConditionsPerTf,
            chartRealtimeEdge,
            _minTfToken,
            (htfTfToken, htfBarIdx, _) =>
            {
                if (_sourceTfTokens.Contains(htfTfToken)
                    && mtf.TryBuildSourceEventCandidate(htfTfToken, htfBarIdx, out var candidate))
                {
                    sourceCandidates.Add(candidate);
                }

                onMinTfClosedCallback?.Invoke();
            });

        // B. Sync forming HTF + realtime price + chart condition cache.
        mtf.SyncHtfFormingBarsToChartCloseTime(
            barCloseTime,
            _trackedConditionsPerTf,
            chartRealtimeEdge,
            chartM15Edge);

        var shouldSimIntraBarFromOhlc = (isBarClosed && !ctx.IsRealtimeMode) || isIntraBarReplay;
        var shouldUpdateRealtimeFromChart = shouldSimIntraBarFromOhlc
            || ctx.Index == ctx.Bars.Count - 1
            || isIntraBarReplay;

        if (shouldUpdateRealtimeFromChart)
        {
            var chartHigh = ctx.ReplayChartHigh ?? ctx.Bars.HighPrices[ctx.Index];
            var chartLow  = ctx.ReplayChartLow ?? ctx.Bars.LowPrices[ctx.Index];
            mtf.UpdateRealtimePriceFromChart(
                ctx.Index,
                chartHigh,
                chartLow,
                ctx.SymbolTickSize,
                _trackedConditionsPerTf,
                chartM15Edge,
                updateChartTfEntry: shouldSimIntraBarFromOhlc || ctx.LiveFormingLastBar || isIntraBarReplay,
                chartBarCloseTimeChartLocal: barCloseTime);
        }

        if (_trackedConditionsPerTf.TryGetValue(ctx.ChartTfToken, out var chartConds))
        {
            mtf.UpdateConditionCache(
                ctx.ChartTfToken,
                ctx.Index,
                ctx.Bars.OpenTimes[ctx.Index],
                isBarClosed,
                isRealtime,
                ctx.IsNewBarOnRealtimeForming,
                chartM15Edge,
                chartConds,
                reevalStateOnBarClose: !shouldSimIntraBarFromOhlc);
        }

        // Chart TF source candidate when chart bar closes and chart TF is a primary source.
        if (isBarClosed
            && _sourceTfTokens.Contains(ctx.ChartTfToken)
            && mtf.TryBuildSourceEventCandidate(ctx.ChartTfToken, ctx.Index, out var chartCandidate))
        {
            sourceCandidates.Add(chartCandidate);
        }

        // C. Source evaluation — mode-dependent.
        if (_windowMode == CompoundWindowMode.SourceOneShot)
        {
            RunSourceOneShotPass(sourceCandidates, mtf, in ctx, chartM15Edge, chartRealtimeEdge,
                minTfBarsClosedThisPass);
        }
        else
        {
            // AnchorLatchWindow and SetupAtCondFire share the same anchor/latch/confirm path.
            // SetupAtCondFire additionally fires _onAnchorCreatedCallback and overrides the
            // emit ChartBarIndex to anchor.StagedAtChartBarIndex (geometry bar = cond bar N).
            RunAnchorLatchWindowPass(sourceCandidates, mtf, in ctx, chartM15Edge, chartRealtimeEdge,
                minTfBarsClosedThisPass);
        }

        return _pendingEvents;
    }

    void RunSourceOneShotPass(
        List<CompoundSourceEventCandidate> sourceCandidates,
        MtfEngineManager mtf,
        in FireRunContext ctx,
        bool chartM15Edge,
        bool chartRealtimeEdge,
        int minTfBarsClosedThisPass)
    {
        var seenCandidateBars = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in sourceCandidates.OrderBy(c => c.SourceBarCloseTimeUtc))
        {
            var barDedupe = CompoundSourceEventKey.CandidateBarDedupeKey(in candidate);
            if (!seenCandidateBars.Add(barDedupe))
                continue;

            ProcessSourceEventCandidate(
                candidate,
                in ctx,
                mtf,
                chartM15Edge,
                chartRealtimeEdge,
                candidateClosedThisPass: true,
                minTfBarsClosedThisPass);
        }
    }

    void RunAnchorLatchWindowPass(
        List<CompoundSourceEventCandidate> sourceCandidates,
        MtfEngineManager mtf,
        in FireRunContext ctx,
        bool chartM15Edge,
        bool chartRealtimeEdge,
        int minTfBarsClosedThisPass)
    {
        var seenCandidateBars = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in sourceCandidates.OrderBy(c => c.SourceBarCloseTimeUtc))
        {
            var barDedupe = CompoundSourceEventKey.CandidateBarDedupeKey(in candidate);
            if (!seenCandidateBars.Add(barDedupe))
                continue;

            if (!MayFireSourceCandidate(in ctx, candidateClosedThisPass: true))
                continue;

            TryCreateAnchorsFromSourceCandidate(
                candidate, mtf, in ctx, chartM15Edge, chartRealtimeEdge);
        }

        ConfirmPendingAnchors(mtf, in ctx, chartM15Edge, chartRealtimeEdge, minTfBarsClosedThisPass);
    }

    void TryCreateAnchorsFromSourceCandidate(
        CompoundSourceEventCandidate candidate,
        MtfEngineManager mtf,
        in FireRunContext ctx,
        bool chartM15Edge,
        bool chartRealtimeEdge)
    {
        mtf.UpdateStateCachesAtSourceClose(
            candidate,
            _trackedConditionsPerTf,
            chartM15Edge,
            chartRealtimeEdge);

        // Log once per unique (tf, sourceBar) — placed after state update so the log
        // only appears when at least one slot will actually consider this candidate.
        var loggedCandidate = false;

        for (var slot = 0; slot < _rulesBySlot.Length; slot++)
        {
            var rule = _rulesBySlot[slot];
            if (rule == null)
                continue;

            var primary = _primarySourcesBySlot[slot];
            if (!string.Equals(primary.TfToken, candidate.SourceTfToken, StringComparison.Ordinal))
                continue;

            if (!TryResolvePrimaryEventOnCandidate(mtf, primary, candidate, out _))
                continue;

            var ruleId = CompoundRuleSourceResolver.ResolveRuleId(rule);
            var symbol = string.IsNullOrWhiteSpace(ctx.Symbol) ? mtf.Symbol : ctx.Symbol;
            var sourceEventKey = CompoundSourceEventKey.Build(
                symbol, ruleId, rule.Direction, candidate.SourceTfToken, candidate.SourceBarIndex);

            if (_emittedSourceEventKeys.Contains(sourceEventKey)
                || _anchorLedger.IsEmitted(sourceEventKey)
                || _anchorLedger.HasActive(sourceEventKey))
                continue;

            if (!loggedCandidate)
            {
                LogCompoundSourceCandidate(candidate);
                LogCompoundStateRefresh(mtf, candidate);
                loggedCandidate = true;
            }

            if (!CompoundEvalSync.IsEvalSyncPointForSourceCandidate(
                    ctx.ChartTfToken, candidate, candidateClosedThisPass: true, ctx.IsBackfillCompleted))
                continue;

            var anchor = BuildAnchorFromSnapshot(
                rule, slot, primary, candidate, symbol, ruleId, sourceEventKey, in ctx, mtf);

            if (anchor.AllStateLegsLatched())
            {
                LogCompoundAnchorCreate(anchor);

                if (_windowMode == CompoundWindowMode.SetupAtCondFire)
                    _onAnchorCreatedCallback?.Invoke(anchor);

                LogCompoundAnchorConfirm(anchor, in ctx, allLatchedAtCreate: true);
                EmitFromAnchor(anchor, rule, in ctx, mtf, minTfBarsClosedThisPass: 0, sourceEventKey);
                continue;
            }

            // TradingView sync: all legs must be OK at cond-fire moment — no pending / retry window.
            if (ctx.SyncMode == CompoundEvalSyncMode.TradingView)
            {
                LogCompoundAnchorSkip(anchor, in ctx, "state-not-all-ok-at-cond-fire");
                continue;
            }

            LogCompoundAnchorCreate(anchor);

            // PineEventWindow + SetupAtCondFire: pin B/D at cond bar N even when state legs still pending.
            if (_windowMode == CompoundWindowMode.SetupAtCondFire)
                _onAnchorCreatedCallback?.Invoke(anchor);

            _anchorLedger.UpsertPending(anchor);
        }
    }

    CompoundEventAnchor BuildAnchorFromSnapshot(
        CompoundAlertRule rule,
        int slot,
        CompoundPrimarySource primary,
        CompoundSourceEventCandidate candidate,
        string symbol,
        string ruleId,
        string sourceEventKey,
        in FireRunContext ctx,
        MtfEngineManager mtf)
    {
        var legStates = new Dictionary<string, CompoundLegLatchState>(StringComparer.Ordinal);
        foreach (var entry in rule.Entries)
        {
            if (!CompoundRuleParser.IsStateCondition(entry.ConditionId))
                continue;

            var legKey = CompoundEventAnchor.LegKey(entry.TfToken, entry.ConditionId);
            legStates[legKey] = mtf.IsStateConditionActive(entry.TfToken, entry.ConditionId)
                ? CompoundLegLatchState.LatchedOk
                : CompoundLegLatchState.Pending;
        }

        return new CompoundEventAnchor
        {
            SourceEventKey = sourceEventKey,
            Symbol = symbol,
            RuleId = ruleId,
            SlotIndex = slot,
            Direction = rule.Direction,
            SourceTfToken = candidate.SourceTfToken,
            SourceBarIndex = candidate.SourceBarIndex,
            SourceBarOpenTimeUtc = candidate.SourceBarOpenTimeUtc,
            SourceBarCloseTimeUtc = candidate.SourceBarCloseTimeUtc,
            StagedAtChartBarIndex = ctx.Index,
            ExpiresAtChartBarIndex = CompoundEventAnchorLedger.UsesSourceBarConfirmWindow(
                candidate.SourceTfToken, slot)
                ? -1
                : CompoundEventAnchorLedger.ComputeExpiresAtChartBar(ctx.Index, ctx.EventValidBars),
            ExpiresAtSourceBarIndex = CompoundEventAnchorLedger.UsesSourceBarConfirmWindow(
                candidate.SourceTfToken, slot)
                ? CompoundEventAnchorLedger.ComputeExpiresAtSourceBar(
                    candidate.SourceBarIndex, ctx.EventValidBars)
                : -1,
            PrimaryEventCondition = primary.PrimaryEventCondition,
            Status = CompoundEventAnchorStatus.Pending,
            LegLatchStates = legStates,
        };
    }

    void ConfirmPendingAnchors(
        MtfEngineManager mtf,
        in FireRunContext ctx,
        bool chartM15Edge,
        bool chartRealtimeEdge,
        int minTfBarsClosedThisPass)
    {
        if (!MayFireSourceCandidate(in ctx, candidateClosedThisPass: true))
            return;

        foreach (var expired in _anchorLedger.ExpireOutsideWindow(
            ctx.Index, a => ResolveAnchorSourceBarIndex(mtf, a)))
            LogCompoundAnchorExpire(expired, in ctx);

        _anchorLedger.PurgeHistory(ctx.Index, ctx.EventValidBars);

        // TradingView sync: anchor is one-shot at cond fire — no pending-leg retry pass.
        if (ctx.SyncMode == CompoundEvalSyncMode.TradingView)
            return;

        foreach (var anchor in _anchorLedger.GetPendingForConfirm(
            ctx.Index, a => ResolveAnchorSourceBarIndex(mtf, a)))
        {
            var rule = _rulesBySlot[anchor.SlotIndex];
            if (rule == null)
                continue;

            if (_emittedSourceEventKeys.Contains(anchor.SourceEventKey))
                continue;

            var newlyLatched = RetryPendingLegs(anchor, mtf);
            foreach (var legKey in newlyLatched)
                LogCompoundAnchorLatch(anchor, legKey);

            if (!anchor.AllStateLegsLatched())
                continue;

            LogCompoundAnchorConfirm(anchor, in ctx, allLatchedAtCreate: false);
            EmitFromAnchor(anchor, rule, in ctx, mtf, minTfBarsClosedThisPass, anchor.SourceEventKey);
        }
    }

    static List<string> RetryPendingLegs(CompoundEventAnchor anchor, MtfEngineManager mtf)
    {
        var newlyLatched = new List<string>();
        foreach (var kv in anchor.LegLatchStates)
        {
            if (kv.Value != CompoundLegLatchState.Pending)
                continue;

            var colon = kv.Key.IndexOf(':');
            if (colon <= 0 || colon >= kv.Key.Length - 1)
                continue;

            var tfToken = kv.Key[..colon];
            if (!Enum.TryParse<AlertConditionId>(kv.Key[(colon + 1)..], out var condId))
                continue;

            if (!mtf.IsStateConditionActive(tfToken, condId))
                continue;

            anchor.LegLatchStates[kv.Key] = CompoundLegLatchState.LatchedOk;
            newlyLatched.Add(kv.Key);
        }

        return newlyLatched;
    }

    void EmitFromAnchor(
        CompoundEventAnchor anchor,
        CompoundAlertRule rule,
        in FireRunContext ctx,
        MtfEngineManager mtf,
        int minTfBarsClosedThisPass,
        string sourceEventKey)
    {
        if (_emittedSourceEventKeys.Contains(sourceEventKey))
            return;

        // Guard: prevent multiple anchors for the same slot from firing on the same chart bar.
        // (e.g. two M5 sourceBar anchors for R2 BUY both confirming at chartBar=223)
        if (_lastFiredChartBarIdx[anchor.SlotIndex] == ctx.Index)
        {
            WriteCompoundAudit(
                $"COMPOUND_ANCHOR_SKIP_SAME_BAR key={sourceEventKey} " +
                $"rule={anchor.RuleId} chartBar={ctx.Index} reason=slot-already-fired-this-bar");
            return;
        }

        _emittedSourceEventKeys.Add(sourceEventKey);
        _anchorLedger.MarkEmitted(sourceEventKey, ctx.Index);

        var candidate = new CompoundSourceEventCandidate
        {
            Symbol = anchor.Symbol,
            SourceTfToken = anchor.SourceTfToken,
            SourceBarIndex = anchor.SourceBarIndex,
            SourceBarOpenTimeUtc = anchor.SourceBarOpenTimeUtc,
            SourceBarCloseTimeUtc = anchor.SourceBarCloseTimeUtc,
        };

        // SetupAtCondFire: geometry bar = cond-fire chart bar (StagedAtChartBarIndex),
        // which may differ from the confirm bar (ctx.Index). Pass it via SetupBarIndex so the
        // bot uses cond-bar OHLC for D-fire lookup and knows to use pinned B.
        var setupBarIndex = _windowMode == CompoundWindowMode.SetupAtCondFire
            ? anchor.StagedAtChartBarIndex
            : -1;

        EmitCompoundSignal(
            anchor.SlotIndex,
            rule,
            anchor.RuleId,
            anchor.Symbol,
            candidate,
            in ctx,
            mtf,
            minTfBarsClosedThisPass,
            sourceEventKey,
            setupBarIndex);
    }

    void LogCompoundAnchorCreate(CompoundEventAnchor anchor)
    {
        var pending = string.Join(",", anchor.PendingLegKeys());
        var latched = new List<string>();
        foreach (var kv in anchor.LegLatchStates)
        {
            if (kv.Value == CompoundLegLatchState.LatchedOk)
                latched.Add(kv.Key);
        }

        WriteCompoundAudit(
            $"COMPOUND_ANCHOR_CREATE key={anchor.SourceEventKey} " +
            $"rule={anchor.RuleId} dir={CompoundSourceEventKey.FormatDirection(anchor.Direction)} " +
            $"sourceTf={CompoundSourceEventKey.FormatSourceTf(anchor.SourceTfToken)} " +
            $"sourceBar={anchor.SourceBarIndex} stagedAtChart={anchor.StagedAtChartBarIndex} " +
            (anchor.ExpiresAtSourceBarIndex >= 0
                ? $"expiresAtSource={anchor.ExpiresAtSourceBarIndex} windowTf=M5"
                : $"expiresAtChart={anchor.ExpiresAtChartBarIndex}") +
            $" latched=[{string.Join(",", latched)}] pending=[{pending}]");
    }

    static int ResolveAnchorSourceBarIndex(MtfEngineManager mtf, CompoundEventAnchor anchor) =>
        anchor.ExpiresAtSourceBarIndex >= 0
            ? mtf.GetSyncBarIndex(anchor.SourceTfToken)
            : -1;

    void LogCompoundAnchorLatch(CompoundEventAnchor anchor, string legKey)
    {
        WriteCompoundAudit(
            $"COMPOUND_ANCHOR_LATCH key={anchor.SourceEventKey} rule={anchor.RuleId} " +
            $"sourceBar={anchor.SourceBarIndex} leg={legKey}");
    }

    void LogCompoundAnchorConfirm(CompoundEventAnchor anchor, in FireRunContext ctx, bool allLatchedAtCreate)
    {
        WriteCompoundAudit(
            $"COMPOUND_ANCHOR_CONFIRM key={anchor.SourceEventKey} rule={anchor.RuleId} " +
            $"sourceTf={CompoundSourceEventKey.FormatSourceTf(anchor.SourceTfToken)} " +
            $"sourceBar={anchor.SourceBarIndex} chartBar={ctx.Index} atCreate={allLatchedAtCreate}");
    }

    void LogCompoundAnchorExpire(CompoundEventAnchor anchor, in FireRunContext ctx)
    {
        WriteCompoundAudit(
            $"COMPOUND_ANCHOR_EXPIRE key={anchor.SourceEventKey} rule={anchor.RuleId} " +
            $"sourceBar={anchor.SourceBarIndex} chartBar={ctx.Index} " +
            $"pending=[{string.Join(",", anchor.PendingLegKeys())}]");
    }

    void LogCompoundAnchorSkip(CompoundEventAnchor anchor, in FireRunContext ctx, string reason)
    {
        WriteCompoundAudit(
            $"COMPOUND_ANCHOR_SKIP key={anchor.SourceEventKey} rule={anchor.RuleId} " +
            $"sourceTf={CompoundSourceEventKey.FormatSourceTf(anchor.SourceTfToken)} " +
            $"sourceBar={anchor.SourceBarIndex} chartBar={ctx.Index} reason={reason} " +
            $"pending=[{string.Join(",", anchor.PendingLegKeys())}]");
    }

    void ProcessSourceEventCandidate(
        CompoundSourceEventCandidate candidate,
        in FireRunContext ctx,
        MtfEngineManager mtf,
        bool chartM15CloseEdgeInjected,
        bool chartRealtimeEdge,
        bool candidateClosedThisPass,
        int minTfBarsClosedThisPass)
    {
        if (!MayFireSourceCandidate(in ctx, candidateClosedThisPass))
            return;

        LogCompoundSourceCandidate(candidate);

        mtf.UpdateStateCachesAtSourceClose(
            candidate,
            _trackedConditionsPerTf,
            chartM15CloseEdgeInjected,
            chartRealtimeEdge);

        LogCompoundStateRefresh(mtf, candidate);

        for (var slot = 0; slot < _rulesBySlot.Length; slot++)
        {
            var rule = _rulesBySlot[slot];
            if (rule == null)
                continue;

            var primary = _primarySourcesBySlot[slot];
            if (!string.Equals(primary.TfToken, candidate.SourceTfToken, StringComparison.Ordinal))
                continue;

            EvaluateAndMaybeEmitFromSourceCandidate(
                rule, slot, primary, candidate, in ctx, mtf, minTfBarsClosedThisPass);
        }
    }

    bool MayFireSourceCandidate(in FireRunContext ctx, bool candidateClosedThisPass)
    {
        if (!ctx.IsBackfillCompleted || !candidateClosedThisPass)
            return false;

        if (ctx.IsRealtimeMode)
        {
            if (!(ctx.IsLastBar && ctx.Index == ctx.Bars.Count - 1))
                return false;
            return ctx.RealtimeLastBarHits >= 2;
        }

        return true;
    }

    void EvaluateAndMaybeEmitFromSourceCandidate(
        CompoundAlertRule rule,
        int slot,
        CompoundPrimarySource primary,
        CompoundSourceEventCandidate candidate,
        in FireRunContext ctx,
        MtfEngineManager mtf,
        int minTfBarsClosedThisPass)
    {
        var ruleId = CompoundRuleSourceResolver.ResolveRuleId(rule);
        var symbol = string.IsNullOrWhiteSpace(ctx.Symbol) ? mtf.Symbol : ctx.Symbol;
        var sourceEventKey = CompoundSourceEventKey.Build(symbol, ruleId, rule.Direction,
            candidate.SourceTfToken, candidate.SourceBarIndex);

        var options = new CompoundRuntimeOptions
        {
            SyncMode = ctx.SyncMode,
            EventValidBars = ctx.EventValidBars,
            ChartBarIndex = ctx.Index,
        };

        if (!TryResolvePrimaryEventOnCandidate(mtf, primary, candidate, out var primaryMissing))
        {
            LogCompoundRuleSnapshot(ruleId, rule.Direction, candidate,
                new CompoundRuleEvalResult { AllTrue = false, MissingLegs = primaryMissing });
            LogCompoundNoEmit(ruleId, rule.Direction, candidate, "EVENT_NOT_ACTIVE", primaryMissing);
            return;
        }

        if (_stageLedger.IsSourceEventEmitted(sourceEventKey))
        {
            LogCompoundNoEmit(ruleId, rule.Direction, candidate, "STAGE_MISS", Array.Empty<string>());
            return;
        }

        if (_emittedSourceEventKeys.Contains(sourceEventKey))
        {
            LogCompoundNoEmit(ruleId, rule.Direction, candidate, "DEDUP_SOURCE_EVENT", Array.Empty<string>());
            return;
        }

        if (!CompoundEvalSync.IsEvalSyncPointForSourceCandidate(
                ctx.ChartTfToken, candidate, candidateClosedThisPass: true, ctx.IsBackfillCompleted))
        {
            LogCompoundNoEmit(ruleId, rule.Direction, candidate, "SYNC_MISS", Array.Empty<string>());
            return;
        }

        var result = CompoundRuleEvaluator.EvaluateRuleAtSnapshot(
            mtf, rule, candidate, primary, options);

        LogCompoundRuleSnapshot(ruleId, rule.Direction, candidate, result);

        if (!result.AllTrue)
        {
            LogCompoundNoEmit(ruleId, rule.Direction, candidate, "CONDITION_MISS", result.MissingLegs);
            if (_fireDebugEnabled)
                LogFireDebugSyncMissForCandidate(mtf, in ctx, slot, rule, candidate, minTfBarsClosedThisPass);
            return;
        }

        _emittedSourceEventKeys.Add(sourceEventKey);
        EmitCompoundSignal(
            slot, rule, ruleId, symbol, candidate, in ctx, mtf, minTfBarsClosedThisPass, sourceEventKey);
    }

    static bool TryResolvePrimaryEventOnCandidate(
        MtfEngineManager mtf,
        CompoundPrimarySource primary,
        CompoundSourceEventCandidate candidate,
        out IReadOnlyList<string> missing)
    {
        var legKey = $"{candidate.SourceTfToken}:{primary.PrimaryEventCondition}";
        if (!mtf.TryGetEventFiredBarIndex(primary.TfToken, primary.PrimaryEventCondition, out var firedBar))
        {
            missing = new[] { legKey };
            return false;
        }

        if (firedBar != candidate.SourceBarIndex)
        {
            missing = new[] { $"{legKey} (fired={firedBar} candidate={candidate.SourceBarIndex})" };
            return false;
        }

        missing = Array.Empty<string>();
        return true;
    }

    void EmitCompoundSignal(
        int slot,
        CompoundAlertRule rule,
        string ruleId,
        string symbol,
        CompoundSourceEventCandidate candidate,
        in FireRunContext ctx,
        MtfEngineManager mtf,
        int minTfBarsClosedThisPass,
        string sourceEventKey,
        int setupBarIndex = -1)
    {
        var chartBarIdx = ctx.Index;
        var barOpenLocal = ctx.BarOpenTimeLocal;
        var close = ctx.Bars.ClosePrices[chartBarIdx];

        _lastFiredChartBarIdx[slot] = chartBarIdx;
        _lastEventBarIdx[slot] = candidate.SourceBarIndex;

        _stageLedger.MarkSourceEmitted(new CompoundFireStageRecord
        {
            Symbol = symbol,
            RuleId = ruleId,
            SlotIndex = slot,
            RuleName = rule.RuleName,
            Direction = rule.Direction,
            ChartTfToken = ctx.ChartTfToken,
            SourceTfToken = candidate.SourceTfToken,
            SourceBarIndex = candidate.SourceBarIndex,
            SourceEventOpenTimeUtc = candidate.SourceBarOpenTimeUtc,
            EmittedAtChartBarIndex = chartBarIdx,
            EmittedAtChartBarOpenTime = barOpenLocal,
        });

        _pendingEvents.Add(new CompoundFireEvent(
            SlotIndex:      slot,
            RuleName:       rule.RuleName,
            Direction:      rule.Direction,
            ChartTfToken:   ctx.ChartTfToken,
            ChartBarIndex:  chartBarIdx,
            BarOpenTime:    barOpenLocal,
            Price:          close,
            EventBarIndex:  candidate.SourceBarIndex,
            SourceTfToken:  candidate.SourceTfToken,
            SourceEventKey: sourceEventKey,
            SetupBarIndex:  setupBarIndex));

        LogCompoundSignalEmit(ruleId, rule.Direction, candidate, sourceEventKey);

        if (_diagLogEnabled)
            _diagLog!.Invoke(
                $"[StageDiag] R{slot + 1} {rule.Direction} EMIT-OK " +
                $"chart={chartBarIdx} sourceTf={candidate.SourceTfToken} " +
                $"sourceBar={candidate.SourceBarIndex} close={close}");

        if (_fireDebugEnabled)
        {
            LogFireDebug(
                "EMIT-OK",
                slot,
                rule,
                in ctx,
                minTfBarsClosedThisPass,
                FormattableString.Invariant(
                    $"sourceTf={candidate.SourceTfToken} sourceBar={candidate.SourceBarIndex} close={close:F5}"),
                mtf,
                includeAllLegs: true);
        }
    }

    void WriteCompoundAudit(string message)
    {
        if (!_compoundAuditEnabled || _compoundAuditLog == null)
            return;

        _compoundAuditLog.Invoke(message);
    }

    void LogCompoundSourceCandidate(CompoundSourceEventCandidate candidate)
    {
        WriteCompoundAudit(
            $"COMPOUND_SOURCE_CANDIDATE symbol={candidate.Symbol} " +
            $"sourceTf={CompoundSourceEventKey.FormatSourceTf(candidate.SourceTfToken)} " +
            $"sourceBar={candidate.SourceBarIndex} openTime={candidate.SourceBarOpenTimeUtc:s} " +
            $"closeTime={candidate.SourceBarCloseTimeUtc:s} O={candidate.Open} H={candidate.High} " +
            $"L={candidate.Low} C={candidate.Close}");
    }

    void LogCompoundStateRefresh(MtfEngineManager mtf, CompoundSourceEventCandidate candidate)
    {
        WriteCompoundAudit(
            $"COMPOUND_STATE_REFRESH " +
            $"sourceTf={CompoundSourceEventKey.FormatSourceTf(candidate.SourceTfToken)} " +
            $"sourceBar={candidate.SourceBarIndex} snapshotTime={candidate.SourceBarCloseTimeUtc:s} " +
            $"{mtf.FormatStateRefreshLine("5", AlertConditionId.CanBuyReal)} " +
            $"{mtf.FormatStateRefreshLine("5", AlertConditionId.CanSellReal)} " +
            $"{mtf.FormatStateRefreshLine("15", AlertConditionId.CanBuyReal)} " +
            $"{mtf.FormatStateRefreshLine("15", AlertConditionId.CanSellReal)} " +
            $"{mtf.FormatStateRefreshLine("60", AlertConditionId.CanBuyTouchM5)} " +
            $"{mtf.FormatStateRefreshLine("60", AlertConditionId.CanSellTouchM5)} " +
            $"{mtf.FormatStateRefreshLine("240", AlertConditionId.CanBuyTouchM5)} " +
            $"{mtf.FormatStateRefreshLine("240", AlertConditionId.CanSellTouchM5)}");
    }

    void LogCompoundRuleSnapshot(
        string ruleId,
        SignalDirection direction,
        CompoundSourceEventCandidate candidate,
        CompoundRuleEvalResult result)
    {
        WriteCompoundAudit(
            $"COMPOUND_RULE_SNAPSHOT rule={ruleId} dir={CompoundSourceEventKey.FormatDirection(direction)} " +
            $"sourceTf={CompoundSourceEventKey.FormatSourceTf(candidate.SourceTfToken)} " +
            $"sourceBar={candidate.SourceBarIndex} allTrue={result.AllTrue} " +
            CompoundRuleEvaluator.FormatLegSnapshot(result.Legs));
    }

    void LogCompoundSignalEmit(
        string ruleId,
        SignalDirection direction,
        CompoundSourceEventCandidate candidate,
        string sourceEventKey)
    {
        WriteCompoundAudit(
            $"COMPOUND_SIGNAL_EMIT rule={ruleId} dir={CompoundSourceEventKey.FormatDirection(direction)} " +
            $"sourceTf={CompoundSourceEventKey.FormatSourceTf(candidate.SourceTfToken)} " +
            $"sourceBar={candidate.SourceBarIndex} key={sourceEventKey}");
    }

    void LogCompoundNoEmit(
        string ruleId,
        SignalDirection direction,
        CompoundSourceEventCandidate candidate,
        string reason,
        IReadOnlyList<string> missing)
    {
        var missingText = missing.Count > 0 ? $" missing=[{string.Join(",", missing)}]" : "";
        WriteCompoundAudit(
            $"COMPOUND_NO_EMIT_REASON rule={ruleId} dir={CompoundSourceEventKey.FormatDirection(direction)} " +
            $"sourceTf={CompoundSourceEventKey.FormatSourceTf(candidate.SourceTfToken)} " +
            $"sourceBar={candidate.SourceBarIndex} reason={reason}{missingText}");
    }

    void LogFireDebugSyncMissForCandidate(
        MtfEngineManager mtf,
        in FireRunContext ctx,
        int slot,
        CompoundAlertRule rule,
        CompoundSourceEventCandidate candidate,
        int minTfBarsClosedThisPass)
    {
        var inactive = BuildInactiveLegSignature(mtf, rule, in ctx);
        LogFireDebug(
            "SYNC-MISS",
            slot,
            rule,
            in ctx,
            minTfBarsClosedThisPass,
            $"sourceTf={candidate.SourceTfToken} sourceBar={candidate.SourceBarIndex} blockers=[{inactive}]",
            mtf,
            includeAllLegs: true);
    }

    // ── Internal helpers (ported 1:1 from TradeAlertLoop6Host) ───────────────

    // Mirrors MayFireCompound in TradeAlertLoop6Host.cs
    bool MayFire(
        in FireRunContext ctx,
        bool isBarClosed,
        int minTfBarsClosedThisPass)
    {
        if (!ctx.IsBackfillCompleted)
            return false;

        if (!CompoundEvalSync.IsEvalSyncPoint(
                ctx.ChartTfToken,
                _minTfMinutes,
                isBarClosed,
                ctx.IsNewBarOnRealtimeForming,
                ctx.BarOpenTimeLocal.Minute,
                minTfBarsClosedThisPass))
            return false;

        if (ctx.IsRealtimeMode)
        {
            if (!(ctx.IsLastBar && ctx.Index == ctx.Bars.Count - 1))
                return false;
            return ctx.RealtimeLastBarHits >= 2;
        }

        // Backtest replay: each Calculate(index) is the current moment in time.
        return true;
    }

    // Mirrors TryFireCompoundAtSyncPoint in TradeAlertLoop6Host.cs
    void TryFireAtSyncPoint(
        MtfEngineManager mtf,
        in FireRunContext ctx,
        bool isBarClosed,
        DateTime barCloseTime,
        int minTfBarsClosed,
        int minTfBarsClosedThisPass)
    {
        if (!MayFire(in ctx, isBarClosed, minTfBarsClosed))
            return;

        EvaluateRules(mtf, in ctx, minTfBarsClosedThisPass);
    }

    // Mirrors EvaluateCompoundRulesAndPrint in TradeAlertLoop6Host.cs.
    // Instead of Print, fires are collected into _pendingEvents.
    void EvaluateRules(MtfEngineManager mtf, in FireRunContext ctx, int minTfBarsClosedThisPass)
    {
        for (var slot = 0; slot < _rulesBySlot.Length; slot++)
        {
            var rule = _rulesBySlot[slot];
            if (rule == null)
                continue;

            if (!AreAllRuleEntriesActive(mtf, rule, in ctx))
            {
                if (_fireDebugEnabled && ShouldLogSyncMiss(mtf, rule, in ctx, minTfBarsClosedThisPass))
                    LogFireDebugSyncMiss(mtf, in ctx, slot, rule, minTfBarsClosedThisPass);
                continue;
            }

            TryEmitSlot(mtf, in ctx, slot, rule, ResolvePrimaryEventBarIndex(mtf, rule), minTfBarsClosedThisPass);
        }
    }

    /// <summary>Chart TF is slower than the rule min TF (e.g. M15 chart, M5 min).</summary>
    static bool UsesCrossTfStaging(in FireRunContext ctx, string minTfToken, int minTfMinutes)
    {
        if (string.Equals(ctx.ChartTfToken, minTfToken, StringComparison.Ordinal))
            return false;

        if (!CompoundEvalSync.TryGetTfMinutes(ctx.ChartTfToken, out var chartMinutes))
            return false;

        return chartMinutes > minTfMinutes;
    }

    bool UsesCrossTfStaging(in FireRunContext ctx) =>
        UsesCrossTfStaging(in ctx, _minTfToken, _minTfMinutes);

    /// <summary>
    /// Records a stage row whenever the rule's primary EVENT has fired on the min-TF within
    /// the event window — regardless of whether the other min-TF state legs (e.g.
    /// <c>M5:canBuyReal</c>) happen to be active at this exact min-TF close.
    ///
    /// Rationale: state legs (<c>canBuyReal</c>, <c>canBuyTouchM5</c>) can flicker between
    /// min-TF closes; requiring them at the min-TF sync was too strict and missed cases
    /// where the panel briefly showed FIRE on a later sub-bar. The ledger now persists every
    /// pending event-fire so <see cref="ConfirmStagedLedger"/> can retry the full AND-check
    /// at every chart-TF close in the window.
    /// </summary>
    void StageAtMinTfSync(
        MtfEngineManager mtf,
        in FireRunContext ctx,
        int minTfBarIndex,
        DateTime minTfBarOpenRaw,
        int minTfBarsClosedThisPass)
    {
        if (!UsesCrossTfStaging(in ctx))
            return;

        for (var slot = 0; slot < _rulesBySlot.Length; slot++)
        {
            var rule = _rulesBySlot[slot];
            if (rule == null)
                continue;

            var primaryEntry = ResolvePrimaryEventEntry(rule);
            if (primaryEntry == null)
                continue;

            // Stage only while the primary EVENT is still within its valid window —
            // ResolvePrimaryEventBarIndex returns the last-fired bar forever, but
            // IsConditionActive correctly returns false once the event window expires.
            if (!mtf.IsConditionActive(
                    primaryEntry.Value.TfToken,
                    primaryEntry.Value.ConditionId,
                    ctx.SyncMode,
                    ctx.EventValidBars))
                continue;

            var eventBarIdx = mtf.GetLastFiredBarIndex(
                primaryEntry.Value.TfToken, primaryEntry.Value.ConditionId);
            if (eventBarIdx < 0)
                continue;

            if (_stageLedger.IsEventEmitted(slot, eventBarIdx))
                continue;

            var alreadyPending = _stageLedger.IsEventPending(slot, eventBarIdx);

            _stageLedger.UpsertPending(new CompoundFireStageRecord
            {
                SlotIndex                = slot,
                RuleName                 = rule.RuleName,
                Direction                = rule.Direction,
                ChartTfToken             = ctx.ChartTfToken,
                SourceBarIndex           = eventBarIdx,
                StagedAtChartBarIndex    = ctx.Index,
                StagedAtMinTfBarIndex    = minTfBarIndex,
                StagedAtChartBarOpenTime = ctx.BarOpenTimeLocal,
                StagedAtMinTfBarOpenTime = minTfBarOpenRaw,
            });

            if (_diagLogEnabled && !alreadyPending)
            {
                var minLegsOk = AreMinTfRuleEntriesActive(mtf, rule, in ctx);
                _diagLog!.Invoke(
                    $"[StageDiag] R{slot + 1} {rule.Direction} STAGE eventBar={eventBarIdx} " +
                    $"@chart={ctx.Index} @minTf={minTfBarIndex} minLegsOk={minLegsOk} " +
                    $"chartOpen={ctx.BarOpenTimeLocal:s} minTfOpen={minTfBarOpenRaw:s}");
            }

            if (_fireDebugEnabled && !alreadyPending && minTfBarsClosedThisPass > 0)
            {
                var minLegsOk = AreMinTfRuleEntriesActive(mtf, rule, in ctx);
                var allLegsOk = AreAllRuleEntriesActive(mtf, rule, in ctx);
                LogFireDebug(
                    "STAGE",
                    slot,
                    rule,
                    in ctx,
                    minTfBarsClosedThisPass,
                    $"eventBar={eventBarIdx} @minTf={minTfBarIndex} minLegsOk={minLegsOk} allLegsOk={allLegsOk}",
                    mtf);
            }
        }
    }

    static CompoundConditionEntry? ResolvePrimaryEventEntry(CompoundAlertRule rule)
    {
        foreach (var ce in rule.Entries)
        {
            if (!CompoundRuleParser.IsStateCondition(ce.ConditionId))
                return ce;
        }
        return null;
    }

    void ConfirmStagedLedger(MtfEngineManager mtf, in FireRunContext ctx, int minTfBarsClosedThisPass)
    {
        if (!UsesCrossTfStaging(in ctx))
            return;

        foreach (var record in _stageLedger.GetPendingForConfirm(ctx.Index, ctx.EventValidBars))
        {
            if (record.SlotIndex < 0 || record.SlotIndex >= _rulesBySlot.Length)
                continue;

            var rule = _rulesBySlot[record.SlotIndex];
            if (rule == null)
                continue;

            if (_stageLedger.IsEventEmitted(record.SlotIndex, record.EventBarIndex))
                continue;

            if (!AreAllRuleEntriesActive(mtf, rule, in ctx))
            {
                if (_diagLogEnabled)
                {
                    _diagLog!.Invoke(
                        $"[StageDiag] R{record.SlotIndex + 1} {rule.Direction} CONFIRM-MISS " +
                        $"eventBar={record.EventBarIndex} @chart={ctx.Index} " +
                        $"stagedAt={record.StagedAtChartBarIndex} reason=AND-not-all-OK " +
                        $"detail={DescribeRuleConditions(mtf, rule, in ctx)}");
                }

                if (_fireDebugEnabled && ShouldLogFireDbgDetail(in ctx, minTfBarsClosedThisPass))
                    LogFireDebugConfirmMiss(mtf, in ctx, record, rule, minTfBarsClosedThisPass);

                continue;
            }

            if (_diagLogEnabled)
            {
                _diagLog!.Invoke(
                    $"[StageDiag] R{record.SlotIndex + 1} {rule.Direction} CONFIRM-OK " +
                    $"eventBar={record.EventBarIndex} @chart={ctx.Index} " +
                    $"stagedAt={record.StagedAtChartBarIndex}");
            }

            TryEmitSlot(mtf, in ctx, record.SlotIndex, rule, record.EventBarIndex, minTfBarsClosedThisPass);
        }
    }

    void LogPendingExpiringThisBar(in FireRunContext ctx)
    {
        if (!_diagLogEnabled)
            return;

        foreach (var rec in _stageLedger.Snapshot())
        {
            if (rec.Status != CompoundFireStageStatus.Pending)
                continue;

            if (CompoundFireStageLedger.IsWithinConfirmWindow(
                    rec.StagedAtChartBarIndex, ctx.Index, ctx.EventValidBars))
                continue;

            _diagLog!.Invoke(
                $"[StageDiag] R{rec.SlotIndex + 1} {rec.Direction} EXPIRE " +
                $"eventBar={rec.EventBarIndex} stagedAt={rec.StagedAtChartBarIndex} " +
                $"now={ctx.Index} window={ctx.EventValidBars}");
        }
    }

    static string DescribeRuleConditions(
        MtfEngineManager mtf,
        CompoundAlertRule rule,
        in FireRunContext ctx)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append('[');
        for (var i = 0; i < rule.Entries.Count; i++)
        {
            var e = rule.Entries[i];
            var ok = mtf.IsConditionActive(e.TfToken, e.ConditionId, ctx.SyncMode, ctx.EventValidBars);
            if (i > 0) sb.Append(',');
            sb.Append(e.TfToken).Append(':').Append(e.ConditionId).Append('=').Append(ok ? "OK" : "--");
        }
        sb.Append(']');
        return sb.ToString();
    }

    static bool AreAllRuleEntriesActive(
        MtfEngineManager mtf,
        CompoundAlertRule rule,
        in FireRunContext ctx)
    {
        foreach (var entry in rule.Entries)
        {
            if (!mtf.IsConditionActive(
                    entry.TfToken, entry.ConditionId, ctx.SyncMode, ctx.EventValidBars))
                return false;
        }

        return true;
    }

    static bool AreMinTfRuleEntriesActive(
        MtfEngineManager mtf,
        CompoundAlertRule rule,
        in FireRunContext ctx,
        string minTfToken)
    {
        var hasMinTfEntry = false;
        foreach (var entry in rule.Entries)
        {
            if (!string.Equals(entry.TfToken, minTfToken, StringComparison.Ordinal))
                continue;

            hasMinTfEntry = true;
            if (!mtf.IsConditionActive(
                    entry.TfToken, entry.ConditionId, ctx.SyncMode, ctx.EventValidBars))
                return false;
        }

        return hasMinTfEntry;
    }

    bool AreMinTfRuleEntriesActive(MtfEngineManager mtf, CompoundAlertRule rule, in FireRunContext ctx) =>
        AreMinTfRuleEntriesActive(mtf, rule, in ctx, _minTfToken);

    static int ResolvePrimaryEventBarIndex(MtfEngineManager mtf, CompoundAlertRule rule)
    {
        foreach (var ce in rule.Entries)
        {
            if (!CompoundRuleParser.IsStateCondition(ce.ConditionId))
                return mtf.GetLastFiredBarIndex(ce.TfToken, ce.ConditionId);
        }

        return -1;
    }

    bool TryEmitSlot(
        MtfEngineManager mtf,
        in FireRunContext ctx,
        int slot,
        CompoundAlertRule rule,
        int eventBarIdx,
        int minTfBarsClosedThisPass)
    {
        var chartBarIdx  = ctx.Index;
        var barOpenLocal = ctx.BarOpenTimeLocal;
        var close        = ctx.Bars.ClosePrices[chartBarIdx];

        if (_lastFiredChartBarIdx[slot] == chartBarIdx)
        {
            if (_diagLogEnabled)
                _diagLog!.Invoke(
                    $"[StageDiag] R{slot + 1} {rule.Direction} EMIT-SKIP " +
                    $"reason=same-chartBar lastFired={_lastFiredChartBarIdx[slot]} chart={chartBarIdx}");
            return false;
        }

        if (eventBarIdx >= 0 && _lastEventBarIdx[slot] == eventBarIdx)
        {
            if (_diagLogEnabled)
                _diagLog!.Invoke(
                    $"[StageDiag] R{slot + 1} {rule.Direction} EMIT-SKIP " +
                    $"reason=same-eventBar lastEvent={_lastEventBarIdx[slot]} event={eventBarIdx}");
            return false;
        }

        if (eventBarIdx >= 0 && _stageLedger.IsEventEmitted(slot, eventBarIdx))
        {
            if (_diagLogEnabled)
                _diagLog!.Invoke(
                    $"[StageDiag] R{slot + 1} {rule.Direction} EMIT-SKIP " +
                    $"reason=ledger-already-emitted event={eventBarIdx}");
            return false;
        }

        _lastFiredChartBarIdx[slot] = chartBarIdx;
        _lastEventBarIdx[slot]      = eventBarIdx;

        if (eventBarIdx >= 0)
            _stageLedger.MarkEmitted(slot, eventBarIdx, chartBarIdx, barOpenLocal);
        else
            _stageLedger.RemovePending(slot, eventBarIdx);

        _pendingEvents.Add(new CompoundFireEvent(
            SlotIndex:     slot,
            RuleName:      rule.RuleName,
            Direction:     rule.Direction,
            ChartTfToken:  ctx.ChartTfToken,
            ChartBarIndex: chartBarIdx,
            BarOpenTime:   barOpenLocal,
            Price:         close,
            EventBarIndex: eventBarIdx));

        if (_diagLogEnabled)
            _diagLog!.Invoke(
                $"[StageDiag] R{slot + 1} {rule.Direction} EMIT-OK " +
                $"chart={chartBarIdx} event={eventBarIdx} close={close}");

            if (_fireDebugEnabled)
            {
                LogFireDebug(
                    "EMIT-OK",
                    slot,
                    rule,
                    in ctx,
                    minTfBarsClosedThisPass,
                    FormattableString.Invariant($"event={eventBarIdx} close={close:F5}"),
                    mtf,
                    includeAllLegs: true);

                LogHtfTouchProbeTable(mtf, in ctx, chartBarIdx, close);
            }

        return true;
    }

    static bool ShouldLogFireDbgDetail(in FireRunContext ctx, int minTfBarsClosedThisPass) =>
        !ctx.SimulatedChartCloseTimeLocal.HasValue || minTfBarsClosedThisPass > 0;

    bool ShouldLogSyncMiss(
        MtfEngineManager mtf,
        CompoundAlertRule rule,
        in FireRunContext ctx,
        int minTfBarsClosedThisPass)
    {
        if (!ShouldLogFireDbgDetail(in ctx, minTfBarsClosedThisPass))
            return false;

        var primary = ResolvePrimaryEventEntry(rule);
        if (primary != null && mtf.IsConditionActive(
                primary.Value.TfToken, primary.Value.ConditionId, ctx.SyncMode, ctx.EventValidBars))
            return true;

        return _stageLedger.PendingCount > 0;
    }

    void LogFireDebugSyncMiss(
        MtfEngineManager mtf,
        in FireRunContext ctx,
        int slot,
        CompoundAlertRule rule,
        int minTfBarsClosedThisPass)
    {
        var inactive = BuildInactiveLegSignature(mtf, rule, in ctx);
        LogFireDebug(
            "SYNC-MISS",
            slot,
            rule,
            in ctx,
            minTfBarsClosedThisPass,
            $"blockers=[{inactive}]",
            mtf,
            includeAllLegs: true);

        LogHtfTouchProbeTable(mtf, in ctx, ctx.Index, ctx.Bars.ClosePrices[ctx.Index]);
    }

    void LogFireDebugConfirmMiss(
        MtfEngineManager mtf,
        in FireRunContext ctx,
        CompoundFireStageRecord record,
        CompoundAlertRule rule,
        int minTfBarsClosedThisPass)
    {
        var inactive = BuildInactiveLegSignature(mtf, rule, in ctx);
        var key = (record.SlotIndex, record.EventBarIndex, ctx.Index, inactive);
        if (_fireDbgConfirmDedup.ContainsKey(key))
            return;
        _fireDbgConfirmDedup[key] = true;

        LogFireDebug(
            "CONFIRM-MISS",
            record.SlotIndex,
            rule,
            in ctx,
            minTfBarsClosedThisPass,
            $"eventBar={record.EventBarIndex} stagedAt={record.StagedAtChartBarIndex} blockers=[{inactive}]",
            mtf,
            includeAllLegs: true);

        LogHtfTouchProbeTable(mtf, in ctx, ctx.Index, ctx.Bars.ClosePrices[ctx.Index]);
    }

    static string BuildInactiveLegSignature(
        MtfEngineManager mtf,
        CompoundAlertRule rule,
        in FireRunContext ctx)
    {
        var parts = new List<string>();
        foreach (var entry in rule.Entries)
        {
            if (mtf.IsConditionActive(entry.TfToken, entry.ConditionId, ctx.SyncMode, ctx.EventValidBars))
                continue;
            parts.Add($"{entry.TfToken}:{entry.ConditionId}");
        }
        return string.Join(",", parts);
    }

    void LogFireDebug(
        string tag,
        int slot,
        CompoundAlertRule rule,
        in FireRunContext ctx,
        int minTfBarsClosedThisPass,
        string extra,
        MtfEngineManager mtf,
        bool includeAllLegs = false)
    {
        if (!_fireDebugEnabled || _fireDebugLog == null)
            return;

        var timing = BuildFireDebugTimingCtx(in ctx, minTfBarsClosedThisPass);
        var sb = new System.Text.StringBuilder();
        sb.Append($"[FireDbg] R{slot + 1} {rule.Direction} {tag} @chart={ctx.Index} ");
        sb.Append(timing).Append(' ').Append(extra);

        if (includeAllLegs)
        {
            sb.Append(" | legs: ");
            sb.Append(BuildFireDebugLegLines(mtf, rule, in ctx));
        }

        _fireDebugLog.Invoke(sb.ToString());
    }

    static string BuildFireDebugTimingCtx(in FireRunContext ctx, int minTfBarsClosedThisPass)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("timing[");
        sb.Append("sync=").Append(ctx.SyncMode);
        sb.Append(" win=").Append(ctx.EventValidBars);
        sb.Append(" m5ClosedPass=").Append(minTfBarsClosedThisPass);
        if (ctx.SimulatedChartCloseTimeLocal.HasValue)
        {
            sb.Append(" replay=Y simClose=").Append(ctx.SimulatedChartCloseTimeLocal.Value.ToString("s"));
            if (ctx.ReplayChartHigh.HasValue && ctx.ReplayChartLow.HasValue)
                sb.Append(FormattableString.Invariant(
                    $" replayHL=[{ctx.ReplayChartLow.Value:F5},{ctx.ReplayChartHigh.Value:F5}]"));
        }
        else
        {
            sb.Append(" replay=N");
        }
        sb.Append(" chartOpen=").Append(ctx.BarOpenTimeLocal.ToString("s"));
        sb.Append(']');
        return sb.ToString();
    }

    string BuildFireDebugLegLines(MtfEngineManager mtf, CompoundAlertRule rule, in FireRunContext ctx)
    {
        var isBarClosed = !ctx.LiveFormingLastBar;
        var isRealtime = ctx.LiveFormingLastBar;
        var chartM15Edge = AlertBarTiming.ComputeM15CloseEdge(
            ctx.ChartTfToken,
            isBarClosed,
            isRealtime,
            ctx.IsNewBarOnRealtimeForming,
            ctx.BarOpenTimeLocal.Minute,
            ctx.InjectM15CloseEdge);

        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < rule.Entries.Count; i++)
        {
            var e = rule.Entries[i];
            if (i > 0) sb.Append(" | ");
            var isChartLeg = string.Equals(e.TfToken, ctx.ChartTfToken, StringComparison.Ordinal);
            sb.Append(mtf.DescribeLegFireDebug(
                e.TfToken,
                e.ConditionId,
                ctx.SyncMode,
                ctx.EventValidBars,
                ctx.Index,
                isBarClosed,
                isRealtime,
                ctx.IsNewBarOnRealtimeForming,
                chartM15Edge,
                isChartLeg ? ctx.ReplayChartHigh : null,
                isChartLeg ? ctx.ReplayChartLow : null));
        }
        return sb.ToString();
    }

    void LogHtfTouchProbeTable(
        MtfEngineManager mtf,
        in FireRunContext ctx,
        int chartBarIdx,
        double chartClose)
    {
        if (_fireDebugLog == null)
            return;

        var isBarClosed = !ctx.LiveFormingLastBar;
        var isRealtime = ctx.LiveFormingLastBar;
        var chartM15Edge = AlertBarTiming.ComputeM15CloseEdge(
            ctx.ChartTfToken,
            isBarClosed,
            isRealtime,
            ctx.IsNewBarOnRealtimeForming,
            ctx.BarOpenTimeLocal.Minute,
            ctx.InjectM15CloseEdge);

        var barCloseTime = ctx.SimulatedChartCloseTimeLocal
            ?? ctx.BarOpenTimeLocal.Add(ctx.BarPeriod);

        var table = mtf.DescribeHtfTouchProbeTableFireDebug(
            ctx.ChartTfToken,
            chartBarIdx,
            isBarClosed,
            isRealtime,
            ctx.IsNewBarOnRealtimeForming,
            chartM15Edge,
            barCloseTime,
            ctx.ReplayChartHigh,
            ctx.ReplayChartLow,
            chartClose);

        if (table == null)
            return;

        var header = new System.Text.StringBuilder();
        header.Append("[FireDbg] HTF-TOUCH-TABLE @chart=").Append(chartBarIdx);
        if (ctx.SimulatedChartCloseTimeLocal.HasValue)
            header.Append(" simClose=").Append(ctx.SimulatedChartCloseTimeLocal.Value.ToString("s"));
        if (ctx.ReplayChartHigh.HasValue && ctx.ReplayChartLow.HasValue)
        {
            header.Append(FormattableString.Invariant(
                $" replayHL=[{ctx.ReplayChartLow.Value:F5},{ctx.ReplayChartHigh.Value:F5}]"));
        }
        _fireDebugLog.Invoke(header.ToString());
        _fireDebugLog.Invoke(table);
    }
}
