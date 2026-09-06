using System;
using System.Collections.Generic;
using cAlgo.API;
using TradeAlert.Core.Models.Alerts;
using TradeAlert.Indicator;
using TradeAlert.Indicator.Host;

namespace TradeAlert.Robot;

/// <summary>
/// Multi-symbol LOOP6 scanner cBot.
/// Scans up to N symbols for R1–R6 compound signal fires and sends email alerts.
/// NEVER executes trades — <see cref="AutoTrade"/> is hard-guarded to <c>false</c>.
/// </summary>
[Robot(AccessRights = AccessRights.None, TimeZone = TimeZones.UTC)]
public sealed class Loop6MultiSymbolEmailScanner : cAlgo.API.Robot
{
    // ── GROUP 01: Scanner ───────────────────────────────────────────────────
    [Parameter("Symbols (comma-separated)", Group = "01. Scanner",
        DefaultValue = "XAUUSD,BTCUSD,ETHUSD,EURUSD,AUDUSD,GBPUSD,NZDUSD,AUDJPY,EURJPY,NZDJPY,GBPJPY,USDJPY,EURCAD,AUDCAD,USDCAD,NZDCAD,GBPCAD,AUDCHF,NZDCHF,GBPCHF,USDCHF,GBPNZD,EURNZD,GBPAUD,AUDNZD")]
    public string SymbolsCsv { get; set; } = "";

    [Parameter("Scan Timeframe", Group = "01. Scanner", DefaultValue = "Minute15")]
    public TimeFrame ScanTimeFrame { get; set; } = TimeFrame.Minute15;

    [Parameter("Check Every (seconds)", Group = "01. Scanner", DefaultValue = 1, MinValue = 1, MaxValue = 60)]
    public int CheckEverySeconds { get; set; }

    [Parameter("Alert Mode", Group = "01. Scanner", DefaultValue = AlertMode.OncePerEventPerBar)]
    public AlertMode AlertMode { get; set; }

    [Parameter("Warmup Bars (history)", Group = "01. Scanner", DefaultValue = 3000, MinValue = 500, MaxValue = 10000)]
    public int WarmupBars { get; set; }

    [Parameter("Enable Log", Group = "01. Scanner", DefaultValue = true)]
    public bool EnableLog { get; set; }

    // ── GROUP 02: Email Alerts ──────────────────────────────────────────────
    [Parameter("Send Email", Group = "02. Email Alerts", DefaultValue = true)]
    public bool SendEmail { get; set; }

    [Parameter("Email From", Group = "02. Email Alerts", DefaultValue = "")]
    public string EmailFrom { get; set; } = "";

    [Parameter("Email To", Group = "02. Email Alerts", DefaultValue = "")]
    public string EmailTo { get; set; } = "";

    [Parameter("Email Subject Prefix", Group = "02. Email Alerts", DefaultValue = "[LOOP6]")]
    public string EmailSubjectPrefix { get; set; } = "[LOOP6]";

    [Parameter("Email Min Seconds Per Key (EveryTime mode)", Group = "02. Email Alerts", DefaultValue = 30, MinValue = 0, MaxValue = 86400)]
    public int EmailMinSecondsPerKey { get; set; }

    [Parameter("Send Test Email On Start", Group = "02. Email Alerts", DefaultValue = false)]
    public bool SendTestEmailOnStart { get; set; }

    // ── GROUP 03: Signal Control ────────────────────────────────────────────
    [Parameter("Alert on BUY signals", Group = "03. Signal Control", DefaultValue = true)]
    public bool AlertOnBuy { get; set; }

    [Parameter("Alert on SELL signals", Group = "03. Signal Control", DefaultValue = true)]
    public bool AlertOnSell { get; set; }

    [Parameter("Auto Trade (v1 disabled — always false)", Group = "03. Signal Control", DefaultValue = false)]
    public bool AutoTrade { get; set; }

    // ── GROUP 04: Compound Rules ────────────────────────────────────────────
    [Parameter("Compound Rule 1 (R1 SELL M5)", Group = "04. Compound Rules", DefaultValue = CompoundAlertPresets.R1SellM5)]
    public string CompoundRule1 { get; set; } = CompoundAlertPresets.R1SellM5;

