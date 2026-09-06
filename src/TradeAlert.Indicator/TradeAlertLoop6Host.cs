using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using cAlgo.API;
using TradeAlert.Core.Drawing;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Engines.AlertEvaluators;
using TradeAlert.Core.Models;
using TradeAlert.Core.Models.Alerts;
using TradeAlert.Core.Models.KeyLevel;
using TradeAlert.Indicator;
using ChartTimePolicy = TradeAlert.Core.Models.ChartTimePolicy;

namespace TradeAlert.Indicator.Host;

/// <summary>
/// Wrapper cTrader thật — kế thừa kiểu nền tảng đầy đủ để tránh nhầm với không gian tên <see cref="TradeAlert.Indicator"/>.
/// <see cref="AccessRights.None"/> — chỉ <see cref="Print"/> / chart UI (compound panel draggable) cho cảnh báo.
/// Không đặt lệnh, không email/Telegram/mạng/file.
/// </summary>
[Indicator(IsOverlay = true, TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
public class TradeAlertLoop6Host : cAlgo.API.Indicator
{
    TradeAlertIndicator? _shell;
    readonly HostLtfCollector _ltfCollector = new();
    bool _engineWarmupCompleted;
    int _warmupEndBar = -1;
    int _realtimeLastBarHits;
    readonly Loop6LastBarOpenTracker _openTracker = new();
    bool _backtestLastBarOpenInitialized;
    Loop6ThrottleState _diagThrottle;
    readonly StringBuilder _diagText = new(256);
    readonly List<(string Text, Color Color)> _compoundPanelLines = new(64);
    readonly List<TextBlock> _compoundLineBlocks = new(64);
    ChartDraggable? _compoundDraggable;
    StackPanel? _compoundPanel;
    bool _compoundDraggablePositioned;

    // ===== Diagnostic panel (M5 shell health) =====
    readonly List<(string Text, Color Color)> _diagPanelLines = new(40);
    readonly List<TextBlock> _diagLineBlocks = new(40);
    ChartDraggable? _diagDraggable;
    StackPanel? _diagPanel;
    bool _diagDraggablePositioned;
    readonly RealZoneDebugPanelRenderer _realZoneDebugPanel = new();
    readonly List<(string Text, Color Color)> _realZonePanelLines = new(32);
    readonly RealZoneDebugPanelRenderer _htfTouchTablePanel = new(initialY: 520);
    readonly List<(string Text, Color Color)> _htfTouchPanelLines = new(64);

    static readonly string[] HtfTouchTableTfTokens = { "5", "15", "60", "240" };
    static readonly Dictionary<string, AlertConditionId[]> HtfTouchTableTrackedConds = BuildHtfTouchTableTrackedConds();
    // counters
    int _diagCalculateCalls;
    int _diagCompoundCalls;
    int _diagAdvanceCount;  // số M15 bar mới sau initial load
    int _diagInitialLastBarIdx = -1;

    const string CompoundPanelObjectPrefix = "Loop6CompoundPanel_L";
    const double CompoundPanelFontSize = 11;
    const double CompoundPanelInitialMargin = 20;
    const string CompoundPanelRuleSeparator = "────────────────";
    static readonly Color CompoundPanelTitleColor = Color.White;
    static readonly Color CompoundPanelSeparatorColor = Color.FromArgb(255, 100, 100, 100);
    static readonly Color CompoundPanelHeaderWaitingColor = Color.Yellow;
    static readonly Color CompoundPanelHeaderFireColor = Color.Red;
    static readonly Color CompoundPanelDetailOkColor = Color.Lime;
    static readonly Color CompoundPanelDetailColor = Color.Gray;
    static readonly Color CompoundPanelOffColor = Color.DimGray;

    AlertTrackingChartRenderer? _alertTrackingRenderer;
    string _chartTfToken = "5";
    MtfEngineManager? _mtfManager;
    // Compound state owned by orchestrator (single source of truth for FIRE logic).
    readonly CompoundFireOrchestrator _compoundFire = new();

    // ── Pine GROUP 1: Core Settings ─────────────────────────────────────────
    [Parameter("Hiển thị bảng Compound", DefaultValue = true, Group = "Core Settings")]
    public bool ShowCompoundPanel { get; set; }

    [Parameter("Lookback bars for pivots", DefaultValue = 200, MinValue = 1, Group = "Core Settings")]
    public int LookbackBars { get; set; }

    // ── Pine GROUP 2: Visual Display (thứ tự giống TradingView) ─────────────
    [Parameter("Enable Keylevel Box (Raw OHLC)", DefaultValue = true, Group = "Visual Display")]
    public bool EnableKeylevel { get; set; }

    [Parameter("Hiển thị OB Box", DefaultValue = true, Group = "Visual Display")]
    public bool ShowObBox { get; set; }

    [Parameter("Ẩn OB mất zin", DefaultValue = true, Group = "Visual Display")]
    public bool HideObLoseZin { get; set; }

    [Parameter("Ẩn OB còn zin", DefaultValue = false, Group = "Visual Display")]
    public bool HideObZin { get; set; }

    [Parameter("Show FAKE swings", DefaultValue = false, Group = "Visual Display")]
    public bool ShowFakeSwings { get; set; }

    [Parameter("Show LOCKED swings", DefaultValue = false, Group = "Visual Display")]
    public bool ShowLockedSwings { get; set; }

    [Parameter("Show ACTIVE swings", DefaultValue = false, Group = "Visual Display")]
    public bool ShowActiveSwings { get; set; }

    [Parameter("Show swing push rule (debug)", DefaultValue = false, Group = "Visual Display")]
    public bool ShowSwingPushDebug { get; set; }

    [Parameter("Swing đỉnh miss labels (10 bars)", DefaultValue = false, Group = "Visual Display")]
    public bool ShowSwingPeakMissLabels { get; set; }

    [Parameter("Wick noise + LTF debug labels", DefaultValue = false, Group = "Visual Display")]
    public bool ShowWickNoiseDebugLabels { get; set; }

    [Parameter("OB missing-box debug labels", DefaultValue = false, Group = "Visual Display")]
    public bool ShowObBoxMissingDebug { get; set; }

    [Parameter("Lock Swing Count", DefaultValue = 50, MinValue = 1, Group = "Visual Display")]
    public int LockSwingCount { get; set; }

    [Parameter("OB Box opacity", DefaultValue = 30, MinValue = 0, MaxValue = 100, Group = "Visual Display")]
    public int ObBoxOpacity { get; set; }

    // ── Pine GROUP 3: Filters ─────────────────────────────────────────────────
    [Parameter("✅ OB Validation (Auto LTF/ATR)", DefaultValue = true, Group = "Filters")]
    public bool UseObConfirm { get; set; }

    [Parameter("OB ATR Multiplier", DefaultValue = 1.5, MinValue = 0.5, MaxValue = 5.0, Step = 0.1, Group = "Filters")]
    public double ObAtrMultiplier { get; set; }

    [Parameter("Filter noise râu (wick > avg range)", DefaultValue = true, Group = "Filters")]
    public bool UseWickNoiseFilter { get; set; }

    [Parameter("Avg range length", DefaultValue = 10, MinValue = 1, Group = "Filters")]
    public int WickAvgLen { get; set; }

    [Parameter("Confirm wick noise bằng LTF", DefaultValue = true, Group = "Filters")]
    public bool UseLtfWickConfirm { get; set; }

    [Parameter("Min penetration ratio", DefaultValue = 0.5, MinValue = 0.05, MaxValue = 1, Step = 0.05, Group = "Filters")]
    public double LtfConfirmRatio { get; set; }

    [Parameter("Use ATR rule cho keylevel", DefaultValue = true, Group = "Filters")]
    public bool KeylevelUseAtrRule { get; set; }

    [Parameter("Pullback filter (new engine A1/A2/A3)", DefaultValue = true, Group = "Filters")]
    public bool UseNewPullbackFilter { get; set; }

    [Parameter("Bx: Max bars A→B", DefaultValue = 6, MinValue = 2, MaxValue = 15, Group = "Filters")]
    public int BMaxLagBars { get; set; }

    [Parameter("CDx: Max bars A→C/D", DefaultValue = 7, MinValue = 3, MaxValue = 20, Group = "Filters")]
    public int CdMaxLagBars { get; set; }

    [Parameter("Strong body ATR mult", DefaultValue = 0.6, MinValue = 0.1, MaxValue = 2, Step = 0.1, Group = "Filters")]
    public double StrongAtrMult { get; set; }

    [Parameter("Medium body ATR mult", DefaultValue = 0.3, MinValue = 0.1, MaxValue = 2, Step = 0.1, Group = "Filters")]
    public double MediumAtrMult { get; set; }

    [Parameter("Multi-A: pick price extreme (common rule)", DefaultValue = true, Group = "Filters")]
    public bool UseExtremePickRule { get; set; }

    [Parameter("🔀 Merge gap candles (X+Y → XY)", DefaultValue = true, Group = "Filters")]
    public bool UseGapMerge { get; set; }

    [Parameter("Gap threshold (ATR mult)", DefaultValue = 0.5, MinValue = 0.1, Step = 0.1, Group = "Filters")]
    public double GapFilterAtr { get; set; }

    [Parameter("đỉnh đáy vi mô", DefaultValue = true, Group = "Filters")]
    public bool UseMicroSwingRule { get; set; }

    [Parameter("A4: Doji Force B + Strong C", DefaultValue = true, Group = "Filters")]
    public bool UseDojiForceBC { get; set; }

    [Parameter("A4: Doji body max ratio", DefaultValue = 0.25, MinValue = 0.05, MaxValue = 0.5, Step = 0.05, Group = "Filters")]
    public double DojiBodyMaxRatio { get; set; }

    [Parameter("A4: Doji force wick min", DefaultValue = 0.55, MinValue = 0.3, MaxValue = 0.9, Step = 0.05, Group = "Filters")]
    public double DojiForceWickRatioMin { get; set; }

    [Parameter("A4: Doji body zone max", DefaultValue = 0.45, MinValue = 0.2, MaxValue = 0.8, Step = 0.05, Group = "Filters")]
    public double DojiForceBodyZoneMax { get; set; }

    [Parameter("A4: Wick dominance ratio", DefaultValue = 1.5, MinValue = 1, MaxValue = 5, Step = 0.25, Group = "Filters")]
    public double DojiWickDomRatio { get; set; }

    // ── Pine: Break Rules ───────────────────────────────────────────────────
    [Parameter("R3: max k (E trong bar B+1..B+k)", DefaultValue = 3, MinValue = 1, MaxValue = 6, Group = "Break Rules")]
    public int BreakR3MaxK { get; set; }

    // ── Pine: OB Settings ───────────────────────────────────────────────────
    [Parameter("LTF Buffer Size", DefaultValue = 50, MinValue = 10, MaxValue = 100, Group = "OB Settings")]
    public int LtfBufferSize { get; set; }

    [Parameter("OB scan bars", DefaultValue = 50, MinValue = 5, MaxValue = 100, Group = "OB Settings")]
    public int ObScanBars { get; set; }

    [Parameter("Max OBs (all types)", DefaultValue = 20, MinValue = 10, MaxValue = 100, Group = "OB Settings")]
    public int MaxObs { get; set; }

    [Parameter("Min OB Body Ratio", DefaultValue = 0.35, MinValue = 0, MaxValue = 1, Step = 0.05, Group = "OB Settings")]
    public double MinObBodyRatio { get; set; }

    [Parameter("Max Doji Body Ratio", DefaultValue = 0.15, MinValue = 0, MaxValue = 0.3, Step = 0.05, Group = "OB Settings")]
    public double MaxDojiBodyRatio { get; set; }

    // ── Pine: Keylevel Settings ─────────────────────────────────────────────
    [Parameter("Reference candle lookback", DefaultValue = 2, MinValue = 1, Group = "Keylevel Settings")]
    public int KeylevelLookback { get; set; }

    [Parameter("Average body length", DefaultValue = 10, MinValue = 1, Group = "Keylevel Settings")]
    public int KeylevelAvgBodyLen { get; set; }

    [Parameter("Min body multiplier (Algo)", DefaultValue = 1.5, MinValue = 0.1, MaxValue = 10, Step = 0.1, Group = "Keylevel Settings")]
    public double KeylevelMinBodyMult { get; set; }

    [Parameter("ATR multiplier (Algo)", DefaultValue = 0.2, MinValue = 0.1, MaxValue = 2, Step = 0.1, Group = "Keylevel Settings")]
    public double KeylevelAtrMult { get; set; }

    [Parameter("ATR length", DefaultValue = 14, MinValue = 1, Group = "Keylevel Settings")]
    public int KeylevelAtrLen { get; set; }

    [Parameter("Max overlapping keylevels kept", DefaultValue = 2, MinValue = 1, Group = "Keylevel Settings")]
    public int MaxKeylevelKeep { get; set; }

    // ── Pine: Style Options ─────────────────────────────────────────────────
    [Parameter("Show key border", DefaultValue = true, Group = "Style Options")]
    public bool ShowKeyBorder { get; set; }

    [Parameter("Key border width", DefaultValue = 1, MinValue = 0, MaxValue = 5, Group = "Style Options")]
    public int KeyBorderWidth { get; set; }

    [Parameter("Invert key color when BROKEN (High↔Low)", DefaultValue = true, Group = "Style Options")]
    public bool InvertBrokenKeyColor { get; set; }

    [Parameter("Key opacity ACTIVE", DefaultValue = 50, MinValue = 0, MaxValue = 100, Group = "Style Options")]
    public int KeyOpacityActive { get; set; }

    [Parameter("Key opacity BROKEN", DefaultValue = 30, MinValue = 0, MaxValue = 100, Group = "Style Options")]
    public int KeyOpacityBroken { get; set; }

    [Parameter("Label opacity", DefaultValue = 50, MinValue = 0, MaxValue = 100, Group = "Style Options")]
    public int LabelOpacity { get; set; }

    [Parameter("High label color", DefaultValue = "Green", Group = "Style Options")]
    public Color HighLabelColor { get; set; }

    [Parameter("Low label color", DefaultValue = "OrangeRed", Group = "Style Options")]
    public Color LowLabelColor { get; set; }

    [Parameter("HH label color", DefaultValue = "Green", Group = "Style Options")]
    public Color HhLabelColor { get; set; }

    [Parameter("LL label color", DefaultValue = "Red", Group = "Style Options")]
    public Color LlLabelColor { get; set; }

    [Parameter("MAIN label color", DefaultValue = "Yellow", Group = "Style Options")]
    public Color MainLabelColor { get; set; }

    [Parameter("FAKE label color", DefaultValue = "Blue", Group = "Style Options")]
    public Color FakeLabelColor { get; set; }

    [Parameter("BROKEN label color", DefaultValue = "Gray", Group = "Style Options")]
    public Color BrokenLabelColor { get; set; }

    [Parameter("High key box color", DefaultValue = "IndianRed", Group = "Style Options")]
    public Color HighKeyBoxColor { get; set; }

    [Parameter("Low key box color", DefaultValue = "LimeGreen", Group = "Style Options")]
    public Color LowKeyBoxColor { get; set; }

    [Parameter("MAIN C Keylevel (vàng sáng)", DefaultValue = "Gold", Group = "Style Options")]
    public Color MainCKeyBoxColor { get; set; }

    // ── cTrader-only (không có trên Pine TV) ────────────────────────────────
    [Parameter("Enable Alerts", DefaultValue = true, Group = "cTrader Host")]
    public bool EnableAlerts { get; set; }

    [Parameter("Print single alert conditions (Touch/Real spam)", DefaultValue = false, Group = "cTrader Host")]
    public bool PrintSingleAlerts { get; set; }

    [Parameter("Debug Mode", DefaultValue = false, Group = "cTrader Host")]
    public bool DebugMode { get; set; }

    [Parameter("Alert Tracking Debug (chart labels)", DefaultValue = false, Group = "Debug Advanced")]
    public bool AlertTrackingDebug { get; set; }

    [Parameter("Real Zone Debug (panel + Print band/zone)", DefaultValue = false, Group = "Debug Advanced")]
    public bool RealZoneDebug { get; set; }

    [Parameter("HTF Touch Table (H1/H4 zones + M5 probe panel)", DefaultValue = false, Group = "Debug Advanced")]
    public bool HtfTouchTableDebug { get; set; }

    [Parameter("HTF Touch parity debug (sync + pivot audit)", DefaultValue = true, Group = "Debug Advanced")]
    public bool HtfTouchParityDebug { get; set; }

    [Parameter("Diagnostic Panel (M5 shell health)", DefaultValue = false, Group = "Debug Advanced")]
    public bool ShowDiagPanel { get; set; }

    [Parameter("Message Prefix", DefaultValue = "[Loop6]", Group = "cTrader Host")]
    public string MessagePrefix { get; set; } = "[Loop6]";

    [Parameter("Max debug log rows", DefaultValue = 1000, MinValue = 1, MaxValue = 50000, Group = "cTrader Host")]
    public int MaxDebugLogEntries { get; set; }

    [Parameter("Diagnostic throttle (sec)", DefaultValue = 60, MinValue = 1, MaxValue = 86400, Group = "cTrader Host")]
    public int DiagnosticThrottleSeconds { get; set; }

    [Parameter("Show Static Text (diag)", DefaultValue = false, Group = "cTrader Host")]
    public bool ShowStaticDiagnostics { get; set; }

    [Parameter("Inject M15 Close Edge host", DefaultValue = false, Group = "cTrader Host")]
    public bool InjectM15CloseEdgeSignal { get; set; }

    [Parameter("Extend right (bars ahead)", DefaultValue = 500, MinValue = 50, MaxValue = 5000, Group = "cTrader Host")]
    public int ExtendRightBarsAhead { get; set; }

    // ── Compound Alerts (MTF AND rules) — parity with Loop6BacktestTradingBot Group 03 ──
    [Parameter("Compound Rule 1 (R1 SELL M5)", DefaultValue = CompoundAlertPresets.R1SellM5, Group = "Compound Alerts")]
    public string CompoundRule1 { get; set; } = CompoundAlertPresets.R1SellM5;

    [Parameter("Enable Compound Rule 1 (R1 SELL M5)", DefaultValue = true, Group = "Compound Alerts")]
    public bool EnableCompoundRule1 { get; set; } = true;

    [Parameter("Compound Rule 2 (R2 BUY M5)", DefaultValue = CompoundAlertPresets.R2BuyM5, Group = "Compound Alerts")]
    public string CompoundRule2 { get; set; } = CompoundAlertPresets.R2BuyM5;

    [Parameter("Enable Compound Rule 2 (R2 BUY M5)", DefaultValue = true, Group = "Compound Alerts")]
    public bool EnableCompoundRule2 { get; set; } = true;

    [Parameter("Compound Rule 3 (R3 BUY HL M15)", DefaultValue = CompoundAlertPresets.R3BuyHlM15, Group = "Compound Alerts")]
    public string CompoundRule3 { get; set; } = CompoundAlertPresets.R3BuyHlM15;

    [Parameter("Enable Compound Rule 3 (R3 BUY HL M15)", DefaultValue = true, Group = "Compound Alerts")]
    public bool EnableCompoundRule3 { get; set; } = true;

    [Parameter("Compound Rule 4 (R4 SELL HL M15)", DefaultValue = CompoundAlertPresets.R4SellHlM15, Group = "Compound Alerts")]
    public string CompoundRule4 { get; set; } = CompoundAlertPresets.R4SellHlM15;

    [Parameter("Enable Compound Rule 4 (R4 SELL HL M15)", DefaultValue = true, Group = "Compound Alerts")]
    public bool EnableCompoundRule4 { get; set; } = true;

    [Parameter("Compound Rule 5 (R5 BUY NG M15)", DefaultValue = CompoundAlertPresets.R5BuyNgM15, Group = "Compound Alerts")]
    public string CompoundRule5 { get; set; } = CompoundAlertPresets.R5BuyNgM15;

    [Parameter("Enable Compound Rule 5 (R5 BUY NG M15)", DefaultValue = true, Group = "Compound Alerts")]
    public bool EnableCompoundRule5 { get; set; } = true;

    [Parameter("Compound Rule 6 (R6 SELL NG M15)", DefaultValue = CompoundAlertPresets.R6SellNgM15, Group = "Compound Alerts")]
    public string CompoundRule6 { get; set; } = CompoundAlertPresets.R6SellNgM15;

    [Parameter("Enable Compound Rule 6 (R6 SELL NG M15)", DefaultValue = true, Group = "Compound Alerts")]
    public bool EnableCompoundRule6 { get; set; } = true;

    [Parameter("EVENT valid window (bars of that TF)", DefaultValue = 3, MinValue = 1, MaxValue = 50, Group = "Compound Alerts")]
    public int CompoundEventValidBars { get; set; }

    [Parameter("Compound sync (TV = same moment)", DefaultValue = CompoundEvalSyncMode.PineEventWindow, Group = "Compound Alerts")]
    public CompoundEvalSyncMode CompoundSyncMode { get; set; }

    [Parameter("Compound window mode", DefaultValue = CompoundWindowMode.SourceOneShot, Group = "Compound Alerts")]
    public CompoundWindowMode CompoundWindowMode { get; set; }

    [Parameter("Print compound fires to log (debug)", DefaultValue = true, Group = "Compound Alerts")]
    public bool PrintCompoundFires { get; set; }


    protected override void Initialize()
    {
        _engineWarmupCompleted = false;
        _warmupEndBar = -1;
        _realtimeLastBarHits = 0;
        _backtestLastBarOpenInitialized = false;
        _openTracker.Reset();

        var hostCtx = new IndicatorHostContext
        {
            Symbol = Symbol.Name,
            ChartTimeframeToken = Loop6ChartTimeframeToken.FromBarsTimeFrame(Bars.TimeFrame),
            HostChartIsRealtime = RunningMode == RunningMode.RealTime,
        };

        IAlertFireSink sink = EnableAlerts && PrintSingleAlerts
            ? new PrintOnlyAlertFireSink(
                s => Print(s),
                evt => $"{MessagePrefix} {evt.Symbol} {evt.ConditionId} bar={evt.SourceBarIndex}")
            : VoidAlertFireSink.Instance;

        _shell = new TradeAlertIndicator(hostCtx, sink);
        _shell.Alerts.MaxDebugLogEntries = MaxDebugLogEntries;

        _chartTfToken = hostCtx.ChartTimeframeToken;
        _alertTrackingRenderer = new AlertTrackingChartRenderer(Chart, Bars, _chartTfToken);

        _ltfCollector.Configure(Bars.TimeFrame, hostCtx.ChartTimeframeToken);
        _ltfCollector.TryRebind(MarketData);
        SyncObLtfMappingResolved();
        SyncLtfSnapshotFetcher();
        ApplyPineParameters();
        InitializeCompoundAlerts(hostCtx.ChartTimeframeToken);
        SyncAlertTracking();
        SyncHtfTouchDebugSettings();
    }

    static Dictionary<string, AlertConditionId[]> BuildHtfTouchTableTrackedConds() =>
        new(StringComparer.Ordinal)
        {
            ["5"] = new[]
            {
                AlertConditionId.CondBuyEventM5,
                AlertConditionId.CondSellEventM5,
            },
            ["15"] = new[]
            {
                AlertConditionId.CondBuyEventNGM15,
                AlertConditionId.CondSellEventNGM15,
                AlertConditionId.CondBuyEventHLNGM15,
                AlertConditionId.CondSellEventHLNGM15,
            },
            ["60"] = new[]
            {
                AlertConditionId.CanBuyTouchM5,
                AlertConditionId.CanSellTouchM5,
            },
            ["240"] = new[]
            {
                AlertConditionId.CanBuyTouchM5,
                AlertConditionId.CanSellTouchM5,
            },
        };

    void SyncHtfTouchDebugSettings()
    {
        _compoundFire.SetFireDebugLogger(HtfTouchTableDebug ? Print : null, HtfTouchTableDebug);
        _mtfManager?.ConfigureHtfTouchParityDebug(HtfTouchTableDebug && HtfTouchParityDebug);
        if (HtfTouchTableDebug)
            EnsureHtfTouchDebugEngines();
    }

    void EnsureHtfTouchDebugEngines()
    {
        if (!HtfTouchTableDebug || _shell == null)
            return;

        _mtfManager ??= new MtfEngineManager(Symbol.Name, Symbol.TickSize);
        if (!_mtfManager.HasTfEntry(_chartTfToken))
        {
            _mtfManager.RegisterChartTfShell(
                _chartTfToken, _shell, Bars, EstimateBarPeriod());
        }

        _mtfManager.InitializeHtfEngines(
            HtfTouchTableTfTokens,
            _chartTfToken,
            MarketData,
            CreateParameterSnapshot(),
            msg => Print($"{MessagePrefix} {msg}"));
    }

    void SyncObLtfMappingResolved()
    {
        if (_shell != null)
            _shell.State.ObLtfMappingResolved = _ltfCollector.HasLtfMapping;
    }

    void SyncLtfSnapshotFetcher()
    {
        if (_shell == null)
            return;

        _shell.State.LtfSnapshotFetcher = htfBar =>
        {
            if (!_ltfCollector.HasLtfMapping)
                return null;
            return _ltfCollector.CollectForClosedHtfBarWithRebind(MarketData, Bars, htfBar);
        };
    }

    LtfBarBundle? CollectLtfSnapshotForBarA(int index)
    {
        if (index < 1 || !_ltfCollector.HasLtfMapping)
            return null;
        return _ltfCollector.CollectForClosedHtfBarWithRebind(MarketData, Bars, index - 1);
    }

    /// <summary>Bù snapshot LTF cho HTF bar thiếu trong ring buffer (attach/realtime).</summary>
    void BackfillMissingLtfSnapshots(int startBar, int endBar)
    {
        if (!_ltfCollector.HasLtfMapping)
            return;

        var buf = _shell!.State.LtfBuffer;
        var filled = 0;
        for (var htfBar = startBar; htfBar <= endBar; htfBar++)
        {
            if (buf.HasBar(htfBar))
                continue;

            var snap = _ltfCollector.CollectForClosedHtfBarWithRebind(MarketData, Bars, htfBar);
            if (snap == null)
                continue;

            buf.PushSnapshot(htfBar, snap);
            filled++;
        }

        if (DebugMode && filled > 0)
            Print($"{MessagePrefix} LTF backfill filled={filled} range={startBar}..{endBar} flat={buf.FlatCount}");
    }

    void InitializeCompoundAlerts(string chartTfToken)
    {
        _compoundFire.SetWindowMode(CompoundWindowMode);

        var r1 = EffectiveCompoundRule(CompoundRule1, EnableCompoundRule1, CompoundAlertPresets.R1SellM5, 1);
        var r2 = EffectiveCompoundRule(CompoundRule2, EnableCompoundRule2, CompoundAlertPresets.R2BuyM5, 2);
        var r3 = EffectiveCompoundRule(CompoundRule3, EnableCompoundRule3, CompoundAlertPresets.R3BuyHlM15, 3);
        var r4 = EffectiveCompoundRule(CompoundRule4, EnableCompoundRule4, CompoundAlertPresets.R4SellHlM15, 4);
        var r5 = EffectiveCompoundRule(CompoundRule5, EnableCompoundRule5, CompoundAlertPresets.R5BuyNgM15, 5);
        var r6 = EffectiveCompoundRule(CompoundRule6, EnableCompoundRule6, CompoundAlertPresets.R6SellNgM15, 6);

        Print($"{MessagePrefix} CompoundRules {FormatCompoundRuleEnableFlags()} | WindowMode={CompoundWindowMode} SyncMode={CompoundSyncMode} EventWindow={CompoundEventValidBars}");

        var activeRules = _compoundFire.Initialize(
            r1, r2, r3, r4, r5, r6,
            msg => Print($"{MessagePrefix} {msg}"));

        if (activeRules.Count == 0)
            return;

        _mtfManager = new MtfEngineManager(Symbol.Name, Symbol.TickSize);
        _mtfManager.RegisterChartTfShell(chartTfToken, _shell!, Bars, EstimateBarPeriod());
        _mtfManager.InitializeHtfEngines(
            CompoundRuleParser.CollectRequiredTfTokens(activeRules),
            chartTfToken,
            MarketData,
            CreateParameterSnapshot(),
            Print);
        SyncAlertTracking();
    }

    /// <summary>
    /// Same contract as cBot <c>EffectiveCompoundRule</c>: Enable=false → empty (slot off).
    /// Extra: when Enable=true but text is blank (stale saved instance params), restore the preset.
    /// Saved HLNG R5/R6 strings are migrated to NG-only via <see cref="CompoundAlertPresets.NormalizeNgEventTrigger"/>.
    /// </summary>
    string EffectiveCompoundRule(string ruleText, bool enabled, string preset, int ruleNumber)
    {
        if (!enabled)
            return "";

        if (!string.IsNullOrWhiteSpace(ruleText))
            return CompoundAlertPresets.NormalizeNgEventTrigger(ruleText);

        Print($"{MessagePrefix} WARNING: Compound Rule {ruleNumber} text empty while Enable=ON — restoring preset");
        return preset;
    }

    string FormatCompoundRuleEnableFlags() =>
        $"R1={(EnableCompoundRule1 ? "ON" : "OFF")} R2={(EnableCompoundRule2 ? "ON" : "OFF")} " +
        $"R3={(EnableCompoundRule3 ? "ON" : "OFF")} R4={(EnableCompoundRule4 ? "ON" : "OFF")} " +
        $"R5={(EnableCompoundRule5 ? "ON" : "OFF")} R6={(EnableCompoundRule6 ? "ON" : "OFF")}";

    void SyncAlertTracking()
    {
        _alertTrackingRenderer ??= new AlertTrackingChartRenderer(Chart, Bars, _chartTfToken);
        _mtfManager?.ConfigureAlertTracking(
            AlertTrackingDebug,
            AlertTrackingDebug && _alertTrackingRenderer != null
                ? labels => _alertTrackingRenderer.DrawLabels(labels)
                : null,
            AlertTrackingDebug ? Print : null,
            AlertTrackingDebug && _alertTrackingRenderer != null && _chartTfToken != "5"
                ? (isBuy, t, p) => _alertTrackingRenderer.DrawM5CondFireMarker(isBuy, t, p)
                : null);
        _mtfManager?.ConfigureRealZoneDebug(RealZoneDebug);
        if (AlertTrackingDebug)
            EnsureAlertTrackingEngines();
    }

    /// <summary>M5 engine for cross-TF debug labels on chart M15+ (independent of compound rules).</summary>
    void EnsureAlertTrackingEngines()
    {
        if (!AlertTrackingDebug || _shell == null || _chartTfToken == "5")
            return;

        if (!CompoundEvalSync.TryGetTfMinutes(_chartTfToken, out var chartMinutes) || chartMinutes <= 5)
            return;

        if (_mtfManager != null && _mtfManager.HasTfEntry("5"))
            return;

        _mtfManager ??= new MtfEngineManager(Symbol.Name, Symbol.TickSize);
        if (!_mtfManager.HasTfEntry(_chartTfToken))
        {
            _mtfManager.RegisterChartTfShell(
                _chartTfToken, _shell, Bars, EstimateBarPeriod());
        }

        _mtfManager.InitializeHtfEngines(
            new[] { "5" },
            _chartTfToken,
            MarketData,
            CreateParameterSnapshot(),
            Print);
        _mtfManager.ConfigureAlertTracking(
            AlertTrackingDebug,
            AlertTrackingDebug && _alertTrackingRenderer != null
                ? labels => _alertTrackingRenderer.DrawLabels(labels)
                : null,
            AlertTrackingDebug ? Print : null,
            AlertTrackingDebug && _alertTrackingRenderer != null && _chartTfToken != "5"
                ? (isBuy, t, p) => _alertTrackingRenderer.DrawM5CondFireMarker(isBuy, t, p)
                : null);
    }

    bool HasAnyCompoundRule() => _compoundFire.HasAnyRule;

    void ApplyPineParameters()
    {
        if (_shell == null) return;
        var snapshot = CreateParameterSnapshot();
        Loop6ParameterBridge.Apply(_shell, in snapshot);
        _mtfManager?.ApplyParametersToHtfShells(in snapshot);
        SyncObLtfMappingResolved();
        SyncLtfSnapshotFetcher();
        SyncAlertTracking();
        SyncHtfTouchDebugSettings();
    }

    Loop6HostParameterSnapshot CreateParameterSnapshot() => new()
    {
        LookbackBars = LookbackBars,
        EnableKeylevel = EnableKeylevel,
        ShowObBox = ShowObBox,
        HideObLoseZin = HideObLoseZin,
        HideObZin = HideObZin,
        ShowFakeSwings = ShowFakeSwings,
        ShowLockedSwings = ShowLockedSwings,
        ShowActiveSwings = ShowActiveSwings,
        ShowSwingPushDebug = ShowSwingPushDebug,
        ShowSwingPeakMissLabels = ShowSwingPeakMissLabels,
        ShowWickNoiseDebugLabels = ShowWickNoiseDebugLabels,
        ShowObBoxMissingDebug = ShowObBoxMissingDebug,
        LockSwingCount = LockSwingCount,
        ObBoxOpacity = ObBoxOpacity,
        UseObConfirm = UseObConfirm,
        ObAtrMultiplier = ObAtrMultiplier,
        UseWickNoiseFilter = UseWickNoiseFilter,
        WickAvgLen = WickAvgLen,
        UseLtfWickConfirm = UseLtfWickConfirm,
        LtfConfirmRatio = LtfConfirmRatio,
        KeylevelUseAtrRule = KeylevelUseAtrRule,
        UseNewPullbackFilter = UseNewPullbackFilter,
        BMaxLagBars = BMaxLagBars,
        CdMaxLagBars = CdMaxLagBars,
        StrongAtrMult = StrongAtrMult,
        MediumAtrMult = MediumAtrMult,
        UseExtremePickRule = UseExtremePickRule,
        UseGapMerge = UseGapMerge,
        GapFilterAtr = GapFilterAtr,
        UseMicroSwingRule = UseMicroSwingRule,
        UseDojiForceBC = UseDojiForceBC,
        DojiBodyMaxRatio = DojiBodyMaxRatio,
        DojiForceWickRatioMin = DojiForceWickRatioMin,
        DojiForceBodyZoneMax = DojiForceBodyZoneMax,
        DojiWickDomRatio = DojiWickDomRatio,
        BreakR3MaxK = BreakR3MaxK,
        LtfBufferSize = LtfBufferSize,
        ObScanBars = ObScanBars,
        MaxObs = MaxObs,
        MinObBodyRatio = MinObBodyRatio,
        MaxDojiBodyRatio = MaxDojiBodyRatio,
        KeylevelLookback = KeylevelLookback,
        KeylevelAvgBodyLen = KeylevelAvgBodyLen,
        KeylevelMinBodyMult = KeylevelMinBodyMult,
        KeylevelAtrMult = KeylevelAtrMult,
        KeylevelAtrLen = KeylevelAtrLen,
        MaxKeylevelKeep = MaxKeylevelKeep,
        ShowKeyBorder = ShowKeyBorder,
        KeyBorderWidth = KeyBorderWidth,
        InvertBrokenKeyColor = InvertBrokenKeyColor,
        KeyOpacityActive = KeyOpacityActive,
        KeyOpacityBroken = KeyOpacityBroken,
        LabelOpacity = LabelOpacity,
        HighLabelArgb = Loop6ColorUtil.ToOpaqueArgb(HighLabelColor, 0xFF32AC45u),
        LowLabelArgb = Loop6ColorUtil.ToOpaqueArgb(LowLabelColor, 0xFFDE3B22u),
        HhLabelArgb = Loop6ColorUtil.ToOpaqueArgb(HhLabelColor, 0xFF19A11Cu),
        LlLabelArgb = Loop6ColorUtil.ToOpaqueArgb(LlLabelColor, 0xFFE63419u),
        MainLabelArgb = Loop6ColorUtil.ToOpaqueArgb(MainLabelColor, 0xFFFFFF00u),
        FakeLabelArgb = Loop6ColorUtil.ToOpaqueArgb(FakeLabelColor, 0xFF2196F3u),
        BrokenLabelArgb = Loop6ColorUtil.ToOpaqueArgb(BrokenLabelColor, 0xFF808080u),
        HighKeyBoxColor = HighKeyBoxColor,
        LowKeyBoxColor = LowKeyBoxColor,
        MainCKeyBoxColor = MainCKeyBoxColor,
    };

    public override void Calculate(int index)
    {
        if (_shell == null || Bars.Count == 0 || index >= Bars.Count)
            return;

        _diagCalculateCalls++;
        _mtfManager?.ResetDiagPassCounters();

        var lastIdx = Bars.Count - 1;
        var tfTok = Loop6ChartTimeframeToken.FromBarsTimeFrame(Bars.TimeFrame);

        // Đếm mỗi lần cTrader gọi Calculate cho nến cuối ở chế độ realtime (sweep lịch sử cuối = 1).
        if (RunningMode == RunningMode.RealTime && IsLastBar && index == lastIdx)
            _realtimeLastBarHits++;

        // Diag: capture initial last-bar index after first sweep, track every advance
        if (IsLastBar && index == lastIdx)
        {
            if (_diagInitialLastBarIdx < 0)
                _diagInitialLastBarIdx = lastIdx;
            else if (lastIdx > _diagInitialLastBarIdx)
                _diagAdvanceCount = lastIdx - _diagInitialLastBarIdx;
        }

        TryEngineWarmupOnce();

        if (_engineWarmupCompleted && index < lastIdx && index <= _warmupEndBar)
            return;

        var barSnap = BarsToCoreAdapter.ToBarSnapshot(
            Bars.OpenTimes[index],
            Bars.OpenPrices[index],
            Bars.HighPrices[index],
            Bars.LowPrices[index],
            Bars.ClosePrices[index],
            (long)Bars.TickVolumes[index]);

        var flags = MapFlagsForIndex(index);
        _shell.Series.OnCalculateBar(index, barSnap, flags);

        LtfBarBundle? ltfSnap = null;
        if (ShouldCollectLtfSnapshot(index, lastIdx, flags))
            ltfSnap = CollectLtfSnapshotForBarA(index);

        ApplyPineParameters();
        SyncObLtfMappingResolved();

        // ---- Tick Pine state engine (pivots / ob / real zones) ----
        _shell.State.TickSize = Symbol.TickSize;
        _shell.State.IsM5Mode = tfTok == "5";
        _shell.State.IsH4Chart = tfTok == "240";
        _shell.State.OnBar(_shell.Series.Buffer, index, _shell.Render, ltfSnap);

        // ---- Flush drawing commands → cTrader Chart objects ----
        FlushRenderCommands();

        var shouldEvalLive = MayEvaluateRealtimeAlerts(index);
        var appendDiag = shouldEvalLive
                         && DebugMode
                         && _diagThrottle.ShouldAppend(Bars.OpenTimes[index], DiagnosticThrottleSeconds, DateTime.UtcNow);

        var liveFormingLastBar = RunningMode == RunningMode.RealTime
                                 && IsLastBar
                                 && index == lastIdx
                                 && _realtimeLastBarHits >= 2
                                 && IsLastBarActuallyLiveForming();
        var shouldEvalBacktestBarClose = RunningMode != RunningMode.RealTime
                                         && _shell!.Series.BackfillCompleted;
        var shouldEvalSingle = RealZoneDebug
                               || (EnableAlerts && (PrintSingleAlerts || DebugMode));

        if (shouldEvalSingle && (shouldEvalLive || shouldEvalBacktestBarClose))
        {
        _shell!.M15Edge.SetHostM15CloseEdgeForCurrentEvaluation(InjectM15CloseEdgeSignal);
            EvaluateAllAlerts(
                index,
                tfTok,
                barSnap.OpenChartTimeLocal,
                appendDiag,
                liveFormingLastBar,
                flags.IsNew);
        }

        RunCompoundAlerts(index, tfTok, liveFormingLastBar, flags.IsNew);

        if (AlertTrackingDebug && _shell!.Series.BackfillCompleted
            && (shouldEvalLive || shouldEvalBacktestBarClose))
            TryEmitChartAlertTracking(index, tfTok, liveFormingLastBar, flags.IsNew);

        _shell!.State.UpdateKeyExtPrevAfterAlertEval(index);

        if (ShowDiagPanel && index == lastIdx)
            DrawDiagPanel();

        if (index == lastIdx)
        {
            RefreshRealZoneDebugPanel(index, tfTok, liveFormingLastBar, flags.IsNew);
            RefreshHtfTouchTablePanel(index, tfTok, liveFormingLastBar, flags.IsNew);
        }
    }

    void TryEmitChartAlertTracking(int index, string tfTok, bool liveFormingLastBar, bool isNewBar)
    {
        if (_shell == null)
            return;

        _shell.M15Edge.SetHostM15CloseEdgeForCurrentEvaluation(InjectM15CloseEdgeSignal);

        if (!liveFormingLastBar)
        {
            var ctx = BuildEvaluationContext(index, tfTok, isBarClosed: true, isRealtime: false, isNewBarOnRealtimeForming: isNewBar, InjectM15CloseEdgeSignal);
            MaybeEmitSingleAlertTracking(in ctx);
            MaybeEmitM5TrackingOnHtfChart(index, isLiveForming: false);
            return;
        }

        if (isNewBar && index > 0)
        {
            var ctx = BuildEvaluationContext(index - 1, tfTok, isBarClosed: true, isRealtime: false, isNewBarOnRealtimeForming: false, InjectM15CloseEdgeSignal, pineBarIndex: index);
            MaybeEmitSingleAlertTracking(in ctx);
            MaybeEmitM5TrackingOnHtfChart(index - 1, isLiveForming: true);
        }
        else
        {
            MaybeEmitM5TrackingOnHtfChart(index, isLiveForming: true);
        }
    }

    /// <summary>Draw M5 EvA/B/C + cond*EventM5 labels on chart M15+ (mapped by bar open time).</summary>
    void MaybeEmitM5TrackingOnHtfChart(int chartBarIndex, bool isLiveForming)
    {
        if (!AlertTrackingDebug || _chartTfToken == "5" || _alertTrackingRenderer == null)
            return;

        if (!CompoundEvalSync.TryGetTfMinutes(_chartTfToken, out var chartMinutes) || chartMinutes <= 5)
            return;

        EnsureAlertTrackingEngines();
        if (_mtfManager == null)
            return;

        var barOpenLocal = ChartTimeFromBars.NormalizeBarOpenTime(Bars.OpenTimes[chartBarIndex]);
        // Same rationale as RunCompoundAlerts: skip the last M5 bar in VisualBacktesting too,
        // because cTrader may report a phantom (H==L==O==C carryover) OHLC for that bar that
        // gets locked into the SeriesBuffer permanently. Feeding it 1 Calculate later (when
        // cTrader has stabilized the bar) keeps PineStateEngine on real data only.
        var chartRealtimeEdge = isLiveForming
                                || RunningMode == RunningMode.VisualBacktesting;
        _mtfManager.SyncM5AlertTrackingToChartBarClose(
            barOpenLocal.Add(EstimateBarPeriod()),
            chartRealtimeEdge,
            compoundFeedsM5: HasAnyCompoundRule());
    }

    /// <summary>
    /// Pine parity on attach: sequential closed-bar replay with LTF push before live Calculate sweep.
    /// Mirrors <see cref="PerSymbolSignalHost.Initialize"/> warmup loop.
    /// </summary>
    void TryEngineWarmupOnce()
    {
        if (_shell!.Series.BackfillCompleted || Bars.Count == 0)
            return;

        var lastIdx = Bars.Count - 1;
        var endBar = Math.Max(0, lastIdx - 1);
        // BurnIn = 300 extra bars before the display window so break events and
        // flag/D_SWING state that originate before LookbackBars are warmed correctly,
        // giving cold-start parity with a chart that has been running continuously.
        const int WarmupBurnIn = 300;
        var warmupSpan = Math.Max(LookbackBars, LtfBufferSize + ObScanBars) + WarmupBurnIn;
        var startBar = Math.Max(0, endBar - warmupSpan + 1);

        _shell.Series.InitialBackfill(lastIdx, i =>
        {
            var b = BarsToCoreAdapter.ToBarSnapshot(
                Bars.OpenTimes[i],
                Bars.OpenPrices[i],
                Bars.HighPrices[i],
                Bars.LowPrices[i],
                Bars.ClosePrices[i],
                (long)Bars.TickVolumes[i]);
            var ff = Loop6BarRuntimeMapper.ForHistoricalBackfillRow(i, lastIdx);
            return (b, ff);
        });

        _ltfCollector.EnsureLtfBars(MarketData);
        SyncObLtfMappingResolved();
        ApplyPineParameters();

        var tfTok = Loop6ChartTimeframeToken.FromBarsTimeFrame(Bars.TimeFrame);
        _shell.State.TickSize = Symbol.TickSize;
        _shell.State.IsM5Mode = tfTok == "5";
        _shell.State.IsH4Chart = tfTok == "240";

        // Pre-populate LTF for bars just before startBar so wick offset-2+ is available
        // at the very first bars of the replay (avoids HTF-only fallback on first ~WickAvgLen bars).
        var preWarmupStart = Math.Max(0, startBar - _shell.State.WickAvgLen - 2);
        if (preWarmupStart < startBar)
            BackfillMissingLtfSnapshots(preWarmupStart, startBar - 1);

        // Pre-fill ring with the last LtfBufferSize bars before the loop so that
        // LtfForOffset(2+) lookups during replay find stable LTF data. Without this,
        // bars near endBar use HTF-only fallback on first indicator load (LTF series
        // still streaming), causing cleanHigh ordering to differ vs subsequent loads
        // and producing a different EXTREME winner in the penH queue.
        // BackfillMissingLtfSnapshots is idempotent (skips bars already in ring).
        BackfillMissingLtfSnapshots(startBar, endBar);

        for (var i = startBar; i <= endBar; i++)
        {
            var barSnap = BarsToCoreAdapter.ToBarSnapshot(
                Bars.OpenTimes[i],
                Bars.OpenPrices[i],
                Bars.HighPrices[i],
                Bars.LowPrices[i],
                Bars.ClosePrices[i],
                (long)Bars.TickVolumes[i]);

            var flags = Loop6BarRuntimeMapper.ForClosedChartBarBeforeLast();
            _shell.Series.OnCalculateBar(i, barSnap, flags);

            LtfBarBundle? ltfSnap = null;
            if (ShouldCollectLtfSnapshot(i, lastIdx, flags))
                ltfSnap = CollectLtfSnapshotForBarA(i);

            _shell.State.OnBar(_shell.Series.Buffer, i, _shell.Render, ltfSnap);
        }

        BackfillMissingLtfSnapshots(startBar, endBar);
        _shell.State.ReconcileSuspiciousObLtfConfirm(endBar, _shell.Render);

        FlushRenderCommands();
        _warmupEndBar = endBar;
        _engineWarmupCompleted = true;

        if (DebugMode)
        {
            Print(
                $"{MessagePrefix} Engine warmup OK | bars={startBar}..{endBar} span={endBar - startBar + 1} " +
                $"ltfReady={_ltfCollector.IsConfigured} ltfFlat={_shell.State.LtfBuffer.FlatCount}");
        }
    }

    BarRuntimeFlags MapFlagsForIndex(int barIndexInclusive)
    {
        var lastIdx = Bars.Count - 1;
        if (RunningMode != RunningMode.RealTime)
            return MapFlagsForBacktestIndex(barIndexInclusive, lastIdx);

        if (!(IsLastBar && barIndexInclusive == lastIdx))
            return Loop6BarRuntimeMapper.ForClosedChartBarBeforeLast();

        var attachHistPhase = _realtimeLastBarHits <= 1;
        if (attachHistPhase)
        {
            _openTracker.SeedSilent(Bars.OpenTimes[barIndexInclusive]);
            return Loop6BarRuntimeMapper.ForAttachHistoryPhaseLastBar();
        }

        var isNewOt = _openTracker.TryConsumeNewBarOpen(Bars.OpenTimes[barIndexInclusive]);
        return Loop6BarRuntimeMapper.ForLiveRealtimeFormingLastBar(isNewOt);
    }

    BarRuntimeFlags MapFlagsForBacktestIndex(int barIndexInclusive, int lastIdx)
    {
        if (barIndexInclusive < lastIdx)
            return Loop6BarRuntimeMapper.ForClosedChartBarBeforeLast();

        var barOpen = Bars.OpenTimes[barIndexInclusive];
        if (!_backtestLastBarOpenInitialized)
        {
            _openTracker.SeedSilent(barOpen);
            _backtestLastBarOpenInitialized = true;
        }

        var isNewOt = _openTracker.TryConsumeNewBarOpen(barOpen);
        if (!isNewOt && IsBacktestLastBarFullyClosed(barIndexInclusive))
            return Loop6BarRuntimeMapper.ForBacktestClosedHistoricalLastBar();

        return Loop6BarRuntimeMapper.ForBacktestFormingLastBar(isNewOt);
    }

    bool IsBacktestLastBarFullyClosed(int barIndexInclusive) =>
        Server.TimeInUtc >= BarOpenToUtc(barIndexInclusive).Add(EstimateBarPeriod());

    /// <summary>
    /// Thu LTF cho HTF bar <c>index - 1</c> (bar A trong OnBar).
    /// Realtime: nến cuối đang hình thành vẫn cần LTF của nến HTF vừa đóng (index-1).
    /// </summary>
    static bool ShouldCollectLtfSnapshot(int index, int lastIdx, BarRuntimeFlags flags) =>
        flags.IsConfirmed
        || (flags.IsRealtime && flags.IsLast && index == lastIdx && index >= 1);

    /// <remarks>
    /// Chỉ true sau InitialBackfill, RunningMode.RealTime, nến cuối và sau lần đầu tính cuối (không bắn alert lịch sử lúc attach).
    /// </remarks>
    bool MayEvaluateRealtimeAlerts(int barIndexInclusive)
    {
        if (!_shell!.Series.BackfillCompleted)
            return false;
        if (RunningMode != RunningMode.RealTime)
            return false;
        if (!(IsLastBar && barIndexInclusive == Bars.Count - 1))
            return false;
        return _realtimeLastBarHits >= 2;
    }

    void RunCompoundAlerts(int index, string chartTfToken, bool liveFormingLastBar, bool isNewBarOnRealtimeForming)
    {
        if (_mtfManager == null || !HasAnyCompoundRule())
            return;

        _diagCompoundCalls++;

        var barOpenLocal = ChartTimeFromBars.NormalizeBarOpenTime(Bars.OpenTimes[index]);
        var ctx = new CompoundFireOrchestrator.FireRunContext(
            symbol:                    Symbol.Name,
            bars:                      Bars,
            chartTfToken:              chartTfToken,
            index:                     index,
            symbolTickSize:            Symbol.TickSize,
            barOpenTimeLocal:          barOpenLocal,
            barPeriod:                 EstimateBarPeriod(),
            liveFormingLastBar:        liveFormingLastBar,
            isNewBarOnRealtimeForming: isNewBarOnRealtimeForming,
            isBackfillCompleted:       _shell!.Series.BackfillCompleted,
            realtimeLastBarHits:       _realtimeLastBarHits,
            isLastBar:                 IsLastBar,
            isRealtimeMode:            RunningMode == RunningMode.RealTime,
            isVisualBacktesting:       RunningMode == RunningMode.VisualBacktesting,
            injectM15CloseEdge:        InjectM15CloseEdgeSignal,
            syncMode:                  CompoundSyncMode,
            eventValidBars:            CompoundEventValidBars);

        var events = _compoundFire.RunOneTick(
            _mtfManager,
            in ctx,
            onMinTfClosedCallback: ShowCompoundPanel ? DrawCompoundPanel : null);

        // Print fires in exactly the same format as before — no behavior change.
        if (PrintCompoundFires)
        {
            foreach (var ev in events)
                Print($"{MessagePrefix} COMPOUND R{ev.SlotIndex + 1} [{ev.RuleName}] FIRE bar={ev.ChartBarIndex} time={ev.BarOpenTime:s} close={ev.Price}");
        }

        if (ShowCompoundPanel)
            DrawCompoundPanel();
        else
            ClearCompoundPanelDrawings();
    }

    void ClearCompoundPanelDrawings()
    {
        RemoveLegacyCompoundStaticText();
        if (_compoundDraggable != null)
        {
            Chart.Draggables.Remove(_compoundDraggable);
            _compoundDraggable = null;
        }

        _compoundPanel = null;
        _compoundLineBlocks.Clear();
        _compoundDraggablePositioned = false;
    }

    void RemoveLegacyCompoundStaticText()
    {
        Chart.RemoveObject("Loop6CompoundPanel");
        for (var i = 0; i < 128; i++)
            Chart.RemoveObject(CompoundPanelObjectPrefix + i);
    }

    void EnsureCompoundDraggable()
    {
        if (_compoundDraggable != null)
            return;

        _compoundPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            BackgroundColor = Color.FromArgb(210, 18, 18, 18),
            Margin = 6,
        };

        _compoundDraggable = Chart.Draggables.Add();
        _compoundDraggable.ShowGrip = true;
        _compoundDraggable.Child = _compoundPanel;

        if (!_compoundDraggablePositioned)
        {
            _compoundDraggable.X = CompoundPanelInitialMargin;
            _compoundDraggable.Y = CompoundPanelInitialMargin;
            _compoundDraggablePositioned = true;
        }
    }

    static bool IsCompoundPanelHeaderLine(string text) =>
        text.Length > 0
        && text[0] == 'R'
        && text.Contains(" : ", StringComparison.Ordinal)
        && !text.StartsWith("   ", StringComparison.Ordinal);

    void SyncCompoundPanelUi()
    {
        EnsureCompoundDraggable();
        if (_compoundPanel == null)
            return;

        while (_compoundLineBlocks.Count > _compoundPanelLines.Count)
        {
            var last = _compoundLineBlocks.Count - 1;
            _compoundPanel.RemoveChild(_compoundLineBlocks[last]);
            _compoundLineBlocks.RemoveAt(last);
        }

        for (var i = 0; i < _compoundPanelLines.Count; i++)
        {
            var (text, color) = _compoundPanelLines[i];
            if (i >= _compoundLineBlocks.Count)
            {
                var block = new TextBlock
                {
                    FontSize = CompoundPanelFontSize,
                    Margin = 1,
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                _compoundLineBlocks.Add(block);
                _compoundPanel.AddChild(block);
            }

            var line = _compoundLineBlocks[i];
            line.Text = text;
            line.ForegroundColor = color;
            line.FontWeight = i == 0 || IsCompoundPanelHeaderLine(text)
                ? FontWeight.Bold
                : FontWeight.Normal;
        }
    }

    void DrawCompoundPanel()
    {
        if (_mtfManager == null || !HasAnyCompoundRule())
        {
            ClearCompoundPanelDrawings();
            return;
        }

        _compoundPanelLines.Clear();
        _compoundPanelLines.Add(("━ COMPOUND ALERTS ━", CompoundPanelTitleColor));

        for (var slot = 0; slot < CompoundAlertPresets.Count; slot++)
        {
            if (slot > 0)
                _compoundPanelLines.Add((CompoundPanelRuleSeparator, CompoundPanelSeparatorColor));

            var rule = _compoundFire.RulesBySlot[slot];
            var slotLabel = slot + 1;

            if (rule == null)
            {
                _compoundPanelLines.Add((
                    $"R{slotLabel} {CompoundAlertPresets.DisplayName(slot)} : (off)",
                    CompoundPanelOffColor));
                continue;
            }

            var allTrue = true;
            foreach (var entry in rule.Entries)
            {
                if (!_mtfManager.IsConditionActive(
                        entry.TfToken, entry.ConditionId, CompoundSyncMode, CompoundEventValidBars))
                {
                    allTrue = false;
                    break;
                }
            }

            var headerColor = allTrue ? CompoundPanelHeaderFireColor : CompoundPanelHeaderWaitingColor;
            _compoundPanelLines.Add((
                $"R{slotLabel} {rule.RuleName} : {(allTrue ? "FIRE" : "waiting")}",
                headerColor));

            foreach (var entry in rule.Entries)
            {
                var isActive = _mtfManager.IsConditionActive(
                    entry.TfToken, entry.ConditionId, CompoundSyncMode, CompoundEventValidBars);
                var status = _mtfManager.FormatConditionStatus(
                    entry.TfToken, entry.ConditionId, CompoundSyncMode, CompoundEventValidBars);
                _compoundPanelLines.Add((
                    $"   {status} {TfDisplayLabel(entry.TfToken)} {CompoundRuleParser.PineNameFor(entry.ConditionId)}",
                    isActive ? CompoundPanelDetailOkColor : CompoundPanelDetailColor));
            }
        }

        SyncCompoundPanelUi();
    }

    static string TfDisplayLabel(string tfToken) =>
        tfToken switch
        {
            "5" => "M5",
            "15" => "M15",
            "60" => "H1",
            "240" => "H4",
            "1440" => "D1",
            _ => tfToken,
        };

    // =====================================================================
    // DIAGNOSTIC PANEL — M5 shell health for backtest replay troubleshooting
    // =====================================================================

    static readonly Color DiagTitleColor = Color.Cyan;
    static readonly Color DiagSectionColor = Color.FromArgb(255, 200, 200, 0);
    static readonly Color DiagOkColor = Color.Lime;
    static readonly Color DiagWarnColor = Color.Orange;
    static readonly Color DiagBadColor = Color.OrangeRed;
    static readonly Color DiagNeutralColor = Color.LightGray;

    static readonly Color RealZoneTitleColor = Color.FromArgb(255, 120, 200, 255);
    static readonly Color RealZoneSectionColor = Color.FromArgb(255, 180, 220, 255);
    static readonly Color RealZoneOkColor = Color.Lime;
    static readonly Color RealZoneNoColor = Color.OrangeRed;
    static readonly Color RealZoneMutedColor = Color.DimGray;
    static readonly Color RealZoneDetailColor = Color.FromArgb(255, 200, 210, 220);

    void RefreshRealZoneDebugPanel(int index, string tfTok, bool liveFormingLastBar, bool isNewBarOnRealtimeForming)
    {
        if (!RealZoneDebug)
        {
            _realZoneDebugPanel.Clear(Chart);
            return;
        }

        if (_shell == null || !_shell.Series.BackfillCompleted)
            return;

        _shell.M15Edge.SetHostM15CloseEdgeForCurrentEvaluation(InjectM15CloseEdgeSignal);

        var isBarClosed = !liveFormingLastBar;
        var isRealtime = liveFormingLastBar;
        var ctx = BuildEvaluationContext(
            index, tfTok, isBarClosed, isRealtime, isNewBarOnRealtimeForming, InjectM15CloseEdgeSignal);

        var detail = TouchRealEvaluators.ComputeRealFlagsDetail(in ctx);
        var rf = ctx.RealtimeFilter;
        var barOpen = Bars.OpenTimes[index];

        _realZonePanelLines.Clear();
        _realZonePanelLines.Add(("━ REAL ZONE DEBUG ━", RealZoneTitleColor));
        _realZonePanelLines.Add((
            $"{Symbol.Name} {TfDisplayLabel(tfTok)} bar={index} {barOpen:MM-dd HH:mm}",
            RealZoneDetailColor));
        _realZonePanelLines.Add((
            FormattableString.Invariant(
                $"O={Bars.OpenPrices[index]:F5} H={Bars.HighPrices[index]:F5} L={Bars.LowPrices[index]:F5} C={Bars.ClosePrices[index]:F5}"),
            RealZoneDetailColor));
        _realZonePanelLines.Add((
            liveFormingLastBar ? "eval=realtime forming" : "eval=bar close",
            liveFormingLastBar ? Color.Khaki : RealZoneMutedColor));

        if (rf == null)
        {
            _realZonePanelLines.Add(("RealtimeFilter=null", RealZoneNoColor));
            _realZoneDebugPanel.Sync(Chart, _realZonePanelLines);
            return;
        }

        var swing = rf.LastPushedSwingType switch
        {
            -1 => "lastPushed=LOW → BUY real path",
            1  => "lastPushed=HIGH → SELL real path",
            _  => $"lastPushed={rf.LastPushedSwingType} (idle)",
        };
        _realZonePanelLines.Add(("─ STATE ─", RealZoneSectionColor));
        _realZonePanelLines.Add((swing, RealZoneDetailColor));

        if (rf.LastPushedSwingType == -1)
        {
            _realZonePanelLines.Add((
                FormattableString.Invariant($"RealBuyBot={Fmt(rf.RealBuyBot)} RealBuyTop={Fmt(rf.RealBuyTop)}"),
                RealZoneDetailColor));
            _realZonePanelLines.Add((
                FormattableString.Invariant($"EffBuyBreakBot={Fmt(rf.EffBuyBreakBot)}"),
                RealZoneDetailColor));
        }
        else if (rf.LastPushedSwingType == 1)
        {
            _realZonePanelLines.Add((
                FormattableString.Invariant($"RealSellBot={Fmt(rf.RealSellBot)} RealSellTop={Fmt(rf.RealSellTop)}"),
                RealZoneDetailColor));
            _realZonePanelLines.Add((
                FormattableString.Invariant($"EffSellBreakTop={Fmt(rf.EffSellBreakTop)}"),
                RealZoneDetailColor));
        }

        _realZonePanelLines.Add(("─ ZONE CHECK ─", RealZoneSectionColor));
        AppendRealZoneWrappedLines(detail.DebugText, RealZoneDetailColor);
        _realZonePanelLines.Add((
            FormattableString.Invariant($"CanBuy={detail.CanBuy} CanSell={detail.CanSell}"),
            detail.CanBuy || detail.CanSell ? RealZoneOkColor : RealZoneNoColor));

        _realZonePanelLines.Add(("─ ALERTS ─", RealZoneSectionColor));
        AppendRealZoneAlertLine("CanBuyReal", AlertConditionId.CanBuyReal, in ctx);
        AppendRealZoneAlertLine("CanSellReal", AlertConditionId.CanSellReal, in ctx);
        AppendRealZoneAlertLine("CanBuyReal+M15", AlertConditionId.CanBuyRealAndM15CloseNow, in ctx);
        AppendRealZoneAlertLine("CanSellReal+M15", AlertConditionId.CanSellRealAndM15CloseNow, in ctx);

        if (!ctx.M15CloseEdgeInjected)
            _realZonePanelLines.Add(("M15 edge=off (inject param off / not boundary)", RealZoneMutedColor));
        else
            _realZonePanelLines.Add(("M15 edge=on", Color.Khaki));

        _realZoneDebugPanel.Sync(Chart, _realZonePanelLines);
    }

    void AppendRealZoneAlertLine(string label, AlertConditionId id, in AlertEvaluationContext ctx)
    {
        var r = _shell!.Alerts.Engine.Evaluate(id, in ctx);
        var status = r.Fired ? "FIRE" : "no";
        var color = r.Fired ? RealZoneOkColor : RealZoneNoColor;
        var reason = r.ReasonText ?? "";
        if (reason.Length > 72)
            reason = reason[..69] + "...";
        _realZonePanelLines.Add(($"{label,-16} [{status}] {reason}", color));
    }

    void AppendRealZoneWrappedLines(string text, Color color)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _realZonePanelLines.Add(("(empty)", RealZoneMutedColor));
            return;
        }

        const int maxLen = 64;
        var start = 0;
        while (start < text.Length)
        {
            var len = Math.Min(maxLen, text.Length - start);
            if (start + len < text.Length)
            {
                var slice = text.AsSpan(start, len);
                var breakAt = slice.LastIndexOf(' ');
                if (breakAt > 20)
                    len = breakAt;
            }

            _realZonePanelLines.Add((text.Substring(start, len).Trim(), color));
            start += len;
            while (start < text.Length && text[start] == ' ')
                start++;
        }
    }

    static readonly Color HtfTouchTitleColor = Color.FromArgb(255, 100, 220, 180);
    static readonly Color HtfTouchSectionColor = Color.FromArgb(255, 160, 210, 190);
    static readonly Color HtfTouchOkColor = Color.Lime;
    static readonly Color HtfTouchNoColor = Color.OrangeRed;
    static readonly Color HtfTouchInZoneColor = Color.Khaki;
    static readonly Color HtfTouchDetailColor = Color.FromArgb(255, 195, 205, 215);
    static readonly Color HtfTouchMutedColor = Color.DimGray;

    void RefreshHtfTouchTablePanel(int index, string tfTok, bool liveFormingLastBar, bool isNewBarOnRealtimeForming)
    {
        if (!HtfTouchTableDebug)
        {
            _htfTouchTablePanel.Clear(Chart);
            return;
        }

        if (_shell == null || !_shell.Series.BackfillCompleted || _mtfManager == null)
            return;

        EnsureHtfTouchDebugEngines();
        SyncMtfForHtfTouchTableDebug(index, tfTok, liveFormingLastBar, isNewBarOnRealtimeForming);

        _shell.M15Edge.SetHostM15CloseEdgeForCurrentEvaluation(InjectM15CloseEdgeSignal);

        var isBarClosed = !liveFormingLastBar;
        var isRealtime = liveFormingLastBar;
        var barOpenLocal = ChartTimeFromBars.NormalizeBarOpenTime(Bars.OpenTimes[index]);
        var chartM15Edge = AlertBarTiming.ComputeM15CloseEdge(
            tfTok,
            isBarClosed,
            isRealtime,
            isNewBarOnRealtimeForming,
            barOpenLocal.Minute,
            InjectM15CloseEdgeSignal);
        var barCloseTime = barOpenLocal.Add(EstimateBarPeriod());

        var table = _mtfManager.DescribeHtfTouchProbeTableFireDebug(
            tfTok,
            index,
            isBarClosed,
            isRealtime,
            isNewBarOnRealtimeForming,
            chartM15Edge,
            barCloseTime,
            Bars.HighPrices[index],
            Bars.LowPrices[index],
            Bars.ClosePrices[index]);

        _htfTouchPanelLines.Clear();
        _htfTouchPanelLines.Add(("━ HTF TOUCH TABLE (chart TF + H1/H4 + M5 probe) ━", HtfTouchTitleColor));
        _htfTouchPanelLines.Add((
            FormattableString.Invariant(
                $"{Symbol.Name} chart={TfDisplayLabel(tfTok)} bar={index} {barOpenLocal:MM-dd HH:mm}"),
            HtfTouchDetailColor));

        if (string.IsNullOrWhiteSpace(table))
        {
            _htfTouchPanelLines.Add(("(no zone data — wait backfill / check HTF engines)", HtfTouchMutedColor));
            _htfTouchTablePanel.Sync(Chart, _htfTouchPanelLines);
            return;
        }

        foreach (var line in table.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;
            _htfTouchPanelLines.Add((trimmed, ColorForHtfTouchTableLine(trimmed)));
        }

        _htfTouchTablePanel.Sync(Chart, _htfTouchPanelLines);
    }

    static Color ColorForHtfTouchTableLine(string line)
    {
        if (line.Contains("===", StringComparison.Ordinal))
            return HtfTouchSectionColor;
        if (line.Contains("**LAG**", StringComparison.Ordinal))
            return Color.OrangeRed;
        if (line.StartsWith("    SYNC", StringComparison.Ordinal))
            return line.Contains("HTF-engine", StringComparison.Ordinal) && line.Contains("forming=Y", StringComparison.Ordinal)
                ? Color.Orange
                : HtfTouchDetailColor;
        if (line.StartsWith("    PIVOT-AUDIT", StringComparison.Ordinal))
            return HtfTouchSectionColor;
        if (line.Contains("→ GKL", StringComparison.Ordinal))
            return HtfTouchOkColor;
        if (line.Contains("→ RKL", StringComparison.Ordinal))
            return Color.Salmon;
        if (line.Contains("→ SKIP", StringComparison.Ordinal))
            return HtfTouchMutedColor;
        if (line.Contains("cache=OK", StringComparison.Ordinal))
            return HtfTouchOkColor;
        if (line.Contains("cache=--", StringComparison.Ordinal))
            return HtfTouchNoColor;
        if (line.Contains("*buyIN*", StringComparison.Ordinal) || line.Contains("*sellIN*", StringComparison.Ordinal))
            return HtfTouchInZoneColor;
        if (line.StartsWith("canBuyTouchM5", StringComparison.Ordinal)
            || line.StartsWith("canSellTouchM5", StringComparison.Ordinal))
            return HtfTouchDetailColor;
        return HtfTouchDetailColor;
    }

    void SyncMtfForHtfTouchTableDebug(int index, string tfTok, bool liveFormingLastBar, bool isNewBarOnRealtimeForming)
    {
        if (_mtfManager == null)
            return;

        var barOpenLocal = ChartTimeFromBars.NormalizeBarOpenTime(Bars.OpenTimes[index]);
        var barCloseTime = barOpenLocal.Add(EstimateBarPeriod());
        var isBarClosed = !liveFormingLastBar;
        var isRealtime = liveFormingLastBar;
        var chartM15Edge = AlertBarTiming.ComputeM15CloseEdge(
            tfTok,
            isBarClosed,
            isRealtime,
            isNewBarOnRealtimeForming,
            barOpenLocal.Minute,
            InjectM15CloseEdgeSignal);
        var chartRealtimeEdge = liveFormingLastBar || RunningMode == RunningMode.VisualBacktesting;

        _mtfManager.SetCurrentChartBarIndex(index);
        _mtfManager.AdvanceHtfEnginesToChartBarCloseTime(
            barCloseTime,
            HtfTouchTableTrackedConds,
            chartRealtimeEdge);

        _mtfManager.SyncHtfFormingBarsToChartCloseTime(
            barCloseTime,
            HtfTouchTableTrackedConds,
            chartRealtimeEdge,
            chartM15Edge);

        var shouldSimIntraBar = isBarClosed && RunningMode != RunningMode.RealTime;
        if (shouldSimIntraBar || liveFormingLastBar)
        {
            _mtfManager.UpdateRealtimePriceFromChart(
                index,
                Bars.HighPrices[index],
                Bars.LowPrices[index],
                Symbol.TickSize,
                HtfTouchTableTrackedConds,
                chartM15Edge,
                updateChartTfEntry: shouldSimIntraBar || liveFormingLastBar,
                chartBarCloseTimeChartLocal: barCloseTime);
        }

        if (HtfTouchTableTrackedConds.TryGetValue(tfTok, out var chartConds))
        {
            _mtfManager.UpdateConditionCache(
                tfTok,
                index,
                Bars.OpenTimes[index],
                isBarClosed,
                isRealtime,
                isNewBarOnRealtimeForming,
                chartM15Edge,
                chartConds,
                reevalStateOnBarClose: !shouldSimIntraBar);
        }
    }

    static string Fmt(double? v) => v.HasValue ? v.Value.ToString("F5", System.Globalization.CultureInfo.InvariantCulture) : "na";

    void EnsureDiagDraggable()
    {
        if (_diagDraggable != null) return;
        _diagPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            BackgroundColor = Color.FromArgb(220, 10, 20, 30),
            Margin = 6,
        };
        _diagDraggable = Chart.Draggables.Add();
        _diagDraggable.ShowGrip = true;
        _diagDraggable.Child = _diagPanel;
        if (!_diagDraggablePositioned)
        {
            _diagDraggable.X = 320;  // right of compound panel
            _diagDraggable.Y = 20;
            _diagDraggablePositioned = true;
        }
    }

    void ClearDiagPanelDrawings()
    {
        if (_diagDraggable != null)
        {
            Chart.Draggables.Remove(_diagDraggable);
            _diagDraggable = null;
        }
        _diagPanel = null;
        _diagLineBlocks.Clear();
        _diagDraggablePositioned = false;
    }

    void SyncDiagPanelUi()
    {
        EnsureDiagDraggable();
        if (_diagPanel == null) return;

        while (_diagLineBlocks.Count > _diagPanelLines.Count)
        {
            var last = _diagLineBlocks.Count - 1;
            _diagPanel.RemoveChild(_diagLineBlocks[last]);
            _diagLineBlocks.RemoveAt(last);
        }

        for (var i = 0; i < _diagPanelLines.Count; i++)
        {
            var (text, color) = _diagPanelLines[i];
            if (i >= _diagLineBlocks.Count)
            {
                var block = new TextBlock
                {
                    FontSize = 10,
                    Margin = 1,
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                _diagLineBlocks.Add(block);
                _diagPanel.AddChild(block);
            }
            var line = _diagLineBlocks[i];
            line.Text = text;
            line.ForegroundColor = color;
            line.FontWeight = (i == 0 || text.StartsWith("─", StringComparison.Ordinal))
                ? FontWeight.Bold : FontWeight.Normal;
        }
    }

    void DrawDiagPanel()
    {
        if (!ShowDiagPanel)
        {
            ClearDiagPanelDrawings();
            return;
        }

        _diagPanelLines.Clear();
        _diagPanelLines.Add(("━ M5 SHELL DIAG ━", DiagTitleColor));

        // ---- Section 1: Backtest / Live detection ----
        _diagPanelLines.Add(("─ MODE ─", DiagSectionColor));
        _diagPanelLines.Add(($"RunMode={RunningMode} IsLastBar={IsLastBar} hits={_realtimeLastBarHits}", DiagNeutralColor));
        var serverNow = Server.TimeInUtc;
        var lastBarOpen = Bars.Count > 0 ? Bars.OpenTimes[Bars.Count - 1] : DateTime.MinValue;
        var period = EstimateBarPeriod();
        var expClose = lastBarOpen.Add(period);
        var liveForming = IsLastBarActuallyLiveForming();
        _diagPanelLines.Add(($"server={serverNow:MM-dd HH:mm:ss}", DiagNeutralColor));
        _diagPanelLines.Add(($"lastBar={lastBarOpen:MM-dd HH:mm} +{period.TotalMinutes:0}m", DiagNeutralColor));
        _diagPanelLines.Add(($"expClose={expClose:MM-dd HH:mm:ss}", DiagNeutralColor));
        _diagPanelLines.Add(($"IsLastBarActuallyLiveForming = {liveForming}", liveForming ? DiagWarnColor : DiagOkColor));
        var rtEdgeActive = liveForming || RunningMode == RunningMode.VisualBacktesting;
        _diagPanelLines.Add(($"chartRealtimeEdge = {rtEdgeActive} (skips last M5 = anti-phantom)", rtEdgeActive ? DiagOkColor : DiagWarnColor));

        // ---- Section 2: Chart Bars + counters ----
        _diagPanelLines.Add(("─ CHART (M15) ─", DiagSectionColor));
        _diagPanelLines.Add(($"Bars.Count={Bars.Count}", DiagNeutralColor));
        _diagPanelLines.Add(($"CalcCalls={_diagCalculateCalls} CompCalls={_diagCompoundCalls}", DiagNeutralColor));
        var advanceTxt = _diagInitialLastBarIdx >= 0
            ? $"advance={_diagAdvanceCount} (initLastIdx={_diagInitialLastBarIdx})"
            : "advance=? (init pending)";
        _diagPanelLines.Add((advanceTxt, _diagAdvanceCount == 0 ? DiagNeutralColor : DiagOkColor));

        AppendChartLtfWickDiagSection();

        // ---- Section 3: M5 entry health ----
        _diagPanelLines.Add(("─ M5 ENGINE ─", DiagSectionColor));
        if (_mtfManager == null)
        {
            _diagPanelLines.Add(("_mtfManager=NULL", DiagBadColor));
        }
        else if (!_mtfManager.TryGetDiagInfo("5", out var info))
        {
            _diagPanelLines.Add(("M5 entry NOT registered", DiagBadColor));
            _diagPanelLines.Add((_compoundFire.TrackedConditionsPerTf.ContainsKey("5")
                ? "but M5 in trackedConditions ✓"
                : "M5 NOT in trackedConditions ✗", DiagWarnColor));
        }
        else
        {
            _diagPanelLines.Add(($"TfBars.Count={info.TfBarsCount}", DiagNeutralColor));
            _diagPanelLines.Add(($"TfBars.LastOpen={info.TfBarsLastOpenTime:MM-dd HH:mm}", DiagNeutralColor));
            var processed = info.NextBarIdxToProcess;
            var unprocessed = info.TfBarsCount - processed;
            var cursorColor = unprocessed == 0 ? DiagWarnColor : DiagOkColor;
            _diagPanelLines.Add(($"NextIdx={processed}/{info.TfBarsCount} (unproc={unprocessed})", cursorColor));
            _diagPanelLines.Add(($"EmitThrough={info.AlertTrackEmitThroughIdx}", DiagNeutralColor));
            _diagPanelLines.Add(($"LastEvalBar={info.LastEvaluatedBarIndex} stateBar={info.ShellStateLastEvalBar}", DiagNeutralColor));
            _diagPanelLines.Add(($"Pivots.Count={info.PivotsCount}", DiagNeutralColor));

            if (_mtfManager.TryGetPivotFlagDistribution("5", out var act, out var pen, out var brk, out var mbrk))
            {
                _diagPanelLines.Add(($"Flags: act={act} pen={pen} brk={brk} mbrk={mbrk}",
                    pen == 0 && brk == 0 ? DiagWarnColor : DiagNeutralColor));
            }

            _diagPanelLines.Add((
                $"Dedup A:b={info.DedupABuy}/s={info.DedupASell} " +
                $"B:b={info.DedupBBuy}/s={info.DedupBSell} " +
                $"C:b={info.DedupCBuy}/s={info.DedupCSell}", DiagNeutralColor));

            // ---- ATR / clear-body precondition for pivot-break ----
            _diagPanelLines.Add(("─ M5 ATR / BODY (last bar) ─", DiagSectionColor));
            var atrColor = double.IsNaN(info.AtrWilder14) ? DiagBadColor : DiagOkColor;
            _diagPanelLines.Add(($"ATR14={FormatPrice(info.AtrWilder14)} ATRkey={FormatPrice(info.AtrKeylevel)} (seen={info.AtrBarsSeen14})", atrColor));
            _diagPanelLines.Add(($"avgBody={FormatPrice(info.AvgBody)}", double.IsNaN(info.AvgBody) ? DiagBadColor : DiagNeutralColor));

            // OHLC at lag 1 (what PivotTransitionEngine actually read)
            var ohlcColor = (info.Ohlc1High > 0 && info.Ohlc1Low > 0 && info.Ohlc1High > info.Ohlc1Low)
                ? DiagOkColor : DiagBadColor;
            _diagPanelLines.Add(($"OHLC1 O={FormatPrice(info.Ohlc1Open)} H={FormatPrice(info.Ohlc1High)}", ohlcColor));
            _diagPanelLines.Add(($"      L={FormatPrice(info.Ohlc1Low)} C={FormatPrice(info.Ohlc1Close)}", ohlcColor));

            _diagPanelLines.Add(($"rawBody={FormatPrice(info.RawBody1)} rawRange={FormatPrice(info.RawRange1)}", DiagNeutralColor));
            _diagPanelLines.Add(($"rawIsDoji={info.RawIsDoji1} rawClearBody={info.RawClearBody1}",
                info.RawClearBody1 ? DiagOkColor : DiagBadColor));
            _diagPanelLines.Add(($"effIsDoji={info.EffIsDoji} effClearBody={info.EffClearBody}",
                info.EffClearBody ? DiagOkColor : DiagNeutralColor));
            if (!double.IsNaN(info.AvgBody))
            {
                var threshBody = info.AvgBody * 1.5;
                var threshAtr = double.IsNaN(info.AtrKeylevel) ? double.NaN : info.AtrKeylevel * 0.2;
                _diagPanelLines.Add(($"need rawBody >= {FormatPrice(threshBody)} (avgBody×1.5) OR {FormatPrice(threshAtr)} (ATR×0.2)", DiagNeutralColor));
            }

            // Compare buffer-stored OHLC vs LIVE cTrader OHLC for same bar — proves whether
            // cTrader populated the bar AFTER we fed it (race) or it was always phantom.
            try
            {
                var tfBars = _mtfManager.GetTfBarsForDiag("5");
                if (tfBars != null && tfBars.Count > 0)
                {
                    var liLast = tfBars.Count - 1;
                    var loLast = tfBars.OpenPrices[liLast];
                    var hiLast = tfBars.HighPrices[liLast];
                    var lwLast = tfBars.LowPrices[liLast];
                    var clLast = tfBars.ClosePrices[liLast];
                    var liveLastColor = (hiLast > lwLast) ? DiagOkColor : DiagBadColor;
                    _diagPanelLines.Add(($"LIVE tfBars[{liLast}] O={FormatPrice(loLast)} H={FormatPrice(hiLast)}", liveLastColor));
                    _diagPanelLines.Add(($"                  L={FormatPrice(lwLast)} C={FormatPrice(clLast)}", liveLastColor));

                    // Same bar that engine just read as offset-1 in last OnBar
                    var liOffset1 = info.LastEvaluatedBarIndex - 1;
                    if (liOffset1 >= 0 && liOffset1 < tfBars.Count)
                    {
                        var lo1 = tfBars.OpenPrices[liOffset1];
                        var hi1 = tfBars.HighPrices[liOffset1];
                        var lw1 = tfBars.LowPrices[liOffset1];
                        var cl1 = tfBars.ClosePrices[liOffset1];
                        // Compare with buffered Ohlc1 — if they differ, buffer is stale
                        var stale = Math.Abs(hi1 - info.Ohlc1High) > 1e-9
                                    || Math.Abs(lw1 - info.Ohlc1Low) > 1e-9;
                        var sameBarColor = stale ? DiagBadColor : DiagOkColor;
                        _diagPanelLines.Add(($"LIVE same bar[{liOffset1}] H={FormatPrice(hi1)} L={FormatPrice(lw1)} {(stale ? "STALE!" : "match")}", sameBarColor));
                    }

                    // Count phantoms (H==L) in last 10 LIVE bars vs buffered window
                    int liveFlatLast10 = 0;
                    int totalCheck = Math.Min(10, tfBars.Count);
                    for (var k = 0; k < totalCheck; k++)
                    {
                        var ii = tfBars.Count - 1 - k;
                        if (tfBars.HighPrices[ii] <= tfBars.LowPrices[ii]) liveFlatLast10++;
                    }
                    var flatColor = liveFlatLast10 >= 5 ? DiagWarnColor : DiagOkColor;
                    _diagPanelLines.Add(($"LIVE flat(H<=L) in last {totalCheck}: {liveFlatLast10}", flatColor));
                }
            }
            catch { /* ignore — diag only */ }
        }

        // ---- Section 4: Feed + Emit history ----
        _diagPanelLines.Add(("─ FEED / EMIT (M5) ─", DiagSectionColor));
        if (_mtfManager != null)
        {
            var fe = _mtfManager.GetDiagFeedEmit("5");
            _diagPanelLines.Add(($"FeedTotal={fe.FeedTotal} lastIdx={fe.FeedLastIdx}", DiagNeutralColor));
            _diagPanelLines.Add(($"FedInLastCalc={fe.FeedThisPass}",
                fe.FeedThisPass == 0 && _diagAdvanceCount > 0 ? DiagBadColor : DiagOkColor));
            if (fe.EmitLastIdx >= 0)
            {
                _diagPanelLines.Add(($"LastEmit bar={fe.EmitLastIdx} t={fe.EmitLastTime:MM-dd HH:mm}", DiagNeutralColor));
                var hits = fe.EmitBuyHL + fe.EmitSellHL + fe.EmitBuyNG + fe.EmitSellNG;
                _diagPanelLines.Add((
                    $"counts bHL={fe.EmitBuyHL} sHL={fe.EmitSellHL} bNG={fe.EmitBuyNG} sNG={fe.EmitSellNG}",
                    hits == 0 ? DiagWarnColor : DiagOkColor));
                _diagPanelLines.Add(($"pivotsAtEmit={fe.EmitPivots}", DiagNeutralColor));
            }
            else
            {
                _diagPanelLines.Add(("LastEmit=<none>", DiagBadColor));
            }
        }

        // ---- Section 5: Cond M5 status ----
        _diagPanelLines.Add(("─ COND M5 ─", DiagSectionColor));
        if (_mtfManager != null)
        {
            FormatCondHit("condBuyEventM5", AlertConditionId.CondBuyEventM5);
            FormatCondHit("condSellEventM5", AlertConditionId.CondSellEventM5);
        }

        SyncDiagPanelUi();
    }

    static string FormatPrice(double v) =>
        double.IsNaN(v) ? "NaN" : v.ToString("0.00000");

    void AppendChartLtfWickDiagSection()
    {
        _diagPanelLines.Add(("─ LTF WICK (chart bar A) ─", DiagSectionColor));
        if (_shell == null)
        {
            _diagPanelLines.Add(("shell=NULL", DiagBadColor));
            return;
        }

        var st = _shell.State;
        var barA = st.DiagBarAIndex;
        var barAOpen = barA >= 0 && barA < Bars.Count
            ? Bars.OpenTimes[barA]
            : DateTime.MinValue;

        _diagPanelLines.Add((
            $"chartTf={_chartTfToken} barA={barA} evalBar={st.LastEvaluatedBarIndex} open={barAOpen:MM-dd HH:mm}",
            barA >= 0 ? DiagOkColor : DiagBadColor));
        _diagPanelLines.Add((
            $"wickFilter={UseWickNoiseFilter} ltfWick={UseLtfWickConfirm} ratio={LtfConfirmRatio:0.##} wickLen={WickAvgLen}",
            UseLtfWickConfirm ? DiagOkColor : DiagWarnColor));
        _diagPanelLines.Add((
            $"LTF map={_ltfCollector.HasLtfMapping} ltfTf={_ltfCollector.LtfTfToken} configured={_ltfCollector.IsConfigured}",
            _ltfCollector.HasLtfMapping ? DiagOkColor : DiagWarnColor));

        _diagPanelLines.Add((
            $"HTF-A O={FormatPrice(st.DiagLastOhlc1Open)} H={FormatPrice(st.DiagLastOhlc1High)}",
            st.DiagLastOhlc1High > st.DiagLastOhlc1Low ? DiagOkColor : DiagBadColor));
        _diagPanelLines.Add((
            $"       L={FormatPrice(st.DiagLastOhlc1Low)} C={FormatPrice(st.DiagLastOhlc1Close)}",
            DiagNeutralColor));

        var w = st.DiagLastWickBarA;
        _diagPanelLines.Add((
            $"cleanH={FormatPrice(w.CleanHigh)} cleanL={FormatPrice(w.CleanLow)} " +
            $"neutH={(w.UpperConfirmed ? "Y" : "n")} neutL={(w.LowerConfirmed ? "Y" : "n")}",
            w.UpperConfirmed || w.LowerConfirmed ? DiagOkColor : DiagNeutralColor));
        _diagPanelLines.Add((
            $"ltfMode={w.UseLtfMode} ltfBars={w.LtfBarCount} avgRng={FormatPrice(w.AvgRangePrev)} body={FormatPrice(w.BodySizePrev)}",
            w.UseLtfMode ? DiagOkColor : (UseLtfWickConfirm ? DiagWarnColor : DiagNeutralColor)));

        if (w.ValidUpperWick || w.ValidLowerWick)
        {
            _diagPanelLines.Add((
                $"↑wick={FormatPrice(w.UpperWick)} valid={w.ValidUpperWick} ltfMaxBody={FormatPrice(w.LtfMaxBodyHigh)} pen={FormatRatio(w.UpperPenetrationRatio)} conf={w.UpperConfirmed}",
                w.UpperConfirmed ? DiagOkColor : DiagNeutralColor));
            _diagPanelLines.Add((
                $"↓wick={FormatPrice(w.LowerWick)} valid={w.ValidLowerWick} ltfMinBody={FormatPrice(w.LtfMinBodyLow)} pen={FormatRatio(w.LowerPenetrationRatio)} conf={w.LowerConfirmed}",
                w.LowerConfirmed ? DiagOkColor : DiagNeutralColor));
        }

        // LTF bars fed into wick engine on last OnBar (from snapshot at bar A)
        if (st.DiagLtfBarCount > 0)
        {
            _diagPanelLines.Add(($"LTF slice used (engine, n={st.DiagLtfBarCount}):", DiagSectionColor));
            for (var i = 0; i < st.DiagLtfBarCount; i++)
            {
                _diagPanelLines.Add((
                    $"  [{i}] O={FormatPrice(st.DiagLtfOpen[i])} H={FormatPrice(st.DiagLtfHigh[i])} " +
                    $"L={FormatPrice(st.DiagLtfLow[i])} C={FormatPrice(st.DiagLtfClose[i])}",
                    DiagOkColor));
            }
        }
        else
        {
            _diagPanelLines.Add(("LTF slice used: <empty>", DiagBadColor));
        }

        // Fresh collect at barA for cross-check (M15→M5 at index-1)
        if (barA >= 0 && _ltfCollector.HasLtfMapping)
        {
            try
            {
                var fresh = _ltfCollector.CollectForClosedHtfBarWithRebind(MarketData, Bars, barA);
                if (fresh is { Count: > 0 })
                {
                    _diagPanelLines.Add(($"LTF re-collect @barA={barA} (n={fresh.Count}):", DiagNeutralColor));
                    for (var i = 0; i < fresh.Count; i++)
                    {
                        var (o, h, l, c) = fresh.OhlcAt(i);
                        var match = i < st.DiagLtfBarCount
                                    && Math.Abs(o - st.DiagLtfOpen[i]) < 1e-9
                                    && Math.Abs(h - st.DiagLtfHigh[i]) < 1e-9;
                        _diagPanelLines.Add((
                            $"  m5[{i}] O={FormatPrice(o)} H={FormatPrice(h)} L={FormatPrice(l)} C={FormatPrice(c)} {(match ? "match" : "DIFF")}",
                            match ? DiagOkColor : DiagBadColor));
                    }
                }
                else
                {
                    _diagPanelLines.Add(($"LTF re-collect @barA={barA}: <empty>", DiagBadColor));
                }
            }
            catch (Exception ex)
            {
                _diagPanelLines.Add(($"LTF re-collect error: {ex.Message}", DiagBadColor));
            }
        }
    }

    static string FormatRatio(double v) =>
        double.IsNaN(v) ? "NaN" : v.ToString("0.###");

    void FormatCondHit(string label, AlertConditionId condId)
    {
        if (_mtfManager!.TryGetConditionHit("5", condId, out var hit))
        {
            if (hit.LastFiredBarIndex < 0)
                _diagPanelLines.Add(($"{label}: never fired", DiagWarnColor));
            else
                _diagPanelLines.Add((
                    $"{label}: bar={hit.LastFiredBarIndex} @chart={hit.LastFiredChartBarIndex} t={hit.LastFiredBarOpenTime:HH:mm}",
                    DiagOkColor));
        }
        else
        {
            _diagPanelLines.Add(($"{label}: NOT in cache", DiagBadColor));
        }
    }

    void EvaluateAllAlerts(
        int index,
        string tfTok,
        DateTime fireTsChartLocal,
        bool appendDiagBatch,
        bool liveFormingLastBar,
        bool isNewBarOnRealtimeForming)
    {
        var alerts = _shell!.Alerts;
        var isBarClosed = !liveFormingLastBar;
        var isRealtime = liveFormingLastBar;
        var m15HostInject = InjectM15CloseEdgeSignal;

        EvaluateAllAlertsPass(
            alerts, index, tfTok, fireTsChartLocal, appendDiagBatch,
            isBarClosed, isRealtime, isNewBarOnRealtimeForming, m15HostInject,
            passIsBarCloseEval: false, passIsRealtimeStateEval: true);

        if (isBarClosed)
        {
            EvaluateAllAlertsPass(
                alerts, index, tfTok, fireTsChartLocal, appendDiagBatch,
                isBarClosed: true, isRealtime: false, isNewBarOnRealtimeForming: false, m15HostInject,
                passIsBarCloseEval: true, passIsRealtimeStateEval: true);
        }
        else if (isRealtime && isNewBarOnRealtimeForming && index > 0)
        {
            var closedOpen = ChartTimeFromBars.NormalizeBarOpenTime(Bars.OpenTimes[index - 1]);
            EvaluateAllAlertsPass(
                alerts, index - 1, tfTok, closedOpen, appendDiagBatch,
                isBarClosed: true, isRealtime: false, isNewBarOnRealtimeForming: false, m15HostInject,
                passIsBarCloseEval: true, passIsRealtimeStateEval: false);
        }

        if (ShowStaticDiagnostics && appendDiagBatch)
        {
            _diagText.Clear();
            _diagText.Append(MessagePrefix)
                .Append(" evalBar=").Append(index)
                .Append(" ").Append(Bars.OpenTimes[index].ToString("s"));

            Chart.DrawStaticText(
                $"{nameof(TradeAlertLoop6Host)}Diag",
                _diagText.ToString(),
                VerticalAlignment.Bottom,
                HorizontalAlignment.Right,
                Color.DimGray);
        }
    }

    void EvaluateAllAlertsPass(
        AlertPipelineHost alerts,
        int barIndex,
        string tfTok,
        DateTime fireTsChartLocal,
        bool appendDiagBatch,
        bool isBarClosed,
        bool isRealtime,
        bool isNewBarOnRealtimeForming,
        bool m15HostInject,
        bool passIsBarCloseEval,
        bool passIsRealtimeStateEval)
    {
        var ctx = BuildEvaluationContext(
            barIndex, tfTok, isBarClosed, isRealtime, isNewBarOnRealtimeForming, m15HostInject);

        foreach (var id in alerts.Engine.Registry.Keys)
        {
            var def = alerts.Engine.Registry[id];
            if (!AlertBarTiming.ShouldEvaluateOnPass(
                    def.TimingClass, passIsBarCloseEval, passIsRealtimeStateEval))
                continue;

            var dupKey = DuplicateKeyLoop6.From(in ctx, in def);

            var appendLog = appendDiagBatch || (RealZoneDebug && IsRealAlertCondition(id));
            var logCountBefore = alerts.LogEntries.Count;
            alerts.EvaluateRecordAndMaybeFire(id, in ctx, fireTsChartLocal, in dupKey, appendLog);
            if (RealZoneDebug && IsRealAlertCondition(id) && alerts.LogEntries.Count > logCountBefore)
                Print($"{MessagePrefix} REALDBG [{def.Title}] {alerts.LogEntries[^1].ReasonText}");
        }
    }

    static bool IsRealAlertCondition(AlertConditionId id) =>
        id is AlertConditionId.CanBuyReal
            or AlertConditionId.CanSellReal
            or AlertConditionId.CanBuyRealAndM15CloseNow
            or AlertConditionId.CanSellRealAndM15CloseNow;

    void MaybeEmitSingleAlertTracking(in AlertEvaluationContext ctx)
    {
        if (!AlertTrackingDebug || _shell == null || _alertTrackingRenderer == null)
            return;

        var cache = _shell.EventDetectionCache;
        void Draw(IReadOnlyList<EventAlertTrackingLabel> labels) =>
            _alertTrackingRenderer.DrawLabels(labels);

        if (ctx.IsM5JustClosed)
        {
            EventFamilyEvaluators.ResolveRawCounts(in ctx, isM15: false, alertTrackingDebug: true, cache);
            cache.TryEmitTracking(ctx.SourceBarIndex, ctx.ChartTimeframeToken, isM15: false, Draw);
        }
    }

    AlertEvaluationContext BuildEvaluationContext(
        int index,
        string tfTok,
        bool isBarClosed,
        bool isRealtime,
        bool isNewBarOnRealtimeForming,
        bool m15HostInject,
        int? pineBarIndex = null)
    {
        var barOpenChartLocal = ChartTimeFromBars.NormalizeBarOpenTime(Bars.OpenTimes[index]);

        double? touchLtfOpen = null;
        if (isBarClosed && !isRealtime && _mtfManager != null)
        {
            var barCloseTime = barOpenChartLocal.Add(EstimateBarPeriod());
            touchLtfOpen = _mtfManager.ResolveTouchLtfOpenForBacktestPublic(
                tfTok, barOpenChartLocal, EstimateBarPeriod(), evalChartCloseTime: barCloseTime);
        }

        return Loop6EvaluationContextFactory.Build(
            _shell!,
            index,
            tfTok,
            barOpenChartLocal,
            Bars.ClosePrices[index],
            Bars.HighPrices[index],
            Bars.LowPrices[index],
            isBarClosed,
            isRealtime,
            isNewBarOnRealtimeForming,
            m15HostInject,
            touchLtfOpenPrice: touchLtfOpen,
            alertTrackingDebug: AlertTrackingDebug,
            eventDetectionCache: _shell!.EventDetectionCache,
            pineBarIndex: pineBarIndex,
            realZoneDebug: RealZoneDebug);
    }

    // ── Chart rendering bridge ────────────────────────────────────────────────
    // Pine-exact colors:
    //   HIGH active label : #32ac45  LOW active label : #de3b22
    //   HH label          : #19a11c  LL label         : #e63419
    //   BROKEN            : gray     MAIN C           : yellow
    //   D (pending)       : orange   MAIN BROKEN      : red
    //   Connecting line   : #b8fbdb (mint-green, fully opaque)
    //   KeyBox fill HIGH  : rgb(197,89,89) @ 90% transparent  (alpha=26)
    //   KeyBox fill LOW   : rgb(72,189,78) @ 90% transparent
    //   KeyBox border HIGH: same RGB @ alpha=212 (17% transparent)
    //   KeyBox border LOW : same RGB @ alpha=184
    void FlushRenderCommands()
    {
        if (_shell == null) return;

        var cmds = _shell.Render.Commands;
        if (cmds.Count == 0) return;

        foreach (var cmd in cmds)
        {
            switch (cmd.Kind)
            {
                case DrawingCommandKind.AddLabel:
                    if (cmd.LabelKeyStr != null && cmd.Text != null
                        && !cmd.BarIndex.HasValue && !cmd.Price.HasValue)
                    {
                        if (Chart.FindObject(cmd.LabelKeyStr) is ChartText existing)
                        {
                            existing.Text = cmd.Text;
                            if (cmd.ColorArgb != 0)
                                existing.Color = ResolveColor(cmd);
                        }
                    }
                    else if (cmd.BarIndex.HasValue && cmd.Price.HasValue)
                    {
                        var name  = cmd.LabelKeyStr ?? $"sw_{cmd.BarIndex.Value}_{cmd.Text ?? "?"}";
                        var color = ResolveColor(cmd);
                        Chart.DrawText(name, cmd.Text ?? "", cmd.BarIndex.Value, cmd.Price.Value, color);
                    }
                    break;

                case DrawingCommandKind.DelLabel:
                    if (cmd.LabelKeyStr != null)
                        Chart.RemoveObject(cmd.LabelKeyStr);
                    else if (cmd.LabelKey.HasValue)
                        Chart.RemoveObject($"sw_{cmd.LabelKey.Value}");
                    break;

                case DrawingCommandKind.AddLine:
                    if (cmd.X1.HasValue && cmd.Y1.HasValue && cmd.X2.HasValue && cmd.Y2.HasValue)
                    {
                        var name  = cmd.LabelKeyStr ?? $"ln_{cmd.X1.Value}_{cmd.X2.Value}";
                        var color = cmd.ColorArgb != 0
                            ? ArgbToColor(cmd.ColorArgb)
                            : Color.FromArgb(255, 184, 251, 219);  // #b8fbdb mint-green
                        var ln = Chart.DrawTrendLine(name, cmd.X1.Value, cmd.Y1.Value,
                                                          cmd.X2.Value, cmd.Y2.Value, color);
                        if (ln != null) ln.Thickness = 1;
                    }
                    break;

                case DrawingCommandKind.DelLine:
                    if (cmd.LabelKeyStr != null)
                        Chart.RemoveObject(cmd.LabelKeyStr);
                    else if (cmd.LineKey.HasValue)
                        Chart.RemoveObject($"ln_{cmd.LineKey.Value}");
                    break;

                case DrawingCommandKind.EnqueueKeyBoxSpec:
                    if (cmd.KeyBoxSpec != null)
                    {
                        var spec = cmd.KeyBoxSpec;
                        var kbName = cmd.LabelKeyStr ?? KeyLevelVisual.ChartObjectName(spec);
                        var leftUtc  = ChartTimePolicy.ChartLocalUnspecifiedToUtcInstant(spec.LeftTimeChartLocal);
                        // Pine extend.right — cTrader: kéo Time2 vào tương lai, không chỉ bar hiện tại
                        var rightUtc = spec.ExtendRight
                            ? ExtendRightEdgeUtc()
                            : ChartTimePolicy.ChartLocalUnspecifiedToUtcInstant(spec.RightTimeChartLocal);

                        var fillArgb  = cmd.ColorArgb != 0 ? cmd.ColorArgb : PineColors.KeyHighFill;
                        var bordArgb  = cmd.BorderColorArgb != 0 ? cmd.BorderColorArgb : PineColors.KeyHighBorder;
                        var thickness = ResolveKeyBorderThickness(cmd);
                        ApplyKeyBoxRectangle(kbName, leftUtc, rightUtc, spec.Top, spec.Bottom,
                            ArgbToColor(fillArgb), ArgbToColor(bordArgb), LineStyle.Solid, thickness);
                    }
                    break;

                case DrawingCommandKind.ExtendKeyBox:
                    if (cmd.LabelKeyStr != null)
                    {
                        var ext = Chart.FindObject(cmd.LabelKeyStr) as ChartRectangle;
                        if (ext != null)
                            ext.Time2 = ExtendRightEdgeUtc();
                    }
                    break;

                case DrawingCommandKind.UpdateKeyBox:
                    if (cmd.LabelKeyStr != null)
                    {
                        var obj = Chart.FindObject(cmd.LabelKeyStr) as ChartRectangle;
                        if (obj != null)
                        {
                            var fillArgb = cmd.ColorArgb;
                            var bordArgb = cmd.BorderColorArgb != 0 ? cmd.BorderColorArgb : fillArgb;
                            var borderStyle = cmd.IsDashed ? LineStyle.DotsRare : LineStyle.Solid;
                            var thickness   = ResolveKeyBorderThickness(cmd);
                            ApplyKeyBoxStyle(obj, fillArgb, bordArgb, borderStyle, thickness);
                        }
                    }
                    break;

                case DrawingCommandKind.DelKeyBox:
                    if (cmd.LabelKeyStr != null)
                        Chart.RemoveObject(cmd.LabelKeyStr);
                    break;

                case DrawingCommandKind.StopExtendKeyBox:
                    if (cmd.LabelKeyStr != null && cmd.BarIndex.HasValue
                        && cmd.BarIndex.Value >= 0 && cmd.BarIndex.Value < Bars.Count)
                    {
                        var chartRightBar = KeyLevelVisual.ToChartRightBarIndex(cmd.BarIndex.Value);
                        if (chartRightBar >= 0 && chartRightBar < Bars.Count)
                        {
                            var obj2 = Chart.FindObject(cmd.LabelKeyStr) as ChartRectangle;
                            if (obj2 != null)
                                obj2.Time2 = BarOpenToUtc(chartRightBar);
                        }
                    }
                    break;

                case DrawingCommandKind.EnqueueObBox:
                    if (cmd.ObBoxSpec != null)
                    {
                        var ob = cmd.ObBoxSpec;
                        var obName = cmd.LabelKeyStr ?? ObDrawEngine.ChartObjectName(ob);
                        var leftUtc  = ChartTimePolicy.ChartLocalUnspecifiedToUtcInstant(ob.LeftTimeChartLocal);
                        var rightUtc = ExtendRightEdgeUtc();
                        var fill  = cmd.ColorArgb != 0 ? cmd.ColorArgb : PineColors.ObGreenFill;
                        var bord  = cmd.BorderColorArgb != 0 ? cmd.BorderColorArgb : PineColors.ObGreenBorder;
                        ApplyKeyBoxRectangle(obName, leftUtc, rightUtc, ob.Top, ob.Bottom,
                            ArgbToColor(fill), ArgbToColor(bord), LineStyle.Solid, thickness: 1);
                    }
                    break;

                case DrawingCommandKind.UpdateObBox:
                    if (cmd.LabelKeyStr != null)
                    {
                        var obObj = Chart.FindObject(cmd.LabelKeyStr) as ChartRectangle;
                        if (obObj != null)
                        {
                            var fillArgb = cmd.ColorArgb != 0 ? cmd.ColorArgb : PineColors.ObGreenFill;
                            var bordArgb = cmd.BorderColorArgb != 0 ? cmd.BorderColorArgb : PineColors.ObGreenBorder;
                            ApplyKeyBoxStyle(obObj, fillArgb, bordArgb, LineStyle.Solid, thickness: 1);
                        }
                    }
                    break;

                case DrawingCommandKind.ExtendObBox:
                    if (cmd.LabelKeyStr != null)
                    {
                        var obExt = Chart.FindObject(cmd.LabelKeyStr) as ChartRectangle;
                        if (obExt != null)
                            obExt.Time2 = ExtendRightEdgeUtc();
                    }
                    break;

                case DrawingCommandKind.StopExtendObBox:
                    if (cmd.LabelKeyStr != null && cmd.BarIndex.HasValue
                        && cmd.BarIndex.Value >= 0 && cmd.BarIndex.Value < Bars.Count)
                    {
                        var chartRightBar = KeyLevelVisual.ToChartRightBarIndex(cmd.BarIndex.Value);
                        if (chartRightBar >= 0 && chartRightBar < Bars.Count)
                        {
                            var obStop = Chart.FindObject(cmd.LabelKeyStr) as ChartRectangle;
                            if (obStop != null)
                                obStop.Time2 = BarOpenToUtc(chartRightBar);
                        }
                    }
                    break;

                case DrawingCommandKind.DelObBox:
                    if (cmd.LabelKeyStr != null)
                        Chart.RemoveObject(cmd.LabelKeyStr);
                    break;
            }
        }

        _shell.Render.ClearCommands();
    }

    /// <summary>Resolve chart text color from a DrawingCommand.
    /// Uses explicit ColorArgb if set, otherwise falls back to Pine High/Low defaults.</summary>
    static Color ResolveColor(DrawingCommand cmd)
    {
        if (cmd.ColorArgb != 0)
            return ArgbToColor(cmd.ColorArgb);
        // Fallback: derive from IsHigh
        return cmd.IsHigh == true
            ? Color.FromArgb(255, 50, 172, 69)    // #32ac45 high green
            : Color.FromArgb(255, 222, 59, 34);   // #de3b22 low red
    }

    void ApplyKeyBoxRectangle(
        string name,
        DateTime leftUtc,
        DateTime rightUtc,
        double top,
        double bottom,
        Color fill,
        Color border,
        LineStyle borderStyle,
        int thickness = 1)
    {
        var rect = Chart.DrawRectangle(name, leftUtc, top, rightUtc, bottom, border);
        if (rect == null) return;
        rect.IsFilled  = true;
        rect.Color     = fill;
        rect.Thickness = thickness;
        rect.LineStyle = borderStyle;
    }

    int ResolveKeyBorderThickness(DrawingCommand cmd)
    {
        if (!ShowKeyBorder) return 0;
        if (cmd.IsDashed)
            return Math.Max(KeyBorderWidth, KeyLevelVisual.BrokenBorderThickness);
        return cmd.BorderThickness > 0 ? cmd.BorderThickness : KeyBorderWidth;
    }

    void ApplyKeyBoxStyle(ChartRectangle rect, uint fillArgb, uint borderArgb, LineStyle borderStyle, int thickness = 1)
    {
        var updated = Chart.DrawRectangle(rect.Name, rect.Time1, rect.Y1, rect.Time2, rect.Y2, ArgbToColor(borderArgb));
        if (updated == null) return;
        updated.IsFilled  = true;
        updated.Color     = ArgbToColor(fillArgb);
        updated.Thickness = thickness;
        updated.LineStyle = borderStyle;
    }

    /// <summary>Pine <c>extend.right</c>: project right edge beyond last bar into the future.</summary>
    DateTime ExtendRightEdgeUtc()
    {
        if (Bars.Count == 0)
            return DateTime.UtcNow;

        var lastLocal = ChartTimeFromBars.NormalizeBarOpenTime(Bars.OpenTimes[Bars.Count - 1]);
        var period    = EstimateBarPeriod();
        var ahead     = Math.Max(ExtendRightBarsAhead, 100);
        var edgeLocal = lastLocal.Add(period * ahead);
        return ChartTimePolicy.ChartLocalUnspecifiedToUtcInstant(edgeLocal);
    }

    DateTime BarOpenToUtc(int barIndex)
    {
        var local = ChartTimeFromBars.NormalizeBarOpenTime(Bars.OpenTimes[barIndex]);
        return ChartTimePolicy.ChartLocalUnspecifiedToUtcInstant(local);
    }

    /// <summary>
    /// True when the last bar is genuinely live-forming right now (live real-time trading).
    /// Compares server UTC time with the expected bar close to distinguish live from
    /// visual backtest replay where RunningMode is also RealTime but bars are historical.
    /// </summary>
    bool IsLastBarActuallyLiveForming()
    {
        if (Bars.Count == 0) return false;
        // In live real-time, Server.TimeInUtc is within the current bar window.
        // In visual backtest replay, Server.TimeInUtc is the current wall-clock year
        // while bar open times are historical, so expectedClose is far in the past.
        var expectedClose = Bars.OpenTimes[Bars.Count - 1].Add(EstimateBarPeriod());
        return Server.TimeInUtc < expectedClose.AddSeconds(30);
    }

    TimeSpan EstimateBarPeriod()
    {
        if (Bars.Count >= 2)
        {
            var a = ChartTimeFromBars.NormalizeBarOpenTime(Bars.OpenTimes[Bars.Count - 1]);
            var b = ChartTimeFromBars.NormalizeBarOpenTime(Bars.OpenTimes[Bars.Count - 2]);
            var d = a - b;
            if (d > TimeSpan.Zero)
                return d;
        }

        if (Bars.TimeFrame.Equals(TimeFrame.Minute))
            return TimeSpan.FromMinutes(1);
        if (Bars.TimeFrame.Equals(TimeFrame.Minute5))
            return TimeSpan.FromMinutes(5);
        if (Bars.TimeFrame.Equals(TimeFrame.Minute15))
            return TimeSpan.FromMinutes(15);
        if (Bars.TimeFrame.Equals(TimeFrame.Hour))
            return TimeSpan.FromHours(1);
        if (Bars.TimeFrame.Equals(TimeFrame.Hour4))
            return TimeSpan.FromHours(4);
        if (Bars.TimeFrame.Equals(TimeFrame.Daily))
            return TimeSpan.FromDays(1);
        return TimeSpan.FromHours(1);
    }

    /// <summary>Convert ARGB uint (0xAARRGGBB) to cTrader Color.</summary>
    static Color ArgbToColor(uint argb)
    {
        var a = (byte)((argb >> 24) & 0xFF);
        var r = (byte)((argb >> 16) & 0xFF);
        var g = (byte)((argb >>  8) & 0xFF);
        var b = (byte)(argb         & 0xFF);
        return Color.FromArgb(a == 0 ? 255 : a, r, g, b);
    }

    protected override void OnDestroy()
    {
        ClearCompoundPanelDrawings();
        _realZoneDebugPanel.Clear(Chart);
        _htfTouchTablePanel.Clear(Chart);
        _shell?.OnStopOrReload();
        base.OnDestroy();
    }
}

static class DuplicateKeyLoop6
{
    public static DuplicateKey From(in AlertEvaluationContext ctx, in AlertDefinition def) =>
        new(
            ctx.Symbol,
            ctx.ChartTimeframeToken,
            def.Title,
            def.PineConditionSymbol,
            ctx.SourceBarOpenTimeChartLocal,
            ctx.SourceBarIndex,
            ctx.ChartTimeframeToken);
}

struct Loop6ThrottleState
{
    DateTime _lastOpen;
    DateTime _lastUtc;
    bool _seeded;

    public bool ShouldAppend(DateTime barOpen, int minSecondsBetween, DateTime utcNow)
    {
        if (!_seeded || barOpen != _lastOpen)
        {
            _lastOpen = barOpen;
            _lastUtc = utcNow;
            _seeded = true;
            return true;
        }

        if ((utcNow - _lastUtc).TotalSeconds >= minSecondsBetween)
        {
            _lastUtc = utcNow;
            return true;
        }

        return false;
    }
}