    [Parameter("Compound Rule 2 (R2 BUY M5)", Group = "04. Compound Rules", DefaultValue = CompoundAlertPresets.R2BuyM5)]
    public string CompoundRule2 { get; set; } = CompoundAlertPresets.R2BuyM5;

    [Parameter("Compound Rule 3 (R3 BUY HL M15)", Group = "04. Compound Rules", DefaultValue = CompoundAlertPresets.R3BuyHlM15)]
    public string CompoundRule3 { get; set; } = CompoundAlertPresets.R3BuyHlM15;

    [Parameter("Compound Rule 4 (R4 SELL HL M15)", Group = "04. Compound Rules", DefaultValue = CompoundAlertPresets.R4SellHlM15)]
    public string CompoundRule4 { get; set; } = CompoundAlertPresets.R4SellHlM15;

    [Parameter("Compound Rule 5 (R5 BUY NG M15)", Group = "04. Compound Rules", DefaultValue = CompoundAlertPresets.R5BuyNgM15)]
    public string CompoundRule5 { get; set; } = CompoundAlertPresets.R5BuyNgM15;

    [Parameter("Compound Rule 6 (R6 SELL NG M15)", Group = "04. Compound Rules", DefaultValue = CompoundAlertPresets.R6SellNgM15)]
    public string CompoundRule6 { get; set; } = CompoundAlertPresets.R6SellNgM15;

    [Parameter("EVENT valid window (bars of that TF)", Group = "04. Compound Rules", DefaultValue = 3, MinValue = 1, MaxValue = 50)]
    public int CompoundEventValidBars { get; set; }

    [Parameter("Compound sync mode", Group = "04. Compound Rules", DefaultValue = CompoundEvalSyncMode.PineEventWindow)]
    public CompoundEvalSyncMode CompoundSyncMode { get; set; }

    [Parameter("Print compound fires to log", Group = "04. Compound Rules", DefaultValue = true)]
    public bool PrintCompoundFires { get; set; }

    [Parameter("Inject M15 Close Edge Signal", Group = "04. Compound Rules", DefaultValue = false)]
    public bool InjectM15CloseEdgeSignal { get; set; }

    // ── GROUP 05: Core Settings ─────────────────────────────────────────────
    [Parameter("Lookback bars for pivots", Group = "05. Core Settings", DefaultValue = 200, MinValue = 1)]
    public int LookbackBars { get; set; }

    [Parameter("Enable Keylevel Box", Group = "05. Core Settings", DefaultValue = true)]
    public bool EnableKeylevel { get; set; }

    [Parameter("Lock Swing Count", Group = "05. Core Settings", DefaultValue = 50, MinValue = 1)]
    public int LockSwingCount { get; set; }

    // ── GROUP 06: Filters ───────────────────────────────────────────────────
    [Parameter("OB Validation (Auto LTF/ATR)", Group = "06. Filters", DefaultValue = true)]
    public bool UseObConfirm { get; set; }

    [Parameter("OB ATR Multiplier", Group = "06. Filters", DefaultValue = 1.5, MinValue = 0.5, MaxValue = 5.0, Step = 0.1)]
    public double ObAtrMultiplier { get; set; }

    [Parameter("Filter noise wick (wick > avg range)", Group = "06. Filters", DefaultValue = true)]
    public bool UseWickNoiseFilter { get; set; }

    [Parameter("Avg range length", Group = "06. Filters", DefaultValue = 10, MinValue = 1)]
    public int WickAvgLen { get; set; }

    [Parameter("Confirm wick noise via LTF", Group = "06. Filters", DefaultValue = true)]
    public bool UseLtfWickConfirm { get; set; }

    [Parameter("Min penetration ratio", Group = "06. Filters", DefaultValue = 0.5, MinValue = 0.05, MaxValue = 1, Step = 0.05)]
    public double LtfConfirmRatio { get; set; }

    [Parameter("Use ATR rule for keylevel", Group = "06. Filters", DefaultValue = true)]
    public bool KeylevelUseAtrRule { get; set; }

    [Parameter("Pullback filter (engine A1/A2/A3)", Group = "06. Filters", DefaultValue = true)]
    public bool UseNewPullbackFilter { get; set; }

    [Parameter("Bx: Max bars A to B", Group = "06. Filters", DefaultValue = 6, MinValue = 2, MaxValue = 15)]
    public int BMaxLagBars { get; set; }

    [Parameter("CDx: Max bars A to C/D", Group = "06. Filters", DefaultValue = 7, MinValue = 3, MaxValue = 20)]
    public int CdMaxLagBars { get; set; }

    [Parameter("Strong body ATR mult", Group = "06. Filters", DefaultValue = 0.6, MinValue = 0.1, MaxValue = 2, Step = 0.1)]
    public double StrongAtrMult { get; set; }

    [Parameter("Medium body ATR mult", Group = "06. Filters", DefaultValue = 0.3, MinValue = 0.1, MaxValue = 2, Step = 0.1)]
    public double MediumAtrMult { get; set; }

    [Parameter("Multi-A: pick price extreme", Group = "06. Filters", DefaultValue = true)]
    public bool UseExtremePickRule { get; set; }

    [Parameter("Merge gap candles (X+Y to XY)", Group = "06. Filters", DefaultValue = true)]
    public bool UseGapMerge { get; set; }

    [Parameter("Gap threshold (ATR mult)", Group = "06. Filters", DefaultValue = 0.5, MinValue = 0.1, Step = 0.1)]
    public double GapFilterAtr { get; set; }

    [Parameter("Micro swing rule", Group = "06. Filters", DefaultValue = true)]
    public bool UseMicroSwingRule { get; set; }

    [Parameter("A4: Doji Force B + Strong C", Group = "06. Filters", DefaultValue = true)]
    public bool UseDojiForceBC { get; set; }

    [Parameter("A4: Doji body max ratio", Group = "06. Filters", DefaultValue = 0.25, MinValue = 0.05, MaxValue = 0.5, Step = 0.05)]
    public double DojiBodyMaxRatio { get; set; }

    [Parameter("A4: Doji force wick min", Group = "06. Filters", DefaultValue = 0.55, MinValue = 0.3, MaxValue = 0.9, Step = 0.05)]
    public double DojiForceWickRatioMin { get; set; }

    [Parameter("A4: Doji body zone max", Group = "06. Filters", DefaultValue = 0.45, MinValue = 0.2, MaxValue = 0.8, Step = 0.05)]
    public double DojiForceBodyZoneMax { get; set; }

    [Parameter("A4: Wick dominance ratio", Group = "06. Filters", DefaultValue = 1.5, MinValue = 1, MaxValue = 5, Step = 0.25)]
    public double DojiWickDomRatio { get; set; }

    // ── GROUP 07: Break Rules ───────────────────────────────────────────────
    [Parameter("R3: max k (E in bar B+1..B+k)", Group = "07. Break Rules", DefaultValue = 3, MinValue = 1, MaxValue = 6)]
    public int BreakR3MaxK { get; set; }

    // ── GROUP 08: Order Block Settings ──────────────────────────────────────
    [Parameter("LTF Buffer Size", Group = "08. OB Settings", DefaultValue = 50, MinValue = 10, MaxValue = 100)]
    public int LtfBufferSize { get; set; }

    [Parameter("OB scan bars", Group = "08. OB Settings", DefaultValue = 50, MinValue = 5, MaxValue = 100)]
    public int ObScanBars { get; set; }

    [Parameter("Max OBs (all types)", Group = "08. OB Settings", DefaultValue = 20, MinValue = 10, MaxValue = 100)]
    public int MaxObs { get; set; }

    [Parameter("Min OB Body Ratio", Group = "08. OB Settings", DefaultValue = 0.35, MinValue = 0, MaxValue = 1, Step = 0.05)]
    public double MinObBodyRatio { get; set; }

    [Parameter("Max Doji Body Ratio", Group = "08. OB Settings", DefaultValue = 0.15, MinValue = 0, MaxValue = 0.3, Step = 0.05)]
    public double MaxDojiBodyRatio { get; set; }

    // ── GROUP 09: Keylevel Settings ─────────────────────────────────────────
    [Parameter("Reference candle lookback", Group = "09. Keylevel Settings", DefaultValue = 2, MinValue = 1)]
    public int KeylevelLookback { get; set; }

    [Parameter("Average body length", Group = "09. Keylevel Settings", DefaultValue = 10, MinValue = 1)]
    public int KeylevelAvgBodyLen { get; set; }

    [Parameter("Min body multiplier", Group = "09. Keylevel Settings", DefaultValue = 1.5, MinValue = 0.1, MaxValue = 10, Step = 0.1)]
    public double KeylevelMinBodyMult { get; set; }

    [Parameter("ATR multiplier", Group = "09. Keylevel Settings", DefaultValue = 0.2, MinValue = 0.1, MaxValue = 2, Step = 0.1)]
    public double KeylevelAtrMult { get; set; }

    [Parameter("ATR length", Group = "09. Keylevel Settings", DefaultValue = 14, MinValue = 1)]
    public int KeylevelAtrLen { get; set; }

    [Parameter("Max overlapping keylevels kept", Group = "09. Keylevel Settings", DefaultValue = 2, MinValue = 1)]
    public int MaxKeylevelKeep { get; set; }

    // ── Runtime ─────────────────────────────────────────────────────────────
    readonly List<PerSymbolSignalHost> _hosts  = new();
    EmailSignalNotifier                _notifier = null!;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    protected override void OnStart()
    {
        Print("[Scanner] OnStart begin");
        try
        {
            OnStartCore();
        }
        catch (Exception ex)
        {
            Print($"[Scanner] FATAL OnStart exception: {ex.GetType().Name}: {ex.Message}");
            Print($"[Scanner] StackTrace: {ex.StackTrace}");
            Stop();
        }
    }

    void OnStartCore()
    {
        _notifier = new EmailSignalNotifier
        {
            Mode               = AlertMode,
            EmailFrom          = EmailFrom,
            EmailTo            = EmailTo,
            EmailSubjectPrefix = EmailSubjectPrefix,
            EmailMinSeconds    = EmailMinSecondsPerKey,
        };

        Print("[Scanner] Notifier created");
        var snapshot = BuildSignalSnapshot();
        Print("[Scanner] Snapshot built");
        var symbols  = ParseSymbolsCsv();

        Log($"[Scanner] Starting: {symbols.Count} symbols | TF={ScanTimeFrame} | mode={AlertMode}");

        foreach (var name in symbols)
        {
            try
            {
                var sym   = Symbols.GetSymbol(name);
                if (sym == null)
                {
                    Log($"[Scanner] {name}: symbol not found — skipped");
                    continue;
                }

                var bars = MarketData.GetBars(ScanTimeFrame, name);
                if (bars == null || bars.Count == 0)
                {
                    Log($"[Scanner] {name}: no bars for {ScanTimeFrame} — skipped");
                    continue;
                }

                var host = new PerSymbolSignalHost(
                    symbolName:         name,
                    tickSize:           sym.TickSize,
                    scanTimeFrame:      ScanTimeFrame,
                    chartBars:          bars,
                    marketData:         MarketData,
                    snapshot:           in snapshot,
                    r1:                 CompoundRule1,
                    r2:                 CompoundRule2,
                    r3:                 CompoundRule3,
                    r4:                 CompoundRule4,
                    r5:                 CompoundAlertPresets.NormalizeNgEventTrigger(CompoundRule5),
                    r6:                 CompoundAlertPresets.NormalizeNgEventTrigger(CompoundRule6),
                    syncMode:           CompoundSyncMode,
                    eventValidBars:     CompoundEventValidBars,
                    injectM15CloseEdge: InjectM15CloseEdgeSignal,
                    warmupBars:         WarmupBars,
                    log:                EnableLog ? (Action<string>)Print : null);

                host.Initialize();
                _hosts.Add(host);
            }
            catch (Exception ex)
            {
                Log($"[Scanner] {name}: init error — {ex.Message}");
            }
        }

        Log($"[Scanner] Init done: {_hosts.Count} hosts ready");

        if (SendTestEmailOnStart && SendEmail)
        {
            try
            {
                Notifications.SendEmail(
                    EmailFrom,
                    EmailTo,
                    $"{EmailSubjectPrefix} TEST Scanner Started",
                    $"Loop6 scanner test email\nHosts ready: {_hosts.Count}\nSymbols: {SymbolsCsv}\nScanTimeFrame: {ScanTimeFrame}\nServerTime UTC: {Server.TimeInUtc:O}");
                Log("[Email] Test email sent");
            }
            catch (Exception ex)
            {
                Log($"[Email] Test email failed: {ex.Message}");
            }
        }

        Timer.Start(CheckEverySeconds);
        Print("[Scanner] Timer started — scanner running");
    }

    protected override void OnTimer()
    {
        var now = Server.TimeInUtc;

        foreach (var host in _hosts)
        {
            IReadOnlyList<CompoundFireEvent> events;
            try
            {
                events = host.Tick();
            }
            catch (Exception ex)
            {
                Log($"[Scanner] {host.SymbolName}: tick error — {ex.Message}");
                continue;
            }

            foreach (var ev in events)
            {
                // Direction filter
                if (ev.Direction == SignalDirection.Buy  && !AlertOnBuy)  continue;
                if (ev.Direction == SignalDirection.Sell && !AlertOnSell) continue;

                var dirLabel = ev.Direction switch
                {
                    SignalDirection.Buy  => "BUY",
                    SignalDirection.Sell => "SELL",
                    _                   => ev.Direction.ToString().ToUpper(),
                };

                if (PrintCompoundFires)
                    Log($"[Scanner] {host.SymbolName} {dirLabel} R{ev.SlotIndex + 1} [{ev.RuleName}] bar={ev.ChartBarIndex} time={ev.BarOpenTime:s} price={ev.Price}");

                if (!SendEmail)
                    continue;

                var payload = _notifier.Evaluate(host.SymbolName, in ev, now);
                if (payload == null)
                    continue;

                if (string.IsNullOrWhiteSpace(payload.Value.To))
                {
                    Log($"[Scanner] Email skipped — EmailTo is empty");
                    continue;
                }

                try
                {
                    Notifications.SendEmail(
                        payload.Value.From,
                        payload.Value.To,
                        payload.Value.Subject,
                        payload.Value.Body);
                }
                catch (Exception ex)
                {
                    Log($"[Scanner] Email error ({host.SymbolName}): {ex.Message}");
                }
            }

            // AutoTrade guard — v1 always no-op
            PlaceOrderPlaceholder(host.SymbolName);
        }
    }

    protected override void OnStop()
    {
        Timer.Stop();
        foreach (var h in _hosts)
        {
            try { h.Dispose(); }
            catch { /* swallow on stop */ }
        }
        _hosts.Clear();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    void Log(string msg)
    {
        if (EnableLog)
            Print(msg);
    }

    List<string> ParseSymbolsCsv()
    {
        var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        if (string.IsNullOrWhiteSpace(SymbolsCsv))
            return result;

        foreach (var raw in SymbolsCsv.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var name = raw.Trim();
            if (!string.IsNullOrEmpty(name) && seen.Add(name))
                result.Add(name);
        }

        return result;
    }

    Loop6HostParameterSnapshot BuildSignalSnapshot() => FillStyleDefaults(new Loop6HostParameterSnapshot
    {
        // ── Core ──────────────────────────────────────────────────────────────
        LookbackBars         = LookbackBars,
        EnableKeylevel       = EnableKeylevel,
        LockSwingCount       = LockSwingCount,

        // ── Filters ───────────────────────────────────────────────────────────
        UseObConfirm         = UseObConfirm,
        ObAtrMultiplier      = ObAtrMultiplier,
        UseWickNoiseFilter   = UseWickNoiseFilter,
        WickAvgLen           = WickAvgLen,
        UseLtfWickConfirm    = UseLtfWickConfirm,
        LtfConfirmRatio      = LtfConfirmRatio,
        KeylevelUseAtrRule   = KeylevelUseAtrRule,
        UseNewPullbackFilter = UseNewPullbackFilter,
        BMaxLagBars          = BMaxLagBars,
        CdMaxLagBars         = CdMaxLagBars,
        StrongAtrMult        = StrongAtrMult,
        MediumAtrMult        = MediumAtrMult,
        UseExtremePickRule   = UseExtremePickRule,
        UseGapMerge          = UseGapMerge,
        GapFilterAtr         = GapFilterAtr,
        UseMicroSwingRule    = UseMicroSwingRule,
        UseDojiForceBC       = UseDojiForceBC,
        DojiBodyMaxRatio     = DojiBodyMaxRatio,
        DojiForceWickRatioMin = DojiForceWickRatioMin,
        DojiForceBodyZoneMax = DojiForceBodyZoneMax,
        DojiWickDomRatio     = DojiWickDomRatio,

        // ── Break Rules ───────────────────────────────────────────────────────
        BreakR3MaxK          = BreakR3MaxK,

        // ── OB Settings ───────────────────────────────────────────────────────
        LtfBufferSize        = LtfBufferSize,
        ObScanBars           = ObScanBars,
        MaxObs               = MaxObs,
        MinObBodyRatio       = MinObBodyRatio,
        MaxDojiBodyRatio     = MaxDojiBodyRatio,

        // ── Keylevel Settings ─────────────────────────────────────────────────
        KeylevelLookback     = KeylevelLookback,
        KeylevelAvgBodyLen   = KeylevelAvgBodyLen,
        KeylevelMinBodyMult  = KeylevelMinBodyMult,
        KeylevelAtrMult      = KeylevelAtrMult,
        KeylevelAtrLen       = KeylevelAtrLen,
        MaxKeylevelKeep      = MaxKeylevelKeep,
    });

    /// <summary>
    /// Fill visual-only fields with their indicator defaults.
    /// These fields are NOT exposed as [Parameter] in the cBot — they only affect rendering
    /// in the indicator and style palettes that the headless scanner never draws.
    /// </summary>
    static Loop6HostParameterSnapshot FillStyleDefaults(Loop6HostParameterSnapshot s) => s with
    {
        ShowObBox          = true,
        HideObLoseZin      = true,
        HideObZin          = false,
        ShowFakeSwings     = false,
        ShowLockedSwings   = false,
        ShowActiveSwings   = false,
        ShowSwingPushDebug = false,
        ObBoxOpacity       = 30,
        ShowKeyBorder      = true,
        KeyBorderWidth     = 1,
        InvertBrokenKeyColor = true,
        KeyOpacityActive   = 50,
        KeyOpacityBroken   = 30,
        LabelOpacity       = 50,
        // Label ARGB colors (same defaults as indicator)
        HighLabelArgb    = 0xFF008000u,
        LowLabelArgb     = 0xFFFF4500u,
        HhLabelArgb      = 0xFF008000u,
        LlLabelArgb      = 0xFFFF0000u,
        MainLabelArgb    = 0xFFFFFF00u,
        FakeLabelArgb    = 0xFF0000FFu,
        BrokenLabelArgb  = 0xFF808080u,
        // Key box colors
        HighKeyBoxColor  = Color.FromArgb(205, 92, 92),   // IndianRed
        LowKeyBoxColor   = Color.FromArgb(50, 205, 50),   // LimeGreen
        MainCKeyBoxColor = Color.FromArgb(255, 215, 0),   // Gold
    };

    /// <summary>
    /// Auto-trade placeholder. In v1 this method is always a no-op.
    /// The AutoTrade [Parameter] defaults to false and is never set to true by the cBot itself.
    /// </summary>
    void PlaceOrderPlaceholder(string symbolName)
    {
        if (!AutoTrade) return;
        // TODO (v2): ExecuteMarketOrder + SL/TP — not implemented in v1
    }
}
