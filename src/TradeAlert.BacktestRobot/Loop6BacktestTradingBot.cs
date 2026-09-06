using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.Alerts;
using TradeAlert.Core.Series;
using TradeAlert.Indicator;
using TradeAlert.Indicator.Host;
using TradeAlert.BacktestRobot.Execution;
using TradeAlert.BacktestRobot.Execution.Kl;
using TradeAlert.BacktestRobot.Execution.Analytics;
using TradeAlert.BacktestRobot.Execution.News;
using TradeAlert.BacktestRobot.Execution.FridayFlat;

namespace TradeAlert.BacktestRobot;

/// <summary>
/// LOOP6 single-symbol backtest trading cBot.
///
/// Reuses <see cref="PerSymbolSignalHost"/> to produce the SAME compound R1-R6 fires as the email
/// scanner, then turns each fire into a pending LIMIT order with SL/TP using swing-B keylevel / OB
/// geometry (read-only) and KlEntryLot FTMO sizing. Requires an M15 chart (Rule 1 reads the M15
/// active swing from the chart-TF engine).
///
/// Engine logic is untouched; this bot only consumes state read-only via <see cref="PerSymbolSignalHost.State"/>.
/// </summary>
// AccessRights.FullAccess: required for (a) local file I/O (news cache JSON/CSV under MyDocuments)
// and (b) optional HTTP to TradingEconomics API when NewsApiKey is set and NewsBacktestMode != CacheOnly.
// The default configuration (CacheOnly, empty key) never makes network calls.
[Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.FullAccess)]
public sealed class Loop6BacktestTradingBot : cAlgo.API.Robot
{
    const string LabelPrefix = "L6BT|";

    // ── GROUP 01: Backtest ──────────────────────────────────────────────────
    [Parameter("Enable Trading (false = dry-run log only)", Group = "01. Backtest", DefaultValue = true)]
    public bool EnableTrading { get; set; }

    [Parameter("Warmup Bars (history)", Group = "01. Backtest", DefaultValue = 3000, MinValue = 500, MaxValue = 10000)]
    public int WarmupBars { get; set; }

    [Parameter("Enable Log", Group = "01. Backtest", DefaultValue = true)]
    public bool EnableLog { get; set; }

    [Parameter("Position SL diagnostic log (OPEN/PRICE/CLOSE)", Group = "01. Backtest", DefaultValue = false)]
    public bool EnablePositionSlDiagnosticLog { get; set; }

    [Parameter("SL diagnostic near zone (pips)", Group = "01. Backtest", DefaultValue = 3, MinValue = 0.1)]
    public double PositionSlDiagnosticNearPips { get; set; } = 3;

    // ── GROUP 02: Signal ────────────────────────────────────────────────────
    [Parameter("Trade BUY signals", Group = "02. Signal", DefaultValue = true)]
    public bool AlertOnBuy { get; set; }

    [Parameter("Trade SELL signals", Group = "02. Signal", DefaultValue = true)]
    public bool AlertOnSell { get; set; }

    [Parameter("Print compound fires", Group = "02. Signal", DefaultValue = true)]
    public bool PrintCompoundFires { get; set; }

    [Parameter("EVENT valid window (bars)", Group = "02. Signal", DefaultValue = 3, MinValue = 1, MaxValue = 50)]
    public int CompoundEventValidBars { get; set; }

    [Parameter("Compound sync mode", Group = "02. Signal", DefaultValue = CompoundEvalSyncMode.PineEventWindow)]
    public CompoundEvalSyncMode CompoundSyncMode { get; set; }

    [Parameter("Compound window mode", Group = "02. Signal", DefaultValue = CompoundWindowMode.SourceOneShot)]
    public CompoundWindowMode CompoundWindowMode { get; set; }

    [Parameter("Inject M15 Close Edge Signal", Group = "02. Signal", DefaultValue = false)]
    public bool InjectM15CloseEdgeSignal { get; set; }

    [Parameter("Enable compound stage diagnostic log (StageDiag)", Group = "02. Signal", DefaultValue = false)]
    public bool EnableCompoundStageDiagLog { get; set; }

    [Parameter("Enable compound audit log (COMPOUND_* lines)", Group = "02. Signal", DefaultValue = true)]
    public bool EnableCompoundAuditLog { get; set; }

    [Parameter("Enable compound fire debug log (FireDbg)", Group = "02. Signal", DefaultValue = false)]
    public bool EnableCompoundFireDebugLog { get; set; }

    [Parameter("Chart labels on FIRE / WAIT-C (debug)", Group = "02. Signal", DefaultValue = false)]
    public bool EnableCompoundFireChartLabels { get; set; }

    [Parameter("Visual replay parity (intra-bar)", Group = "02. Signal", DefaultValue = true)]
    public bool UseVisualReplayParity { get; set; }

    [Parameter("Visual replay use M5 bars (false = M1)", Group = "02. Signal", DefaultValue = false)]
    public bool VisualReplayUseM5Bars { get; set; }

    // ── GROUP 03: Compound Rules ────────────────────────────────────────────
    [Parameter("Compound Rule 1 (R1 SELL M5)", Group = "03. Compound Rules", DefaultValue = CompoundAlertPresets.R1SellM5)]
    public string CompoundRule1 { get; set; } = CompoundAlertPresets.R1SellM5;

    [Parameter("Compound Rule 2 (R2 BUY M5)", Group = "03. Compound Rules", DefaultValue = CompoundAlertPresets.R2BuyM5)]
    public string CompoundRule2 { get; set; } = CompoundAlertPresets.R2BuyM5;

    [Parameter("Enable Compound Rule 1 (R1 SELL M5)", Group = "03. Compound Rules", DefaultValue = true)]
    public bool EnableCompoundRule1 { get; set; } = true;

    [Parameter("Enable Compound Rule 2 (R2 BUY M5)", Group = "03. Compound Rules", DefaultValue = true)]
    public bool EnableCompoundRule2 { get; set; } = true;

    [Parameter("Compound Rule 3 (R3 BUY HL M15)", Group = "03. Compound Rules", DefaultValue = CompoundAlertPresets.R3BuyHlM15)]
    public string CompoundRule3 { get; set; } = CompoundAlertPresets.R3BuyHlM15;

    [Parameter("Enable Compound Rule 3 (R3 BUY HL M15)", Group = "03. Compound Rules", DefaultValue = true)]
    public bool EnableCompoundRule3 { get; set; } = true;

    [Parameter("Compound Rule 4 (R4 SELL HL M15)", Group = "03. Compound Rules", DefaultValue = CompoundAlertPresets.R4SellHlM15)]
    public string CompoundRule4 { get; set; } = CompoundAlertPresets.R4SellHlM15;

    [Parameter("Enable Compound Rule 4 (R4 SELL HL M15)", Group = "03. Compound Rules", DefaultValue = true)]
    public bool EnableCompoundRule4 { get; set; } = true;

    [Parameter("Compound Rule 5 (R5 BUY NG M15)", Group = "03. Compound Rules", DefaultValue = CompoundAlertPresets.R5BuyNgM15)]
    public string CompoundRule5 { get; set; } = CompoundAlertPresets.R5BuyNgM15;

    [Parameter("Enable Compound Rule 5 (R5 BUY NG M15)", Group = "03. Compound Rules", DefaultValue = true)]
    public bool EnableCompoundRule5 { get; set; } = true;

    [Parameter("Compound Rule 6 (R6 SELL NG M15)", Group = "03. Compound Rules", DefaultValue = CompoundAlertPresets.R6SellNgM15)]
    public string CompoundRule6 { get; set; } = CompoundAlertPresets.R6SellNgM15;

    [Parameter("Enable Compound Rule 6 (R6 SELL NG M15)", Group = "03. Compound Rules", DefaultValue = true)]
    public bool EnableCompoundRule6 { get; set; } = true;

    [Parameter("Prefer higher-tier rules (R3/R4 > R5/R6 > R1/R2, same bar)", Group = "03. Compound Rules", DefaultValue = true)]
    public bool PreferM15CompoundRules { get; set; } = true;

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

    // ── GROUP 10: Trade / Risk (KL FTMO) ────────────────────────────────────
    [Parameter("FTMO Account Balance", Group = "10. Trade/Risk", DefaultValue = 20000, MinValue = 0)]
    public double AccountBalanceFtmo { get; set; }

    [Parameter("Risk %", Group = "10. Trade/Risk", DefaultValue = 3.0, MinValue = 0.01, MaxValue = 100, Step = 0.01)]
    public double RiskPercent { get; set; }

    [Parameter("Lot rounding precision", Group = "10. Trade/Risk", DefaultValue = 1000, MinValue = 1)]
    public int RoundPrecision { get; set; }

    [Parameter("Reward:Risk (TP)", Group = "10. Trade/Risk", DefaultValue = 1.25, MinValue = 0.1, MaxValue = 20, Step = 0.1)]
    public double RewardRisk { get; set; } = 1.25;

    [Parameter("SL width multiplier", Group = "10. Trade/Risk", DefaultValue = 2.0, MinValue = 0.1, MaxValue = 20, Step = 0.1)]
    public double SlWidthMult { get; set; }

    [Parameter("SL from entry (else swing-X edge)", Group = "10. Trade/Risk", DefaultValue = false)]
    public bool SlFromEntry { get; set; }

    [Parameter("R1/R2: M5 swing-B fallback when M15 has no B", Group = "10. Trade/Risk", DefaultValue = true)]
    public bool EnableM5SwingBFallback { get; set; } = true;

    // ── GROUP 10b: Sizing override (blank/0 = auto-detect từ tên symbol) ────
    [Parameter("Override asset type (Auto = detect)", Group = "10b. Sizing Override", DefaultValue = KlAssetType.Auto)]
    public KlAssetType OverrideAssetType { get; set; }

    [Parameter("Override contract size (0 = auto)", Group = "10b. Sizing Override", DefaultValue = 0, MinValue = 0)]
    public int OverrideContractSize { get; set; }

    [Parameter("Override quote currency (blank = auto)", Group = "10b. Sizing Override", DefaultValue = "")]
    public string OverrideQuoteCurrency { get; set; } = "";

    [Parameter("Override conv USD/quote (0 = auto)", Group = "10b. Sizing Override", DefaultValue = 0.0, MinValue = 0)]
    public double OverrideConvUsdPerQuote { get; set; }

    [Parameter("Override: is crypto (false = auto)", Group = "10b. Sizing Override", DefaultValue = false)]
    public bool OverrideIsCrypto { get; set; }

    // ── GROUP 11: Exit / Session ────────────────────────────────────────────
    [Parameter("Start hour (VN, no entries before)", Group = "11. Exit/Session", DefaultValue = 6, MinValue = 0, MaxValue = 23)]
    public int StartHourVn { get; set; }

    [Parameter("Stop hour (VN, close + block from)", Group = "11. Exit/Session", DefaultValue = 3, MinValue = 0, MaxValue = 23)]
    public int StopHourVn { get; set; }

    [Parameter("Entry cutoff hour (Bangkok, block new entries from)", Group = "11. Exit/Session", DefaultValue = 22, MinValue = 0, MaxValue = 23)]
    public int EntryCutoffHourBangkok { get; set; }

    [Parameter("Use entry cutoff (Bangkok) — also cancels pending orders at cutoff hour", Group = "11. Exit/Session", DefaultValue = true)]
    public bool UseEntryCutoffBangkok { get; set; }

    [Parameter("Use session window", Group = "11. Exit/Session", DefaultValue = true)]
    public bool UseSessionWindow { get; set; }

    [Parameter("Flip: close/cancel opposite before new entry", Group = "11. Exit/Session", DefaultValue = false)]
    public bool AllowFlip { get; set; } = false;

    [Parameter("Allow multiple setups (positions + pendings)", Group = "11. Exit/Session", DefaultValue = true)]
    public bool AllowParallel { get; set; } = true;

    // ── GROUP 12: Spread ────────────────────────────────────────────────────
    [Parameter("Spread override (0=auto). XAU/XAG: price as UI (0.4). Forex: pips", Group = "12. Spread", DefaultValue = 0.0, MinValue = 0)]
    public double SpreadPipsOverride { get; set; }

    [Parameter("Enable spread gate (skip trade if spread too wide)", Group = "12. Spread", DefaultValue = true)]
    public bool EnableSpreadGate { get; set; }

    [Parameter("Max spread gate. Forex: pips (0.5). XAU/XAG: price (0.50). 0=disabled", Group = "12. Spread", DefaultValue = 0.5, MinValue = 0, Step = 0.05)]
    public double MaxSpreadGate { get; set; }

    [Parameter("Adjust entry/SL/TP by spread (false=no shift, true=shift+recalc TP for cfgRR)", Group = "12. Spread", DefaultValue = false)]
    public bool RecalcTpAfterSpread { get; set; } = false;

    // ── GROUP 12b: News Filter ──────────────────────────────────────────────
    [Parameter("Enable news filter (CPI/NFP block)", Group = "12b. News Filter", DefaultValue = true)]
    public bool EnableNewsFilter { get; set; } = true;

    [Parameter("News provider", Group = "12b. News Filter", DefaultValue = "TradingEconomics")]
    public string NewsProvider { get; set; } = "TradingEconomics";

    [Parameter("News API key (blank = no API calls)", Group = "12b. News Filter", DefaultValue = "")]
    public string NewsApiKey { get; set; } = "";

    [Parameter("News lookahead days", Group = "12b. News Filter", DefaultValue = 14, MinValue = 1, MaxValue = 60)]
    public int NewsLookaheadDays { get; set; } = 14;

    [Parameter("News refresh interval hours", Group = "12b. News Filter", DefaultValue = 12.0, MinValue = 0.5, MaxValue = 168)]
    public double NewsRefreshHours { get; set; } = 12.0;

    [Parameter("Use news cache", Group = "12b. News Filter", DefaultValue = true)]
    public bool UseNewsCache { get; set; } = true;

    [Parameter("Cache file name", Group = "12b. News Filter", DefaultValue = "loop6_news_cache.json")]
    public string NewsCacheFileName { get; set; } = "loop6_news_cache.json";

    [Parameter("Cache max age hours", Group = "12b. News Filter", DefaultValue = 48.0, MinValue = 1)]
    public double NewsCacheMaxAgeHours { get; set; } = 48.0;

    [Parameter("Fail-safe mode (ContinueWithWarning / UseCacheThenBlock / BlockAllAffectedSymbols)", Group = "12b. News Filter", DefaultValue = "UseCacheThenBlock")]
    public string NewsFailSafeMode { get; set; } = "UseCacheThenBlock";

    [Parameter("Block currencies (comma-separated)", Group = "12b. News Filter", DefaultValue = "USD")]
    public string BlockCurrencies { get; set; } = "USD";

    [Parameter("Block event keywords (comma-separated)", Group = "12b. News Filter", DefaultValue = "CPI,Consumer Price Index,NFP,Nonfarm,Non-Farm,Non Farm Payrolls,Non-Farm Payrolls,Payrolls,Employment Situation")]
    public string BlockEventKeywords { get; set; } = "CPI,Consumer Price Index,NFP,Nonfarm,Non-Farm,Non Farm Payrolls,Non-Farm Payrolls,Payrolls,Employment Situation";

    [Parameter("USD news affected symbols (comma-separated)", Group = "12b. News Filter", DefaultValue = "EURUSD,GBPUSD,AUDUSD,NZDUSD,USDJPY,USDCAD,USDCHF,XAUUSD")]
    public string BlockUsdNewsSymbols { get; set; } = "EURUSD,GBPUSD,AUDUSD,NZDUSD,USDJPY,USDCAD,USDCHF,XAUUSD";

    [Parameter("Conservative cross-pair block", Group = "12b. News Filter", DefaultValue = false)]
    public bool EnableConservativeCrossBlock { get; set; } = false;

    [Parameter("Conservative cross symbols (comma-separated)", Group = "12b. News Filter", DefaultValue = "EURJPY")]
    public string ConservativeCrossBlockSymbols { get; set; } = "EURJPY";

    [Parameter("Block before news (min)", Group = "12b. News Filter", DefaultValue = 60.0, MinValue = 0, MaxValue = 240)]
    public double BlockBeforeHighImpactMin { get; set; } = 60.0;

    [Parameter("Block after news (min)", Group = "12b. News Filter", DefaultValue = 30.0, MinValue = 0, MaxValue = 240)]
    public double BlockAfterHighImpactMin { get; set; } = 30.0;

    [Parameter("Enable force-close before news", Group = "12b. News Filter", DefaultValue = true)]
    public bool EnableNewsForceClose { get; set; } = true;

    [Parameter("Force-close before news (min)", Group = "12b. News Filter", DefaultValue = 15.0, MinValue = 0, MaxValue = 120)]
    public double ForceCloseBeforeNewsMin { get; set; } = 15.0;

    [Parameter("Cancel pending before news (min)", Group = "12b. News Filter", DefaultValue = 15.0, MinValue = 0, MaxValue = 120)]
    public double CancelPendingBeforeNewsMin { get; set; } = 15.0;

    [Parameter("Refresh news on start", Group = "12b. News Filter", DefaultValue = true)]
    public bool NewsRefreshOnStart { get; set; } = true;

    [Parameter("Refresh news on timer", Group = "12b. News Filter", DefaultValue = true)]
    public bool NewsRefreshOnTimer { get; set; } = true;

    [Parameter("Fail-safe Friday block start Bangkok (HH:mm)", Group = "12b. News Filter", DefaultValue = "19:00")]
    public string FailSafeFridayBlockStartBangkok { get; set; } = "19:00";

    [Parameter("Fail-safe Friday block end Bangkok (HH:mm)", Group = "12b. News Filter", DefaultValue = "22:00")]
    public string FailSafeFridayBlockEndBangkok { get; set; } = "22:00";

    [Parameter("Backtest mode (Disabled / CacheOnly / ApiThenCache)", Group = "12b. News Filter", DefaultValue = "CacheOnly")]
    public string NewsBacktestMode { get; set; } = "CacheOnly";

    // ── GROUP 12c: Friday Flat Guard ────────────────────────────────────────
    [Parameter("Enable Friday Flat Guard", Group = "12c. Friday Flat Guard", DefaultValue = true)]
    public bool EnableFridayFlatGuard { get; set; }

    [Parameter("Friday flat close time Bangkok (HH:mm)", Group = "12c. Friday Flat Guard", DefaultValue = "18:00")]
    public string FridayFlatTimeBangkok { get; set; } = "18:00";

    [Parameter("Friday block new entries time Bangkok (HH:mm)", Group = "12c. Friday Flat Guard", DefaultValue = "18:00")]
    public string FridayBlockNewEntriesTimeBangkok { get; set; } = "18:00";

    [Parameter("Monday resume time Bangkok (HH:mm)", Group = "12c. Friday Flat Guard", DefaultValue = "06:00")]
    public string MondayResumeTimeBangkok { get; set; } = "06:00";

    // ── GROUP 13: Entry-SL Zone Gate ────────────────────────────────────────
    [Parameter("Enable Entry-SL zone gate", Group = "13. Entry-SL Zone Gate", DefaultValue = true)]
    public bool EnableEntrySlZoneGate { get; set; }

    [Parameter("Gate rules mask (condM5,ngM15 or R numbers)", Group = "13. Entry-SL Zone Gate", DefaultValue = "condM5,ngM15")]
    public string EntrySlZoneGateRulesMask { get; set; } = "condM5,ngM15";

    [Parameter("Gate TF tokens", Group = "13. Entry-SL Zone Gate", DefaultValue = "240,60,15,5")]
    public string EntrySlZoneGateTfTokens { get; set; } = "240,60,15,5";

    [Parameter("Include KeyLevels", Group = "13. Entry-SL Zone Gate", DefaultValue = true)]
    public bool IncludeKeyLevels { get; set; }

    [Parameter("Include OrderBlocks", Group = "13. Entry-SL Zone Gate", DefaultValue = true)]
    public bool IncludeOrderBlocks { get; set; }

    [Parameter("Include Broken KeyLevels", Group = "13. Entry-SL Zone Gate", DefaultValue = true)]
    public bool IncludeBrokenKeyLevels { get; set; }

    [Parameter("Overlap tolerance (pips)", Group = "13. Entry-SL Zone Gate", DefaultValue = 0.0, MinValue = 0)]
    public double OverlapTolerancePips { get; set; }

    [Parameter("Require same color", Group = "13. Entry-SL Zone Gate", DefaultValue = true)]
    public bool RequireSameColor { get; set; }

    [Parameter("Exclude setup Swing B keylevel", Group = "13. Entry-SL Zone Gate", DefaultValue = true)]
    public bool ExcludeSetupSwingBKeyLevel { get; set; } = true;

    [Parameter("M5 confluence OB only (no M5 KL)", Group = "13. Entry-SL Zone Gate", DefaultValue = true)]
    public bool M5ConfluenceObOnly { get; set; } = true;

    [Parameter("Skip trade when Swing-C zone overlaps same-color zone", Group = "13. Entry-SL Zone Gate", DefaultValue = false)]
    public bool EnableSwingCZoneObstacleGate { get; set; }

    [Parameter("Swing-C obstacle TF tokens (M5,M15,H1,H4)", Group = "13. Entry-SL Zone Gate", DefaultValue = "5,15,60,240")]
    public string SwingCZoneObstacleTfTokens { get; set; } = "5,15,60,240";

    [Parameter("Swing-C obstacle fallback to Daily when no obstacle found in M5-H4", Group = "13. Entry-SL Zone Gate", DefaultValue = false)]
    public bool EnableSwingCZoneObstacleDailyFallback { get; set; }

    // ── GROUP 14: Pending Invalidation ──────────────────────────────────────
    [Parameter("Cancel on post-B push vs obstacle", Group = "14. Pending Invalidation", DefaultValue = false)]
    public bool EnableCancelOnPostBPushObstacle { get; set; }

    [Parameter("Obstacle TF tokens", Group = "14. Pending Invalidation", DefaultValue = "240,60,15,5")]
    public string CancelPostBPushObstacleTfTokens { get; set; } = "240,60,15,5";

    [Parameter("Include KeyLevels", Group = "14. Pending Invalidation", DefaultValue = true)]
    public bool CancelPostBPushObstacleIncludeKeyLevels { get; set; } = true;

    [Parameter("Include Broken KeyLevels", Group = "14. Pending Invalidation", DefaultValue = true)]
    public bool CancelPostBPushObstacleIncludeBrokenKeyLevels { get; set; } = true;

    [Parameter("Include OrderBlocks", Group = "14. Pending Invalidation", DefaultValue = true)]
    public bool CancelPostBPushObstacleIncludeOrderBlocks { get; set; } = true;

    [Parameter("Overlap tolerance (pips)", Group = "14. Pending Invalidation", DefaultValue = 0.0, MinValue = 0)]
    public double CancelPostBPushObstacleTolerancePips { get; set; }

    [Parameter("Rules mask (all or R numbers)", Group = "14. Pending Invalidation", DefaultValue = "all")]
    public string CancelPostBPushObstacleRulesMask { get; set; } = "all";

    [Parameter("Cancel HL M15 when BC > threshold*AB", Group = "14. Pending Invalidation", DefaultValue = true)]
    public bool EnableCancelHlM15BcAbRatio { get; set; } = true;

    [Parameter("HL M15 BC/AB ratio threshold", Group = "14. Pending Invalidation", DefaultValue = 2.7, MinValue = 0.1, MaxValue = 20, Step = 0.1)]
    public double CancelHlM15BcAbRatioThreshold { get; set; } = 2.7;

    [Parameter("HL M15 BC/AB rules mask", Group = "14. Pending Invalidation", DefaultValue = "hlM15")]
    public string CancelHlM15BcAbRatioRulesMask { get; set; } = "hlM15";

    [Parameter("HL M15 BC/AB verbose skip log", Group = "14. Pending Invalidation", DefaultValue = false)]
    public bool CancelHlM15BcAbRatioVerbose { get; set; }

    [Parameter("Cancel on near-D zone touch (split mode)", Group = "14. Pending Invalidation", DefaultValue = true)]
    public bool EnableCancelOnNearDZone { get; set; } = true;

    [Parameter("Near-D cancel rules mask (all or R numbers)", Group = "14. Pending Invalidation", DefaultValue = "all")]
    public string CancelNearDZoneRulesMask { get; set; } = "all";

    [Parameter("Pending cancel: backtest uses bar High/Low; realtime Ask/Bid (near-D + near-B→TpC)", Group = "14. Pending Invalidation", DefaultValue = true)]
    public bool NearDCancelBacktestUseHighLow { get; set; } = true;

    [Parameter("Cancel when B touched then TP-C reached (split mode)", Group = "14. Pending Invalidation", DefaultValue = true)]
    public bool EnableCancelOnNearBThenCTp { get; set; } = true;

    [Parameter("Near-B→TP-C cancel rules mask (all or R numbers)", Group = "14. Pending Invalidation", DefaultValue = "all")]
    public string CancelNearBThenCTpRulesMask { get; set; } = "all";

    [Parameter("Cancel limit when swing C → broken wait D", Group = "14. Pending Invalidation", DefaultValue = true)]
    public bool EnableCancelOnSwingCBrokenWaitD { get; set; } = true;

    [Parameter("C broken-wait-D cancel rules mask (all or R numbers)", Group = "14. Pending Invalidation", DefaultValue = "all")]
    public string CancelSwingCBrokenWaitDRulesMask { get; set; } = "all";

    [Parameter("Cancel stale pending when sideways (10+ ACTIVE swings)", Group = "14. Pending Invalidation", DefaultValue = true)]
    public bool EnableCancelOnStalePendingSwing { get; set; } = true;

    [Parameter("Stale pending: min new ACTIVE swings since fire bar", Group = "14. Pending Invalidation", DefaultValue = 10, MinValue = 1)]
    public int CancelStalePendingSwingThreshold { get; set; } = 10;

    [Parameter("Stale pending cancel rules mask (all or R numbers)", Group = "14. Pending Invalidation", DefaultValue = "all")]
    public string CancelStalePendingSwingRulesMask { get; set; } = "all";

    [Parameter("Post-fill C-rollover at breakeven (split mode)", Group = "14. Pending Invalidation", DefaultValue = true)]
    public bool EnablePostFillCRollover { get; set; } = true;

    [Parameter("Post-fill C-rollover rules mask (all or R numbers)", Group = "14. Pending Invalidation", DefaultValue = "all")]
    public string PostFillCRolloverRulesMask { get; set; } = "all";

    [Parameter("Post-fill C-rollover keep original TP D", Group = "14. Pending Invalidation", DefaultValue = false)]
    public bool PostFillCRolloverKeepOriginalTpD { get; set; }

    // ── GROUP 15: Swing-C TP / B-Broken ─────────────────────────────────────
    [Parameter("Use Swing-C edge take-profit", Group = "15. Swing-C TP / B-Broken", DefaultValue = false)]
    public bool UseSwingCEdgeTakeProfit { get; set; } = false;

    [Parameter("Swing-C TP fallback to RR", Group = "15. Swing-C TP / B-Broken", DefaultValue = true)]
    public bool SwingCEdgeTpFallbackToRR { get; set; } = true;

    [Parameter("Swing-C TP rules mask (all or R numbers)", Group = "15. Swing-C TP / B-Broken", DefaultValue = "all")]
    public string SwingCEdgeTpRulesMask { get; set; } = "all";

    [Parameter("Update TP when Swing-C confirms (live)", Group = "15. Swing-C TP / B-Broken", DefaultValue = true)]
    public bool UpdateTpWhenSwingCConfirms { get; set; } = true;

    [Parameter("Swing-C TP mode (UpdateOnCConfirm / NoTpUntilC / NextKeyLevelAfterC / SplitTpAtCAndD)", Group = "15. Swing-C TP / B-Broken", DefaultValue = "NoTpUntilC")]
    public string SwingCTpModeParam { get; set; } = "NoTpUntilC";

    [Parameter("Skip/cancel if swing-C RR < base RR", Group = "15. Swing-C TP / B-Broken", DefaultValue = false)]
    public bool SkipIfSwingCRrBelowBase { get; set; }

    [Parameter("Move entry if swing-C RR < base RR (split only)", Group = "15. Swing-C TP / B-Broken", DefaultValue = false)]
    public bool MoveEntryIfSwingCRrBelowBase { get; set; }

    [Parameter("MARKET fallback when limit wrong side", Group = "15. Swing-C TP / B-Broken", DefaultValue = true)]
    public bool EnableMarketWrongSideFallback { get; set; } = true;

    [Parameter("Split D min incremental RR above C (0=off, fallback D TP→C)", Group = "15. Swing-C TP / B-Broken", DefaultValue = 0.5, MinValue = 0, MaxValue = 5, Step = 0.05)]
    public double SplitDLegMinIncrementalRr { get; set; } = 0.5;

    [Parameter("Gồng lời: R3-R6 TP D từ D' (M15/H1/H4, không M5); D giữ cancel", Group = "15. Swing-C TP / B-Broken", DefaultValue = false)]
    public bool EnableGongLoiTpD { get; set; }

    [Parameter("Move TP to entry on B-broken (open)", Group = "15. Swing-C TP / B-Broken", DefaultValue = true)]
    public bool EnableMoveTpToEntryOnBBroken { get; set; } = true;

    [Parameter("B-broken open position mode", Group = "15. Swing-C TP / B-Broken", DefaultValue = "MoveTpToEntry")]
    public string BBrokenOpenPositionMode { get; set; } = "MoveTpToEntry";

    [Parameter("B-broken move-TP reject mode", Group = "15. Swing-C TP / B-Broken", DefaultValue = "CloseImmediately")]
    public string BBrokenMoveTpRejectMode { get; set; } = "CloseImmediately";

    [Parameter("Protection anchor mode (ActualFillRelative / PlannedAbsolute)", Group = "15. Swing-C TP / B-Broken", DefaultValue = "ActualFillRelative")]
    public string ProtectionAnchorModeParam { get; set; } = "ActualFillRelative";

    // ── GROUP 16: Risk / Stop Loss ──────────────────────────────────────────
    [Parameter("Use ATR-adjusted SL width mult", Group = "16. Risk/Stop Loss", DefaultValue = true)]
    public bool UseAtrAdjustedSlWidthMultiplier { get; set; } = true;

    [Parameter("Base SL width multiplier", Group = "16. Risk/Stop Loss", DefaultValue = 2.0, MinValue = 0.1, MaxValue = 20, Step = 0.1)]
    public double BaseSlWidthMult { get; set; } = 2.0;

    [Parameter("ATR adjust TF token", Group = "16. Risk/Stop Loss", DefaultValue = "15")]
    public string AtrAdjustTf { get; set; } = "15";

    [Parameter("ATR adjust period", Group = "16. Risk/Stop Loss", DefaultValue = 14, MinValue = 1, MaxValue = 200)]
    public int AtrAdjustPeriod { get; set; } = 14;

    [Parameter("ATR baseline SMA period", Group = "16. Risk/Stop Loss", DefaultValue = 100, MinValue = 1, MaxValue = 500)]
    public int AtrBaselinePeriod { get; set; } = 100;

    [Parameter("ATR adjustment factor", Group = "16. Risk/Stop Loss", DefaultValue = 1.5, MinValue = 0, MaxValue = 5, Step = 0.1)]
    public double AtrAdjustmentFactor { get; set; } = 1.5;

    [Parameter("Min dynamic SL width mult", Group = "16. Risk/Stop Loss", DefaultValue = 1.0, MinValue = 0.1, MaxValue = 20, Step = 0.1)]
    public double MinDynamicSlWidthMult { get; set; } = 1.0;

    [Parameter("Max dynamic SL width mult", Group = "16. Risk/Stop Loss", DefaultValue = 4.0, MinValue = 0.1, MaxValue = 20, Step = 0.1)]
    public double MaxDynamicSlWidthMult { get; set; } = 4.0;

    [Parameter("ATR adjust use closed bar", Group = "16. Risk/Stop Loss", DefaultValue = true)]
    public bool AtrAdjustUseClosedBar { get; set; } = true;

    [Parameter("ATR adjust fallback to base", Group = "16. Risk/Stop Loss", DefaultValue = true)]
    public bool AtrAdjustFallbackToBase { get; set; } = true;

    [Parameter("Allow cross-symbol quote lookup", Group = "16. Risk/Stop Loss", DefaultValue = false)]
    public bool AllowCrossSymbolQuoteLookup { get; set; }

    [Parameter("Enable min SL constraint (spread & pip floor)", Group = "16. Risk/Stop Loss", DefaultValue = true)]
    public bool EnableMinSlConstraint { get; set; } = true;

    [Parameter("Min SL: spread multiplier (e.g. 10 = spread×10 pips)", Group = "16. Risk/Stop Loss", DefaultValue = 10.0, MinValue = 0, Step = 0.5)]
    public double MinSlSpreadMultiple { get; set; } = 10.0;

    [Parameter("Min SL: absolute pips for Forex (5 pips default)", Group = "16. Risk/Stop Loss", DefaultValue = 5.0, MinValue = 0, Step = 0.5)]
    public double MinSlPipsForex { get; set; } = 5.0;

    /// <summary>
    /// Backtest mode (true): clamp SL up to the minimum floor so the trade still fires.
    /// Live mode (false): if computed SL is below the minimum, skip the trade entirely.
    /// </summary>
    [Parameter("Min SL mode: Backtest=clamp / Live=skip trade", Group = "16. Risk/Stop Loss", DefaultValue = true)]
    public bool MinSlBacktestMode { get; set; } = true;

    // ── GROUP 17: Internal R-Score ─────────────────────────────────────────
    [Parameter("Close reason tolerance pips", Group = "17. Internal R-Score", DefaultValue = 2, MinValue = 0, MaxValue = 50)]
    public double CloseReasonTolerancePips { get; set; } = 2;

    [Parameter("Internal TP slippage pips", Group = "17. Internal R-Score", DefaultValue = 0, MinValue = 0, MaxValue = 100)]
    public double InternalTpSlippagePips { get; set; }

    [Parameter("Internal SL slippage pips", Group = "17. Internal R-Score", DefaultValue = 0, MinValue = 0, MaxValue = 100)]
    public double InternalSlSlippagePips { get; set; }

    // ── GROUP 18: Virtual xR Bar-Close Exit ────────────────────────────────
    [Parameter("Enable virtual xR bar-close exit", Group = "18. Virtual xR Exit", DefaultValue = false)]
    public bool EnableVirtualXrBarCloseExit { get; set; }

    [Parameter("Virtual xR trigger R", Group = "18. Virtual xR Exit", DefaultValue = 0.3, MinValue = 0.1, MaxValue = 20, Step = 0.05)]
    public double VirtualXrTriggerR { get; set; } = 0.3;

    [Parameter("Virtual xR exit timeframe token", Group = "18. Virtual xR Exit", DefaultValue = "15")]
    public string VirtualXrExitTimeframe { get; set; } = "15";

    [Parameter("Virtual xR fill mode (SignalBarClose/NextBarOpen/ActualMarketClose)", Group = "18. Virtual xR Exit", DefaultValue = "SignalBarClose")]
    public string VirtualXrFillMode { get; set; } = "SignalBarClose";

    [Parameter("Virtual xR use broker TP (hybrid)", Group = "18. Virtual xR Exit", DefaultValue = false)]
    public bool VirtualXrUseBrokerTp { get; set; }

    [Parameter("Virtual xR emergency broker TP R (0=off)", Group = "18. Virtual xR Exit", DefaultValue = 0, MinValue = 0, MaxValue = 20, Step = 0.1)]
    public double VirtualXrEmergencyBrokerTpR { get; set; }

    // ── GROUP 19: Detailed CSV Analytics ───────────────────────────────────
    [Parameter("Enable detailed CSV log", Group = "19. CSV Analytics", DefaultValue = true)]
    public bool EnableDetailedCsvLog { get; set; }

    [Parameter("Log directory name", Group = "19. CSV Analytics", DefaultValue = "Loop6Logs")]
    public string LogDirectoryName { get; set; } = "Loop6Logs";

    [Parameter("Log session timezone (Bangkok)", Group = "19. CSV Analytics", DefaultValue = "Asia/Bangkok")]
    public string LogSessionTimezone { get; set; } = "Asia/Bangkok";

    [Parameter("Max log rows in memory (0=stream to file)", Group = "19. CSV Analytics", DefaultValue = 0, MinValue = 0)]
    public int MaxLogRowsInMemory { get; set; }

    // ── GROUP 20: H4 SL Mode ───────────────────────────────────────────────
    [Parameter("Enable H4 SL mode (H4 High/Low SL, break-depth guard)", Group = "20. H4 SL Mode", DefaultValue = false)]
    public bool EnableH4SlMode { get; set; }

    [Parameter("H4 SL max lookback (H4 bars)", Group = "20. H4 SL Mode", DefaultValue = 50, MinValue = 1, MaxValue = 500)]
    public int H4SlMaxLookbackH4Bars { get; set; } = 50;

    [Parameter("H4 SL break-depth window (ring buffer capacity)", Group = "20. H4 SL Mode", DefaultValue = 100, MinValue = 1, MaxValue = 10000)]
    public int H4SlBreakDepthWindow { get; set; } = 100;

    [Parameter("H4 SL min samples (below → fallback band MinSlPips→20p)", Group = "20. H4 SL Mode", DefaultValue = 20, MinValue = 1, MaxValue = 1000)]
    public int H4SlMinSampleCount { get; set; } = 20;

    [Parameter("H4 SL band low — trần dưới (mean/p25/p75/pmin/pmax)", Group = "20. H4 SL Mode", DefaultValue = H4SlBandStatRef.P25)]
    public H4SlBandStatRef H4SlBandLowStat { get; set; } = H4SlBandStatRef.P25;

    [Parameter("H4 SL band high — trần trên (mean/p25/p75/pmin/pmax)", Group = "20. H4 SL Mode", DefaultValue = H4SlBandStatRef.P75)]
    public H4SlBandStatRef H4SlBandHighStat { get; set; } = H4SlBandStatRef.P75;

    [Parameter("H4 SL hi-fallback (dist>trần trên): mean/p25/p75/pmin/pmax/slh1(tìm H1 bar)", Group = "20. H4 SL Mode", DefaultValue = H4SlHiFallbackRef.Mean)]
    public H4SlHiFallbackRef H4SlHiFallbackStat { get; set; } = H4SlHiFallbackRef.Mean;

    [Parameter("H4 SL slh1 no-bar fallback (khi slh1 không tìm được H1 bar): mean/p25/p75/pmin/pmax", Group = "20. H4 SL Mode", DefaultValue = H4SlBandStatRef.Mean)]
    public H4SlBandStatRef H4SlHiFallbackSlH1NoBarStat { get; set; } = H4SlBandStatRef.Mean;

    [Parameter("H4 SL: R1/R2 dùng H1 bar (false = H4 cho tất cả)", Group = "20. H4 SL Mode", DefaultValue = true)]
    public bool H4SlR1R2UseH1 { get; set; } = true;

    [Parameter("H4 SL RR guard: khi RR<base → chỉnh entry → mean SL fallback → SKIP", Group = "20. H4 SL Mode", DefaultValue = false)]
    public bool H4SlRrGuard { get; set; }

    [Parameter("H4 SL debug panel on chart", Group = "20. H4 SL Mode", DefaultValue = true)]
    public bool EnableH4SlDebugPanel { get; set; } = true;

    // ── Runtime ─────────────────────────────────────────────────────────────
    PerSymbolSignalHost _host = null!;
    TradeExecutionService _exec = null!;
    readonly OpenPositionBook _book = new();
    int _startUtcHour;
    int _stopUtcHour;
    SymbolSizingProfile _sizingProfile = null!;
    int _onBarCount;
    EntrySlZoneGateConfig _gateCfg = null!;
    PostBPushObstacleCancelRuleConfig _postBCancelCfg = null!;
    HlM15BcAbRatioCancelRuleConfig _hlM15BcAbCancelCfg = null!;
    NearDZoneCancelConfig _nearDCancelCfg = null!;
    NearBThenCTpCancelConfig _nearBCancelCfg = null!;
    SwingCBrokenWaitDCancelConfig _swingCBrokenWaitDCancelCfg = null!;
    StalePendingSwingCancelConfig _stalePendingSwingCancelCfg = null!;
    HashSet<int> _postFillCRolloverSlots = new();
    readonly HashSet<string> _rolloverBeLoggedKeys = new();
    readonly HashSet<string> _rolloverZoneSkipLoggedKeys = new();
    readonly HashSet<string> _splitPairedCloseGuard = new(StringComparer.Ordinal);
    SwingCEdgeTpConfig _swingCTpCfg = null!;
    ProtectionAnchorMode _protectionAnchorMode;
    AtrSlWidthAdjustConfig _atrSlCfg = null!;
    BBrokenHandlerConfig _bBrokenCfg = null!;
    VirtualXrConfig _virtualXrCfg = new();
    readonly SlotAuditTracker _slotAudit = new();
    readonly CloseReasonAuditTracker _closeReasonAudit = new();
    readonly ExecutionAuditTracker _execAudit = new();
    NewsFilterService? _newsFilter;
    NewsForceCloseTracker _newsForceCloseTracker = new();
    FridayFlatGuardService? _fridayFlat;
    FridayFlatCloseTracker _fridayFlatTracker = new();
    Loop6DetailedCsvLogger? _csvLog;
    readonly Loop6TradeExcursionTracker _tradeExcursions = new();
    readonly DeferredSplitFireQueue _deferredSplitFires = new();
    CompoundFireChartDebugLabels? _fireChartLabels;

    // SetupAtCondFire: B/D geometry pinned at cond-fire bar N, keyed by sourceEventKey.
    readonly Dictionary<string, SwingBResult> _pinnedSwingBByKey = new(StringComparer.Ordinal);
    readonly Dictionary<string, DFireAtFireCache> _pinnedDFireByKey = new(StringComparer.Ordinal);
    int _indexCheckBarsRemaining = 5;
    int _r3r6DebugBarsRemaining  = 5;   // periodic condition dumps when R3–R6 configured but silent
    int _posDiagSlLogBar = -1;
    readonly HashSet<long> _posDiagSlLoggedThisBar = new();

    // ── H4 SL mode: break-depth ring buffers (separate BUY/SELL pools) ──────
    BreakDepthStatistics _buyStopBreakDepthStats = null!;
    BreakDepthStatistics _sellStopBreakDepthStats = null!;
    readonly HashSet<int> _breakDepthRecordedPivots = new();
    H4SlDebugPanelRenderer? _h4SlDebugPanel;
    H4SlPlanLevelOverlay? _h4SlPlanOverlay;
    H4SlPlanDebugSnapshot _lastH4SlPlanDebug;
    string _lastH4SlResolveNote = "";

    protected override void OnStart()
    {
        try
        {
            OnStartCore();
        }
        catch (Exception ex)
        {
            Print($"[L6BT] FATAL OnStart: {ex.GetType().Name}: {ex.Message}");
            Print($"[L6BT] {ex.StackTrace}");
            Stop();
        }
    }

    void OnStartCore()
    {
        if (TimeFrame != TimeFrame.Minute15)
            Log($"[L6BT] WARNING: chart TF is {TimeFrame}, Rule 1 expects M15 swings — results may be off.");

        Print($"[L6BT] Backtest symbols=[{SymbolName}]");

        _startUtcHour = VnHourToUtc(StartHourVn);
        _stopUtcHour = VnHourToUtc(StopHourVn);

        // Auto-detect sizing from symbol name, then apply any user overrides.
        _sizingProfile = SymbolSizingProfile.Detect(SymbolName);
        if (OverrideAssetType != KlAssetType.Auto)
            _sizingProfile = _sizingProfile.WithOverrides(
                assetType: OverrideAssetType,
                contractSize: OverrideContractSize > 0 ? OverrideContractSize : (int?)null,
                quoteCurrency: !string.IsNullOrWhiteSpace(OverrideQuoteCurrency) ? OverrideQuoteCurrency.Trim().ToUpperInvariant() : null,
                isCrypto: null,
                baseAssetName: null);
        else if (OverrideContractSize > 0 || !string.IsNullOrWhiteSpace(OverrideQuoteCurrency) || OverrideIsCrypto)
            _sizingProfile = _sizingProfile.WithOverrides(
                assetType: null,
                contractSize: OverrideContractSize > 0 ? OverrideContractSize : (int?)null,
                quoteCurrency: !string.IsNullOrWhiteSpace(OverrideQuoteCurrency) ? OverrideQuoteCurrency.Trim().ToUpperInvariant() : null,
                isCrypto: OverrideIsCrypto ? true : (bool?)null,
                baseAssetName: null);

        Log($"[L6BT] Sizing auto-detect for '{SymbolName}': " +
            $"AssetType={_sizingProfile.AssetType} ContractSize={_sizingProfile.ContractSize} " +
            $"Quote={_sizingProfile.QuoteCurrency} Crypto={_sizingProfile.IsCrypto}");

        var snapshot = BuildSnapshot();

        _host = new PerSymbolSignalHost(
            symbolName:         SymbolName,
            tickSize:           Symbol.TickSize,
            scanTimeFrame:      TimeFrame,
            chartBars:          Bars,
            marketData:         MarketData,
            snapshot:           in snapshot,
            r1:                 EffectiveCompoundRule(CompoundRule1, EnableCompoundRule1),
            r2:                 EffectiveCompoundRule(CompoundRule2, EnableCompoundRule2),
            r3:                 EffectiveCompoundRule(CompoundRule3, EnableCompoundRule3),
            r4:                 EffectiveCompoundRule(CompoundRule4, EnableCompoundRule4),
            r5:                 EffectiveCompoundRule(CompoundRule5, EnableCompoundRule5),
            r6:                 EffectiveCompoundRule(CompoundRule6, EnableCompoundRule6),
            syncMode:           CompoundSyncMode,
            eventValidBars:     CompoundEventValidBars,
            injectM15CloseEdge: InjectM15CloseEdgeSignal,
            warmupBars:         WarmupBars,
            log:                EnableLog ? (Action<string>)Print : null,
            mode:               UseVisualReplayParity
                ? PerSymbolSignalHostMode.BacktestVisualReplay
                : PerSymbolSignalHostMode.BacktestBarClose,
            compoundWindowMode: CompoundWindowMode,
            visualReplayUseM5Bars: VisualReplayUseM5Bars);

        _host.Initialize();

        if (EnableCompoundFireChartLabels)
        {
            _fireChartLabels = new CompoundFireChartDebugLabels(Chart, Bars, Symbol.PipSize);
            Print("[L6BT] Compound fire chart labels ON — FIRE / WAIT-C / C-OK / SKIP at fire bar");
        }

        if (EnableCompoundAuditLog && EnableLog)
        {
            _host.SetCompoundAuditLogger(Log, enabled: true);
            Print("[L6BT] Compound audit log ON — emits COMPOUND_* (incl. ANCHOR_* when AnchorLatchWindow/SetupAtCondFire)");
        }

        if (CompoundWindowMode == CompoundWindowMode.SetupAtCondFire)
        {
            _host.SetAnchorCreatedCallback(PinSetupAtCondFire);
            Print("[L6BT] SetupAtCondFire: B/D geometry pinned at cond-fire bar N; rule-fire = permission to submit");
        }

        if (EnableCompoundStageDiagLog && EnableLog)
        {
            _host.SetCompoundDiagnosticLogger(Log, enabled: true);
            Print("[L6BT] Compound StageDiag log ON — emits [StageDiag] lines for every stage create/confirm/dedup/expire");
        }

        if (EnableCompoundFireDebugLog && EnableLog)
        {
            _host.SetCompoundFireDebugLogger(Log, enabled: true);
            Print("[L6BT] Compound FireDbg log ON — emits [FireDbg] leg eval + HTF-TOUCH-TABLE (H1/H4 zones + M5 probe)");
        }

        Print($"[L6BT] Warmup absolute index check startBar={_host.WarmupStartBar} endBar={_host.WarmupEndBar} nextBar={_host.NextBarToProcess}");

        // ── Startup rule diagnostics ────────────────────────────────────────
        var ruleStrings = new[]
        {
            EffectiveCompoundRule(CompoundRule1, EnableCompoundRule1),
            EffectiveCompoundRule(CompoundRule2, EnableCompoundRule2),
            EffectiveCompoundRule(CompoundRule3, EnableCompoundRule3),
            EffectiveCompoundRule(CompoundRule4, EnableCompoundRule4),
            EffectiveCompoundRule(CompoundRule5, EnableCompoundRule5),
            EffectiveCompoundRule(CompoundRule6, EnableCompoundRule6),
        };
        var activeLabels = new System.Collections.Generic.List<string>();
        for (var i = 0; i < ruleStrings.Length; i++)
            if (!string.IsNullOrWhiteSpace(ruleStrings[i]))
                activeLabels.Add($"R{i + 1}");

        Print($"[L6BT] CompoundPreset slots active=[{string.Join(",", activeLabels)}] | {FormatCompoundRuleEnableFlags()}");

        if (activeLabels.Count > 0 && activeLabels.TrueForAll(l => l == "R1" || l == "R2"))
            Print("[L6BT] WARNING: Only R1/R2 have non-empty rule strings — R3-R6 will never fire. Check Group 03 parameters.");

        var r3r6Active = !string.IsNullOrWhiteSpace(ruleStrings[2])
                      || !string.IsNullOrWhiteSpace(ruleStrings[3])
                      || !string.IsNullOrWhiteSpace(ruleStrings[4])
                      || !string.IsNullOrWhiteSpace(ruleStrings[5]);
        if (!r3r6Active)
            Print("[L6BT] WARNING: CompoundRule3–6 all empty → R3-R6 will never appear. Reset to defaults if unintentional.");

        Print($"[L6BT] AlertOnBuy={AlertOnBuy} AlertOnSell={AlertOnSell} PreferTierFilter={PreferM15CompoundRules} (R3/R4>R5/R6>R1/R2)");
        Print($"[L6BT] EntrySlZoneGateRulesMask raw=\"{EntrySlZoneGateRulesMask}\" (zone filter only — does NOT disable signals)");
        Print($"[L6BT] R1-R4 preset: H1/H4 canBuyReal/canSellReal (M5/M15 Real)");
        Print($"[L6BT] R5-R6 preset: cond*EventNGM15 + can*Real on M5/M15/H1/H4 (no TouchM5)");
        Print($"[L6BT] CompoundSyncMode={CompoundSyncMode} CompoundWindowMode={CompoundWindowMode} EventValidWindow={CompoundEventValidBars} InjectM15CloseEdge={InjectM15CloseEdgeSignal}");
        if (CompoundSyncMode == CompoundEvalSyncMode.TradingView
            && CompoundWindowMode != CompoundWindowMode.SourceOneShot)
            Print("[L6BT] TradingView anchor: one-shot at cond fire — all state legs must be OK @ T (no pending/retry; SACF pin only on immediate emit)");
        Print("[L6BT] R1/R2 (M5 primary): EventValidWindow counts M5 source bars; R3-R6 count M15 chart bars");

        // Confirm parsed active slots from orchestrator (post-Initialize)
        var parsedSlots = _host.ActiveCompoundSlots();
        Print($"[L6BT] Orchestrator parsed slots=[{string.Join(",", parsedSlots.Select(s => $"R{s + 1}"))}]");
        if (parsedSlots.Count > 0 && parsedSlots.All(s => s < 2))
            Print("[L6BT] WARNING EnabledRulesMask equivalent: orchestrator loaded only R1/R2 — R3-R6 strings may be empty or parse-failed.");

        _gateCfg = BuildGateConfig();
        _postBCancelCfg = BuildPostBPushCancelConfig();
        _hlM15BcAbCancelCfg = BuildHlM15BcAbCancelConfig();
        _nearDCancelCfg = BuildNearDZoneCancelConfig();
        _nearBCancelCfg = BuildNearBThenCTpCancelConfig();
        _swingCBrokenWaitDCancelCfg = BuildSwingCBrokenWaitDCancelConfig();
        _stalePendingSwingCancelCfg = BuildStalePendingSwingCancelConfig();
        _postFillCRolloverSlots = PostBPushObstacleCancelRuleConfig.ParseRulesMask(
            PostFillCRolloverRulesMask, EnableLog ? (Action<string>)Log : null);
        _swingCTpCfg = BuildSwingCTpConfig();
        _protectionAnchorMode = ProtectionAnchorModeParser.Parse(ProtectionAnchorModeParam);
        _atrSlCfg = BuildAtrSlWidthConfig();
        _bBrokenCfg = BuildBBrokenConfig();
        _virtualXrCfg = VirtualXrConfig.Parse(
            EnableVirtualXrBarCloseExit,
            VirtualXrTriggerR,
            VirtualXrExitTimeframe,
            VirtualXrFillMode,
            VirtualXrUseBrokerTp,
            VirtualXrEmergencyBrokerTpR);
        if (EnableEntrySlZoneGate)
        {
            _host.EnsureGateTfEngines(_gateCfg.TfTokens, in snapshot);
            Print($"[L6BT] Zone gate ON rev=7 | excludeOwnB={ExcludeSetupSwingBKeyLevel} | m5ObOnly={M5ConfluenceObOnly} | preferR3R6={PreferM15CompoundRules} | mask={EntrySlZoneGateRulesMask} | TFs={EntrySlZoneGateTfTokens} | slots=[{string.Join(",", _gateCfg.GatedSlotIndices.OrderBy(x => x).Select(x => x + 1))}]");
        }
        else
            Print($"[L6BT] Zone gate OFF rev=7 | preferR3R6={PreferM15CompoundRules} — all FIRE plans execute without zone confluence check");

        if (EnableCancelOnPostBPushObstacle)
        {
            var cancelTfs = string.Join(",", _postBCancelCfg.TfTokens);
            var cancelSlots = string.Join(",", _postBCancelCfg.RuleSlotIndices.OrderBy(x => x).Select(x => x + 1));
            _host.EnsureGateTfEngines(_postBCancelCfg.TfTokens, in snapshot);
            Print($"[L6BT] Post-B push obstacle cancel ON | mask={CancelPostBPushObstacleRulesMask} | TFs={cancelTfs} | slots=[{cancelSlots}] | tol={CancelPostBPushObstacleTolerancePips}p");
        }
        else
            Print("[L6BT] Post-B push obstacle cancel OFF");

        if (EnableCancelHlM15BcAbRatio)
        {
            var bcAbSlots = string.Join(",", _hlM15BcAbCancelCfg.RuleSlotIndices.OrderBy(x => x).Select(x => x + 1));
            Print($"[L6BT] HL M15 BC/AB cancel ON | mask={CancelHlM15BcAbRatioRulesMask} | slots=[{bcAbSlots}] | threshold={CancelHlM15BcAbRatioThreshold} | formula=BC>{CancelHlM15BcAbRatioThreshold}*AB");
        }
        else
            Print("[L6BT] HL M15 BC/AB cancel OFF");

        if (_swingCTpCfg.UseSwingCEdgeTakeProfit)
        {
            var swingSlots = string.Join(",", _swingCTpCfg.RuleSlotIndices.OrderBy(x => x).Select(x => x + 1));
            var deferHint = (_swingCTpCfg.TpMode == SwingCTpMode.SplitTpAtCAndD && !_swingCTpCfg.SwingCEdgeTpFallbackToRR)
                ? " | deferredSplit=ON(await-C)" : "";
            if (_swingCTpCfg.TpMode == SwingCTpMode.SplitTpAtCAndD)
            {
                _host.EnsureGateTfEngines(s_dZoneTfTokens, in snapshot);
                Print($"[L6BT] D-zone TF engines ensured for SplitTpAtCAndD: {string.Join(",", s_dZoneTfTokens)}");

                if (EnableCancelOnNearDZone)
                {
                    _host.EnsureGateTfEngines(s_nearDFallbackTfTokens, in snapshot);
                    var nearDSlots = string.Join(",", _nearDCancelCfg.RuleSlotIndices.OrderBy(x => x).Select(x => x + 1));
                    var nearDProbeMode = NearDCancelBacktestUseHighLow
                        ? "backtest=bar H/L / realtime=Ask|Bid (near-D; near-B touch+TpC)"
                        : "Ask/Bid (all modes)";
                    Print($"[L6BT] Near-D cancel gate ON | mask={CancelNearDZoneRulesMask} | slots=[{nearDSlots}] | probe={nearDProbeMode} | fallbackTFs={string.Join(",", s_nearDFallbackTfTokens)}");
                }
                else
                    Print("[L6BT] Near-D cancel gate OFF");

                if (EnableCancelOnNearBThenCTp)
                {
                    var nearBSlots = string.Join(",", _nearBCancelCfg.RuleSlotIndices.OrderBy(x => x).Select(x => x + 1));
                    Print($"[L6BT] Near-B→TpC cancel gate ON | mask={CancelNearBThenCTpRulesMask} | slots=[{nearBSlots}] | probe={(NearDCancelBacktestUseHighLow ? "backtest H/L" : "Ask/Bid")}");
                }
                else
                    Print("[L6BT] Near-B→TpC cancel gate OFF");

                if (EnablePostFillCRollover)
                {
                    var rollSlots = string.Join(",", _postFillCRolloverSlots.OrderBy(x => x).Select(x => x + 1));
                    Print($"[L6BT] Post-fill C-rollover ON | mask={PostFillCRolloverRulesMask} | slots=[{rollSlots}] | scope=M15 | keepOriginalTpD={PostFillCRolloverKeepOriginalTpD}");
                }
                else
                    Print("[L6BT] Post-fill C-rollover OFF");

                if (EnableGongLoiTpD)
                {
                    _host.EnsureGateTfEngines(s_dPrimeZoneTfTokens, in snapshot);
                    Print($"[L6BT] Gồng lời TP D' ON | slots=R3-R6 | scanTFs={string.Join(",", s_dPrimeZoneTfTokens)} | cancel=D(M5/M15/H1/H4) | noDPrime→fallback D");
                }

                Print("[L6BT] Split-leg paired close ON — pending: SL→cancel sibling, TP→cancel when |C TP|=|D TP| | backtest filled: SL→sync sibling, TP→sync when same TP | live filled: broker only");
            }
            Print($"[L6BT] Swing-C edge TP ON | mask={SwingCEdgeTpRulesMask} | slots=[{swingSlots}] | fallbackRR={SwingCEdgeTpFallbackToRR}{deferHint} | liveUpdateOnCConfirm={_swingCTpCfg.UpdateTpWhenSwingCConfirms} | mode={_swingCTpCfg.TpMode}");
            if (EnableCancelOnSwingCBrokenWaitD)
            {
                var cBrokenSlots = string.Join(",", _swingCBrokenWaitDCancelCfg.RuleSlotIndices.OrderBy(x => x).Select(x => x + 1));
                Print($"[L6BT] C-broken-wait-D cancel gate ON | mask={CancelSwingCBrokenWaitDRulesMask} | slots=[{cBrokenSlots}] | entryC=ACTIVE|D_SWING | split pair-cancel=ON");
            }
            else
                Print("[L6BT] C-broken-wait-D cancel gate OFF");

            if (EnableCancelOnStalePendingSwing)
            {
                var staleSlots = string.Join(",", _stalePendingSwingCancelCfg.RuleSlotIndices.OrderBy(x => x).Select(x => x + 1));
                Print($"[L6BT] Stale-pending swing cancel ON | mask={CancelStalePendingSwingRulesMask} | slots=[{staleSlots}] | threshold={CancelStalePendingSwingThreshold} ACTIVE swings");
            }
            else
                Print("[L6BT] Stale-pending swing cancel OFF");
        }
        else
        {
            Print("[L6BT] Swing-C edge TP OFF (RR TP only)");
            if (EnablePostFillCRollover)
            {
                var rollSlots = string.Join(",", _postFillCRolloverSlots.OrderBy(x => x).Select(x => x + 1));
                Print($"[L6BT] Post-fill C-rollover INACTIVE — requires UseSwingCEdgeTakeProfit + mode=SplitTpAtCAndD | param=ON mask={PostFillCRolloverRulesMask} slots=[{rollSlots}]");
            }
        }

        if (_swingCTpCfg.UseSwingCEdgeTakeProfit
            && _swingCTpCfg.TpMode != SwingCTpMode.SplitTpAtCAndD
            && EnablePostFillCRollover)
        {
            var rollSlots = string.Join(",", _postFillCRolloverSlots.OrderBy(x => x).Select(x => x + 1));
            Print($"[L6BT] Post-fill C-rollover INACTIVE — SwingCTpMode={_swingCTpCfg.TpMode} (need SplitTpAtCAndD) | param=ON mask={PostFillCRolloverRulesMask} slots=[{rollSlots}]");
        }

        if (EnableSwingCZoneObstacleGate)
        {
            var obstTfs = SwingCZoneObstacleTfTokens
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            _host.EnsureGateTfEngines(obstTfs, in snapshot);
            if (EnableSwingCZoneObstacleDailyFallback)
            {
                _host.EnsureGateTfEngines(s_swingCObstacleFallbackTfTokens, in snapshot);
                Print($"[L6BT] Swing-C zone obstacle gate ON | tfs=[{string.Join(",", obstTfs)}] | dailyFallback=ON");
            }
            else
                Print($"[L6BT] Swing-C zone obstacle gate ON | tfs=[{string.Join(",", obstTfs)}] | dailyFallback=OFF");
        }
        else
            Print("[L6BT] Swing-C zone obstacle gate OFF");

        // ── H4 SL mode: register H4 ("240") shell + seed break-depth stats from warmup ──
        if (EnableH4SlMode)
        {
            _host.EnsureGateTfEngines(new[] { "240" }, in snapshot);
            if (H4SlR1R2UseH1)
                _host.EnsureGateTfEngines(new[] { "60" }, in snapshot);
            _buyStopBreakDepthStats  = new BreakDepthStatistics(H4SlBreakDepthWindow);
            _sellStopBreakDepthStats = new BreakDepthStatistics(H4SlBreakDepthWindow);
            Print($"[L6BT] H4 SL mode ON | window={H4SlBreakDepthWindow} minSamples={H4SlMinSampleCount} maxLookbackH4={H4SlMaxLookbackH4Bars} bandLo={H4SlBandLowStat} bandHi={H4SlBandHighStat} shortPoolFallback=[MinSlPipsForex,20]pip R1R2UseH1={H4SlR1R2UseH1}");
            BackfillBreakDepthStatsFromHistory();
            if (EnableH4SlDebugPanel)
            {
                _h4SlDebugPanel = new H4SlDebugPanelRenderer();
                _h4SlPlanOverlay = new H4SlPlanLevelOverlay(Chart, Bars);
                Print("[L6BT] H4 SL debug panel ON — pool stats + entry/SL/TP C/D levels on chart");
                UpdateH4SlDebugPanel();
            }
        }
        else
            Print("[L6BT] H4 SL mode OFF");

        Print($"[L6BT] B-broken open mode={BBrokenOpenPositionMode} moveTpOnBroken={EnableMoveTpToEntryOnBBroken} rejectMode={BBrokenMoveTpRejectMode}");

        if (_virtualXrCfg.Enabled)
            Print($"[L6BT] Virtual xR bar-close exit ON | xR={VirtualXrTriggerR} tf={VirtualXrExitTimeframe} fillMode={_virtualXrCfg.FillMode} brokerTp={VirtualXrUseBrokerTp} emergencyTpR={VirtualXrEmergencyBrokerTpR} pureMode={_virtualXrCfg.PureVirtualMode}");
        else
            Print("[L6BT] Virtual xR bar-close exit OFF");

        if (_atrSlCfg.UseAtrAdjustedSlWidthMultiplier)
            Print($"[L6BT] ATR SL width adjust ON | tf={AtrAdjustTf} period={AtrAdjustPeriod} baseline={AtrBaselinePeriod} baseMult={BaseSlWidthMult} factor={AtrAdjustmentFactor} clamp=[{MinDynamicSlWidthMult},{MaxDynamicSlWidthMult}] fallbackBase={AtrAdjustFallbackToBase}");
        else
            Print($"[L6BT] ATR SL width adjust OFF | fixed SlWidthMult={SlWidthMult}");

        var spreadAtStart = ResolveSpreadSnapshot();
        Print(SpreadTelemetryLog.FormatStartup(
            SpreadPipsOverride,
            spreadAtStart.Source,
            spreadAtStart.Pips,
            spreadAtStart.Price,
            Symbol.Bid,
            Symbol.Ask,
            EnableMinSlConstraint,
            MinSlBacktestMode,
            MinSlSpreadMultiple,
            MinSlPipsForex));

        if (EnableDetailedCsvLog)
        {
            var runId = $"{SymbolName}_{Server.TimeInUtc:yyyyMMdd_HHmmss}";
            _csvLog = new Loop6DetailedCsvLogger(LogDirectoryName, runId, Print);
            _csvLog.LogRuntimeTelemetry(BuildAnalyticsContext(), ResolveSpreadOverrideMode());
        }

        var isGoldAtStart = SpreadResolver.UsesPriceSpreadOverride(SymbolName, _sizingProfile.AssetType);
        var spreadGateUnit = isGoldAtStart ? "price" : "pip";
        if (EnableSpreadGate && MaxSpreadGate > 0)
            Print($"[L6BT] Spread gate ON | max={MaxSpreadGate:0.####} {spreadGateUnit} ({(isGoldAtStart ? "XAU price-mode" : "Forex pip-mode")})");
        else
            Print("[L6BT] Spread gate OFF (all spreads accepted)");

        _exec = new TradeExecutionService(this, SymbolName, LabelPrefix, Log);

        // ── News filter init ─────────────────────────────────────────────────
        InitNewsFilter();

        // ── Friday Flat Guard init ───────────────────────────────────────────
        InitFridayFlatGuard();

        _execAudit.SetPipSize(AuditPipSize());
        _execAudit.SetProtectionAnchorMode(_protectionAnchorMode);
        _execAudit.SetInternalPerformanceConfig(new InternalPerformanceConfig
        {
            CloseReasonTolerancePips = CloseReasonTolerancePips,
            InternalTpSlippagePips = InternalTpSlippagePips,
            InternalSlSlippagePips = InternalSlSlippagePips,
        });
        _execAudit.SetVirtualXrConfig(in _virtualXrCfg);
        _execAudit.SetStrategyEvalContext(new InternalStrategyEvalContext
        {
            StartEquityUsd = AccountBalanceFtmo,
            RewardRisk = RewardRisk,
            ProtectionAnchorMode = _protectionAnchorMode,
            UseSwingCEdgeTakeProfit = UseSwingCEdgeTakeProfit,
        });

        if (EnableTrading)
        {
            Positions.Closed += OnPositionClosedAudit;
            Positions.Opened += OnPositionOpenedAudit;
        }

        Print($"[L6BT] Protection anchor mode={_protectionAnchorMode}");
        Print($"[L6BT] Internal R-score ON | closeTolPips={CloseReasonTolerancePips} tpSlipPips={InternalTpSlippagePips} slSlipPips={InternalSlSlippagePips}");
        Print($"[L6BT] RR effective={RewardRisk:0.#}");
        Log($"[L6BT] Started {SymbolName} {TimeFrame} | mode={(UseVisualReplayParity ? $"VisualReplay/{(VisualReplayUseM5Bars ? "M5" : "M1")}" : "BacktestBarClose")} | EnableTrading={EnableTrading} | session UTC [{_startUtcHour}->{_stopUtcHour}) | entryCutoffBkk={EntryCutoffHourBangkok} useEntryCutoff={UseEntryCutoffBangkok} | risk={RiskPercent}% bal={AccountBalanceFtmo} RR={RewardRisk:0.#} SLx={SlWidthMult}");
        Print($"[L6BT] Exposure: AllowFlip={AllowFlip} (close/cancel opposite) | AllowParallel={AllowParallel} (multi setup)");
        if (EnablePositionSlDiagnosticLog)
            Print($"[L6BT] Position SL diagnostic ON — near={PositionSlDiagnosticNearPips}p | logs OPEN/PRICE-near-SL/CLOSE + live snapshot");
    }

    protected override void OnBar()
    {
        if (_host == null)
            return;

        // 1) reconcile book with live orders + audit closes for filled positions.
        var removedLabels = _book.RetainOnly(_exec.LiveLabels());
        ReconcileRemovedLabels(removedLabels);

        // 1-pre) record the chart bar at which each tracked order first becomes an open position
        // (used by the post-fill C-rollover rule to detect zones that appear AFTER the fill).
        CaptureFillBars();

        // 1a) Friday Flat Guard: execute force-close (backtest compatibility; live uses OnTimer).
        try { CheckAndExecuteFridayFlatClose(); }
        catch (Exception ex) { Print($"[FRIDAY_FLAT] OnBar error: {ex.Message}"); }

        // 1b) news filter: refresh if needed + execute force-close (backtest compatibility).
        _newsFilter?.RefreshIfNeeded(Server.TimeInUtc);
        CheckAndExecuteNewsForceClose();

        // 2) advance engine + collect fires for the just-closed bar.
        var events = _host.Tick();
        _onBarCount++;

        // 2a) H4 SL mode: collect break-depth samples from newly-latched broken swings.
        if (EnableH4SlMode)
            CollectBreakDepthStats();

        if (EnableH4SlDebugPanel)
            UpdateH4SlDebugPanel();

        if (_indexCheckBarsRemaining > 0 && _host.LastClosedBarProcessedThisTick >= 0)
        {
            Print($"[L6BT] Tick index check chartIdx={_host.LastClosedBarProcessedThisTick} " +
                  $"stateLastBar={_host.ChartShellLastBarIndex} latestPivotBar={_host.GetLatestPivotBarIndex()}");
            _indexCheckBarsRemaining--;
        }

        foreach (var ev in events)
            _slotAudit.RecordFire(ev.SlotIndex);

        // R3–R6 deep condition sample — every 500 bars when R3-6 configured but never fired.
        if (_r3r6DebugBarsRemaining > 0 && _onBarCount % 500 == 0)
        {
            var r3r6configured = !string.IsNullOrWhiteSpace(CompoundRule3)
                              || !string.IsNullOrWhiteSpace(CompoundRule4)
                              || !string.IsNullOrWhiteSpace(CompoundRule5)
                              || !string.IsNullOrWhiteSpace(CompoundRule6);

            if (r3r6configured)
            {
                // Check if any of R3–R6 have ever fired
                bool anyFired = false;
                for (var s = 2; s < 6; s++)
                {
                    var (f, _, _, _, _) = _slotAudit.Get(s);
                    if (f > 0) { anyFired = true; break; }
                }

                if (!anyFired)
                {
                    _r3r6DebugBarsRemaining--;
                    DumpR3R6ConditionDebug();
                }
            }
        }

        // 3) session gate — outside window: flatten all + block. Inside window but after entry cutoff (step 6): block new entries only.
        var inWindow = !UseSessionWindow || InWindow(Server.TimeInUtc.Hour, _startUtcHour, _stopUtcHour);

        if (EnableLog && (_onBarCount % 500 == 0 || events.Count > 0))
            Log($"[L6BT] bar={Bars.Count} events={events.Count} inWindow={inWindow} onBar#{_onBarCount}");

        if (events.Count > 0 && EnableLog)
        {
            foreach (var ev in events)
                Log($"[L6BT] FIRE R{ev.SlotIndex + 1} {ev.Direction} bar={ev.ChartBarIndex} time={ev.BarOpenTime:s} close={ev.Price}");
        }

        if (!inWindow)
        {
            if (events.Count > 0 && _csvLog != null)
            {
                var ctx = BuildAnalyticsContext();
                foreach (var ev in events)
                {
                    _csvLog.LogSkip(ctx, ev.SlotIndex,
                        ev.Direction == SignalDirection.Buy ? "buy" : ev.Direction == SignalDirection.Sell ? "sell" : "",
                        Loop6SkipReasonCodes.OutsideTradingHours,
                        $"outside session window utcHour={Server.TimeInUtc.Hour} vnStart={StartHourVn} vnStop={StopHourVn}",
                        chartBar: ev.ChartBarIndex);
                }
            }

            if (EnableTrading && _exec.HasAnyLive())
            {
                var sessionLabels = _exec.OurPositions()
                    .Select(p => p.Label)
                    .Where(l => l != null)
                    .Cast<string>()
                    .ToArray();
                foreach (var label in sessionLabels)
                {
                    _closeReasonAudit.RecordManualClose(label, "session-stop");
                    _execAudit.PrepareClose(label, "session-stop");
                }
                _exec.CloseAndCancelAll("session-stop");
                AuditManualCloses(sessionLabels, "session-stop");
                _book.Clear();
            }
            return;
        }

        // 4) pending invalidation rules (pending only — never close filled positions).
        if (EnableTrading && (EnableCancelOnPostBPushObstacle || EnableCancelHlM15BcAbRatio || EnableCancelOnNearDZone || EnableCancelOnNearBThenCTp || EnableCancelOnSwingCBrokenWaitD || EnableCancelOnStalePendingSwing))
        {
            if (EnableCancelOnPostBPushObstacle)
                SyncObstacleTfStatesForCancel();
            EvaluatePendingInvalidations();
        }

        // 4a) entry cutoff: cancel all pending orders (runs every bar during cutoff, idempotent).
        if (EnableTrading && IsEntryBlockedByBangkokCutoff())
            CancelAllPendingOnCutoff();

        // 5) exit — swing B broken: cancel pending or move open-position TP to entry.
        if (EnableTrading)
            HandleSwingBrokenExits();

        // 5a) virtual xR bar-close exit (after B-broken, before swing-C TP).
        if (EnableTrading && _virtualXrCfg.Enabled)
            EvaluateVirtualXrBarCloseExits();

        // 5b) late-bind TP to confirmed swing-C edge (RR default or no-TP mode).
        if (EnableTrading && _swingCTpCfg.WantsLiveTpUpdate && !_virtualXrCfg.PureVirtualMode)
            UpdateSwingCTakeProfits();

        if (EnableTrading)
            AuditLiveExecutions();

        UpdateTradeExcursionsOnBar();

        // 6) new entries from fires (and any deferred split fires waiting for swing-C).
        if (events.Count == 0 && _deferredSplitFires.Count == 0)
            return;

        if (IsEntryBlockedByBangkokCutoff())
        {
            if (_csvLog != null)
            {
                var ctx = BuildAnalyticsContext();
                var bkk = Loop6BangkokSession.GetBangkokTime(Server.TimeInUtc);
                foreach (var ev in events)
                {
                    _csvLog.LogSkip(ctx, ev.SlotIndex,
                        ev.Direction == SignalDirection.Buy ? "buy" : ev.Direction == SignalDirection.Sell ? "sell" : "",
                        Loop6SkipReasonCodes.EntryCutoffBangkok,
                        $"entry cutoff Bangkok hour>={EntryCutoffHourBangkok} bkkHour={bkk.Hour} sessionStopVn={StopHourVn}",
                        chartBar: ev.ChartBarIndex);
                }
            }

            if (EnableLog)
                Log($"[L6BT] ENTRY CUTOFF BKK hour={Loop6BangkokSession.GetBangkokTime(Server.TimeInUtc).Hour} — skip {events.Count} fire(s); open kept; pending cancelled");

            // Cutoff persists for the rest of the session — drop every deferred fire.
            DropAllDeferredOnCutoff();
            return;
        }

        if (EnableEntrySlZoneGate)
            SyncGateTfStatesForGate();

        if (_atrSlCfg.UseAtrAdjustedSlWidthMultiplier)
            SyncAtrAdjustTfState();

        if (EnableH4SlMode)
            SyncH4SlTfState();

        var cfg = BuildMapperConfig();
        var entryEvents = CompoundFirePriorityFilter.Apply(
            events,
            PreferM15CompoundRules,
            PreferM15CompoundRules && EnableLog ? Log : null);

        foreach (var ev in entryEvents)
        {
            if (ev.Direction == SignalDirection.Buy && !AlertOnBuy)
            {
                LogCsvSkip(ev.SlotIndex, "buy", Loop6SkipReasonCodes.SignalInvalid, "AlertOnBuy=false");
                continue;
            }
            if (ev.Direction == SignalDirection.Sell && !AlertOnSell)
            {
                LogCsvSkip(ev.SlotIndex, "sell", Loop6SkipReasonCodes.SignalInvalid, "AlertOnSell=false");
                continue;
            }

            // ── Friday Flat Guard entry block ────────────────────────────────
            if (IsBlockedByFridayFlat(ev.SlotIndex,
                ev.Direction == SignalDirection.Buy ? "buy" : "sell",
                ev.ChartBarIndex))
                continue;

            // ── News filter entry block ──────────────────────────────────────
            if (IsBlockedByNews(ev.SlotIndex,
                ev.Direction == SignalDirection.Buy ? "buy" : "sell",
                rawEntry: 0, rawSl: 0, rawTp: 0, slStructurePips: 0,
                chartBar: ev.ChartBarIndex))
                continue;

            // SetupAtCondFire: use cond-bar OHLC for D-fire and pinned B from cond bar N.
            var isSacf = CompoundWindowMode == CompoundWindowMode.SetupAtCondFire
                         && !string.IsNullOrEmpty(ev.SourceEventKey);
            var setupBar = ev.SetupBarIndex >= 0 ? ev.SetupBarIndex : ev.ChartBarIndex;
            TryGetChartBarOhlc(setupBar, out var fireHigh, out var fireLow);

            SwingBResult? pinnedB = null;
            DFireAtFireCache? sacfDFire = null;
            if (isSacf)
            {
                if (!_pinnedSwingBByKey.TryGetValue(ev.SourceEventKey, out pinnedB))
                {
                    // B was not resolved at cond-fire bar (e.g. no B existed then) → skip this fire.
                    var skipCode = TradePlanSkipReasonMapper.Map("no pinned B (setup-at-cond-fire)");
                    if (PrintCompoundFires)
                    {
                        Log($"[L6BT] SACF-SKIP R{ev.SlotIndex + 1} {ev.Direction} bar={ev.ChartBarIndex} " +
                            $"sourceBar={ev.EventBarIndex} reason=no-pinned-B");
                        Log($"TRADE_PLAN_SKIP rule=R{ev.SlotIndex + 1} " +
                            $"dir={CompoundSourceEventKey.FormatDirection(ev.Direction)} " +
                            $"sourceTf={CompoundSourceEventKey.FormatSourceTf(ev.SourceTfToken)} " +
                            $"sourceBar={ev.EventBarIndex} reason={skipCode}");
                    }
                    LogCsvSkip(ev.SlotIndex, ev.Direction == SignalDirection.Buy ? "buy" : "sell",
                        Loop6SkipReasonCodes.Other, "no-pinned-B sacf", chartBar: ev.ChartBarIndex);
                    DrawFireChartSkip(in ev, setupBar, "no pinned B (SACF)");
                    PurgePinnedSetupKeys(ev.SourceEventKey);
                    continue;
                }
                if (_pinnedDFireByKey.TryGetValue(ev.SourceEventKey, out var pd))
                    sacfDFire = pd;
            }

            // immediate fire: SlEvalTime = fire bar open time (conservative leak guard).
            var evCfg = ApplyH4SlConfig(in cfg, ev.Direction == SignalDirection.Buy, ev.BarOpenTime);
            var plans = CompoundFireTradeMapper.TryBuildPlans(
                in ev, _host.State, in evCfg, out var reason, ResolveAtrSeriesForSl,
                pinnedB: pinnedB,
                cachedDFireAtFire: sacfDFire?.DFire,
                cachedDPrimeAtFire: sacfDFire?.DPrimeFire,
                fireBarHigh: fireHigh, fireBarLow: fireLow);

            if (plans.Count == 0)
            {
                // SplitTpAtCAndD + fallbackRR=false + no confirmed swing-C yet → park the fire
                // and retry every bar until C confirms (or B is broken / cutoff / B moves).
                // Confirmed C at fire but gates failed (RR, obstacle, TP side, …) → one-shot SKIP.
                if (ShouldDeferForSwingC(in ev, pinnedB, in evCfg, reason)
                    && _swingCTpCfg.AppliesToSlot(ev.SlotIndex))
                {
                    EnqueueDeferredSplit(in ev, reason, sacfPinnedB: pinnedB, sacfDFire: sacfDFire);
                    LogSwingCPivotAudit(in ev, pinnedB, in evCfg, "fire-defer", chartBar: ev.ChartBarIndex, buildReason: reason);
                    DrawFireChartWaitC(in ev, setupBar, reason, pinnedB?.PivotBar ?? -1, pinnedB);
                    if (isSacf) PurgePinnedSetupKeys(ev.SourceEventKey);
                    continue;
                }

                if (PrintCompoundFires)
                {
                    var skipCode = TradePlanSkipReasonMapper.Map(reason);
                    Log($"[L6BT] FIRE R{ev.SlotIndex + 1} {ev.Direction} bar={ev.ChartBarIndex} -> SKIP ({reason})");
                    Log(
                        $"TRADE_PLAN_SKIP rule=R{ev.SlotIndex + 1} dir={CompoundSourceEventKey.FormatDirection(ev.Direction)} " +
                        $"sourceTf={CompoundSourceEventKey.FormatSourceTf(ev.SourceTfToken)} " +
                        $"sourceBar={ev.EventBarIndex} reason={skipCode}");
                }
                if (_swingCTpCfg.AppliesToSlot(ev.SlotIndex)
                    && _swingCTpCfg.TpMode == SwingCTpMode.SplitTpAtCAndD)
                    LogSwingCPivotAudit(in ev, pinnedB, in evCfg, "fire-skip", chartBar: ev.ChartBarIndex, buildReason: reason);
                var (code, detail) = Loop6SkipReasonCodes.Map(reason, $"R{ev.SlotIndex + 1}");
                LogCsvSkip(ev.SlotIndex,
                    ev.Direction == SignalDirection.Buy ? "buy" : "sell",
                    code, detail, chartBar: ev.ChartBarIndex);
                DrawFireChartSkip(in ev, setupBar, reason, pinnedB);
                if (isSacf) PurgePinnedSetupKeys(ev.SourceEventKey);
                continue;
            }

            DrawFireChartFire(in ev, setupBar, pinnedB);
            if (isSacf) PurgePinnedSetupKeys(ev.SourceEventKey);
            ExecuteResolvedPlans(in ev, plans, in cfg, "FIRE");
        }

        // Process any fires that were previously deferred waiting for swing-C confirmation.
        ProcessDeferredSplitFires(in cfg);

        if (EnableH4SlDebugPanel)
            UpdateH4SlDebugPanel();
    }

    /// <summary>
    /// Executes the resolved plans for a fire event (one plan for single-leg modes, two for split).
    /// Shared by the main fire loop and the deferred-split processor so per-plan diagnostics,
    /// gates and order placement stay in sync.
    /// </summary>
    void ExecuteResolvedPlans(
        in CompoundFireEvent ev,
        IReadOnlyList<TradePlan> plans,
        in TradeMapperConfig cfg,
        string logContextTag)
    {
        if (plans.Count == 0)
            return;

        _slotAudit.RecordPlan(ev.SlotIndex);

        var totalRiskUsd = AccountBalanceFtmo * RiskPercent / 100.0;
        var isSplit = plans.Count > 1;
        var placedLabels = new List<string>(plans.Count);

        for (var pi = 0; pi < plans.Count; pi++)
        {
            var plan = plans[pi];
            var legRiskUsd = isSplit ? totalRiskUsd * 0.5 : totalRiskUsd;
            var lotSizingRiskPips = plan.StopLossPips;

            if (_atrSlCfg.UseAtrAdjustedSlWidthMultiplier && plan.SlAdjust is { } slAdj)
            {
                Print(SlDynMultLog.FormatPlanLog(
                    in plan, ev.SlotIndex, plan.KeyWidth, in slAdj,
                    plan.SlBaseMult, plan.SlAdjustFactor, plan.SlRiskDistance));
            }

            if (plan.TakeProfitSource == TakeProfitSource.SwingCEdge && plan.SwingCPivotBar > 0)
            {
                var swingC = new SwingCEdgeResult
                {
                    PivotIndex = plan.SwingCPivotIndex,
                    PivotBar = plan.SwingCPivotBar,
                    PivotType = plan.IsBuy ? SwingBResolver.TypeHigh : SwingBResolver.TypeLow,
                    TakeProfit = plan.TakeProfit,
                    EdgeTop = plan.SwingCEdgeTop,
                    EdgeBottom = plan.SwingCEdgeBottom,
                };
                Print(SwingCEdgeTakeProfitResolver.FormatPlanLog(in plan, ev.SlotIndex, swingC));
            }

            Print(TradePlanRrLog.FormatPlanRrCheck(in plan, ev.SlotIndex, cfg.RewardRisk));
            Print(SpreadTelemetryLog.FormatPlan(in plan, ev.SlotIndex, Symbol.Bid, Symbol.Ask));

            if (PrintCompoundFires)
                Log($"[L6BT] {logContextTag} R{ev.SlotIndex + 1} {ev.Direction} bar={ev.ChartBarIndex} -> PLAN {plan.Reason}");

            if (pi == 0 && plan.Reason.Contains("[H4SL=", StringComparison.Ordinal))
                CaptureH4SlResolveNote(plan.Reason, ev.Direction);

            if (pi == 0 && EnableEntrySlZoneGate && _gateCfg.AppliesToSlot(ev.SlotIndex))
            {
                var gate = EntrySlZoneGate.Evaluate(plan, _host, in _gateCfg);
                Print(gate.Passed
                    ? EntrySlZoneGate.FormatPassLog(in plan, ev.SlotIndex, in gate)
                    : EntrySlZoneGate.FormatSkipLog(in plan, ev.SlotIndex, in gate));

                // Draw entry–SL support zone on chart (risk band outline + matched zone fill).
                _fireChartLabels?.DrawEntrySlZone(
                    plan.Label, ev.ChartBarIndex,
                    gate.RiskLow, gate.RiskHigh,
                    gate.ZoneLow, gate.ZoneHigh,
                    gate.Passed, plan.IsBuy);

                if (gate.Passed)
                    _slotAudit.RecordGatePass(ev.SlotIndex);
                else
                {
                    _slotAudit.RecordGateSkip(ev.SlotIndex);
                    LogCsvSkip(ev.SlotIndex, plan.IsBuy ? "buy" : "sell",
                        Loop6SkipReasonCodes.Other,
                        $"ZONE_GATE: {gate.SkipReason}",
                        plan.RawEntryBid, plan.RawSlBid, plan.RawTpBid,
                        plan.SlPipsBeforeFloor, ev.ChartBarIndex);
                    break;
                }
            }

            // Draw near-D cancel zone + buffer band when placing the order (first leg only).
            if (pi == 0 && plan.NearDCancelZoneLow > 0 && plan.NearDCancelZoneHigh > 0)
                _fireChartLabels?.DrawNearDCancelZoneDebug(
                    CompoundFireChartDebugLabels.NearDCancelZoneScopeKey(in ev),
                    ev.ChartBarIndex,
                    plan.NearDCancelZoneLow,
                    plan.NearDCancelZoneHigh,
                    plan.IsBuy,
                    plan.NearDCancelZoneIsOb,
                    plan.NearDCancelZoneTfToken);

            if (pi == 0 && EnableSpreadGate && MaxSpreadGate > 0)
            {
                var spreadSnap = ResolveSpreadSnapshot();
                var isGoldSymbol = SpreadResolver.UsesPriceSpreadOverride(SymbolName, _sizingProfile.AssetType);
                var spreadValue = isGoldSymbol ? spreadSnap.Price : spreadSnap.Pips;
                var unit = isGoldSymbol ? "price" : "pip";
                if (spreadValue > MaxSpreadGate)
                {
                    Print($"[L6BT] SPREAD_GATE skip {plan.Label}: {unit}={spreadValue:0.####} > max={MaxSpreadGate:0.####}");
                    LogCsvSkip(ev.SlotIndex, plan.IsBuy ? "buy" : "sell",
                        Loop6SkipReasonCodes.SpreadAboveMax,
                        $"spread {unit}={spreadValue:0.####} > max={MaxSpreadGate:0.####}",
                        plan.RawEntryBid, plan.RawSlBid, plan.RawTpBid,
                        plan.SlPipsBeforeFloor, ev.ChartBarIndex);
                    break;
                }
            }

            if (_csvLog != null)
            {
                _csvLog.LogPlan(in plan, in ev, BuildAnalyticsContext(),
                    EnableTrading ? "PLAN" : "DRY_RUN",
                    plannedRiskUsd: legRiskUsd,
                    lotSizingRiskPips: lotSizingRiskPips);
            }

            if (!EnableTrading)
            {
                placedLabels.Add(plan.Label);
                continue;
            }

            var riskPctForLeg = isSplit ? RiskPercent * 0.5 : RiskPercent;
            if (ExecutePlan(plan, ev.SlotIndex, in ev, legRiskUsd, lotSizingRiskPips, riskPctForLeg,
                    skipParallelGate: isSplit && pi > 0,
                    skipFlip: isSplit && pi > 0))
            {
                _slotAudit.RecordOrder(ev.SlotIndex);
                placedLabels.Add(plan.Label);
            }
        }

        if (EnableH4SlDebugPanel && placedLabels.Count > 0)
        {
            _lastH4SlPlanDebug = H4SlPlanDebugSnapshot.FromPlans(plans, in ev);
            _h4SlPlanOverlay?.Register(placedLabels, in _lastH4SlPlanDebug, freezeImmediately: !EnableTrading);
            UpdateH4SlDebugPanel();
        }

        _fireChartLabels?.TryAnnotatePlanMetrics(in ev, plans);
    }

    bool ShouldDeferForSwingC(
        in CompoundFireEvent ev,
        SwingBResult? pinnedB,
        in TradeMapperConfig cfg,
        string reason)
    {
        if (_swingCTpCfg.TpMode != SwingCTpMode.SplitTpAtCAndD
            || _swingCTpCfg.SwingCEdgeTpFallbackToRR
            || string.IsNullOrEmpty(reason)
            || !reason.Contains("no confirmed swing C", StringComparison.OrdinalIgnoreCase))
            return false;

        var bForC = pinnedB ?? CompoundFireTradeMapper.ResolveSwingB(
            _host.State, ev.Direction == SignalDirection.Buy, ev.SlotIndex, in cfg, out _);

        return SwingCWaitDiagnostic.IsAwaitingSwingCConfirmation(
            _host.State, bForC, ev.Direction, ev.SlotIndex, in cfg);
    }

    void LogSwingCPivotAudit(
        in CompoundFireEvent ev,
        SwingBResult? pinnedB,
        in TradeMapperConfig cfg,
        string phase,
        int chartBar = -1,
        int? pickedCBar = null,
        string? buildReason = null)
    {
        if (!EnableLog && !PrintCompoundFires)
            return;
        if (!_swingCTpCfg.AppliesToSlot(ev.SlotIndex))
            return;

        var curBar = chartBar >= 0 ? chartBar : Bars.Count - 1;
        var bForC = pinnedB ?? CompoundFireTradeMapper.ResolveSwingB(
            _host.State, ev.Direction == SignalDirection.Buy, ev.SlotIndex, in cfg, out _);
        Log(SwingCPivotAudit.FormatLogBlock(
            _host.State, bForC, ev.Direction, ev.SlotIndex, in cfg,
            ev.SlotIndex, ev.ChartBarIndex, curBar, phase, pickedCBar, buildReason));
    }

    void DrawFireChartFire(in CompoundFireEvent ev, int barIndex, SwingBResult? pinnedB = null)
    {
        if (_fireChartLabels == null)
            return;
        _fireChartLabels.DrawFire(in ev, _host.FormatRuleLegSnapshot(ev.SlotIndex), barIndex);
        TryDrawEntryOscillationZone(in ev, barIndex, pinnedB);
    }

    void DrawFireChartWaitC(in CompoundFireEvent ev, int barIndex, string reason, int swingBPivotBar, SwingBResult? pinnedB = null)
    {
        if (_fireChartLabels == null)
            return;

        var cfg = BuildMapperConfig();
        var diag = SwingCWaitDiagnostic.Describe(
            _host.State, pinnedB, ev.Direction, ev.SlotIndex, in cfg, reason);

        _fireChartLabels.DrawWaitC(
            in ev,
            _host.FormatRuleLegSnapshot(ev.SlotIndex),
            barIndex,
            swingBPivotBar,
            reason,
            statusLine: diag.ShortLine,
            detailLine: diag.DetailLine);

        SyncFireChartSwingCCandidates(in ev, pinnedB, watchBar: barIndex);
        TryDrawEntryOscillationZone(in ev, barIndex, pinnedB);
        TryDrawNearDCancelZoneForDeferred(in ev, barIndex);
    }

    void TryDrawNearDCancelZoneForDeferred(in CompoundFireEvent ev, int fireBarIndex)
    {
        if (_fireChartLabels == null)
            return;

        foreach (var item in _deferredSplitFires.Items)
        {
            if (item.Ev.SlotIndex != ev.SlotIndex || item.Ev.ChartBarIndex != ev.ChartBarIndex)
                continue;
            if (item.NearDCancelZoneLow <= 0 || item.NearDCancelZoneHigh <= item.NearDCancelZoneLow)
                return;

            _fireChartLabels.DrawNearDCancelZoneDebug(
                CompoundFireChartDebugLabels.NearDCancelZoneScopeKey(in ev),
                fireBarIndex,
                item.NearDCancelZoneLow,
                item.NearDCancelZoneHigh,
                ev.Direction == SignalDirection.Buy,
                item.NearDCancelZoneIsOb,
                item.NearDCancelZoneTfToken);
            return;
        }
    }

    void UpdateFireChartWaitC(in CompoundFireEvent ev, in DeferredSplitFire item, string? buildReason)
    {
        if (_fireChartLabels == null)
            return;

        var pinnedB = BuildPinnedSwingBFromDeferred(item);
        var cfg = BuildMapperConfig();
        var diag = SwingCWaitDiagnostic.Describe(
            _host.State, pinnedB, ev.Direction, ev.SlotIndex, in cfg, buildReason);
        var heldBars = Bars.Count - 1 - item.DeferredAtBar;

        _fireChartLabels.UpdateWaitC(
            in ev,
            item.DeferredAtBar,
            heldBars,
            item.OriginalSwingBPivotBar,
            diag.ShortLine,
            diag.DetailLine);

        SyncFireChartSwingCCandidates(in ev, pinnedB, watchBar: Bars.Count - 1);

        if (item.SwingBKeyBottom > 0 && item.SwingBKeyTop > item.SwingBKeyBottom)
        {
            TryDrawEntryOscillationZone(in ev, item.DeferredAtBar, pinnedB);
        }

        TryDrawNearDCancelZoneForDeferred(in ev, item.DeferredAtBar);
    }

    void SyncFireChartSwingCCandidates(in CompoundFireEvent ev, SwingBResult? pinnedB, int watchBar)
    {
        if (_fireChartLabels == null || pinnedB is null)
            return;

        var cfg = BuildMapperConfig();
        var rows = SwingCPivotAudit.CollectAfterB(
            _host.State, pinnedB, ev.Direction, ev.SlotIndex, in cfg);
        _fireChartLabels.SyncSwingCCandidateLabels(
            in ev, ev.Direction == SignalDirection.Buy, rows, watchBar);
    }

    static SwingCPivotAudit.Row? FindSwingCRow(IReadOnlyList<SwingCPivotAudit.Row> rows, int pivotBar)
    {
        foreach (var row in rows)
        {
            if (row.Bar == pivotBar)
                return row;
        }

        return null;
    }

    void DrawFireChartSkip(in CompoundFireEvent ev, int barIndex, string reason, SwingBResult? pinnedB = null)
    {
        if (_fireChartLabels == null)
            return;
        _fireChartLabels.DrawSkip(in ev, _host.FormatRuleLegSnapshot(ev.SlotIndex), barIndex, reason);
        TryDrawEntryOscillationZone(in ev, barIndex, pinnedB);
    }

    /// <summary>Draw swing-B keylevel band where entry may sit / move (RR adjust). Resolves B when not pinned.</summary>
    void TryDrawEntryOscillationZone(in CompoundFireEvent ev, int barIndex, SwingBResult? pinnedB = null)
    {
        if (_fireChartLabels == null)
            return;

        var b = pinnedB;
        if (b is null)
        {
            var cfg = BuildMapperConfig();
            var isBuy = ev.Direction == SignalDirection.Buy;
            b = CompoundFireTradeMapper.ResolveSwingB(_host.State, isBuy, ev.SlotIndex, in cfg, out _);
        }

        if (b is not { KeyBottom: > 0, KeyTop: > 0 } || b.KeyTop <= b.KeyBottom)
            return;

        _fireChartLabels.DrawEntryOscillationZone(
            in ev, barIndex, b.KeyBottom, b.KeyTop, ev.Direction == SignalDirection.Buy);
    }

    void DrawFireChartCancel(in PendingOrderContext ctx, string reason)
    {
        if (_fireChartLabels == null || ctx.FireChartBarIndex < 0)
            return;
        // Build the original FIRE label key by convention so we can dim it.
        var fireLabelKey = $"{CompoundFireChartDebugLabels.ObjectPrefix}FIRE|R{ctx.RuleSlot}|c{ctx.FireChartBarIndex}|e{ctx.SwingBPivotBar}";
        _fireChartLabels.DrawCancel(fireLabelKey, ctx.FireChartBarIndex, Bars.Count - 1, ctx.IsBuy, reason);
    }

    void EnqueueDeferredSplit(in CompoundFireEvent ev, string reason,
        SwingBResult? sacfPinnedB = null, DFireAtFireCache? sacfDFire = null)
    {
        var isBuy = ev.Direction == SignalDirection.Buy;
        var cfg = BuildMapperConfig();

        // SetupAtCondFire: B/D already resolved at cond-fire bar N — reuse them.
        SwingBResult? b;
        DFireAtFireCache dFireCache;
        if (sacfPinnedB is not null)
        {
            b = sacfPinnedB;
            dFireCache = sacfDFire ?? default;
        }
        else
        {
            b = CompoundFireTradeMapper.ResolveSwingB(_host.State, isBuy, ev.SlotIndex, in cfg, out _);
            var setupBar = ev.SetupBarIndex >= 0 ? ev.SetupBarIndex : ev.ChartBarIndex;
            TryGetChartBarOhlc(setupBar, out var fireHigh, out var fireLow);
            dFireCache = b is not null
                ? CompoundFireTradeMapper.ResolveDFireAtFire(_host.State, in cfg, in ev, b, fireHigh, fireLow)
                : default;
        }

        var bPivot = b?.PivotBar ?? -1;
        var usesM5SwingC = b is not null && R1R2M5Structure.UsesM5SwingC(ev.SlotIndex, in cfg);
        var swingBFromM5 = b?.Source == SwingBSource.M5AnchoredFallback;

        var deferredItem = new DeferredSplitFire
        {
            Ev = ev,
            OriginalSwingBPivotBar = bPivot,
            OriginalSwingBPivotIndex = b?.PivotIndex ?? -1,
            SwingBKeyTop = b?.KeyTop ?? 0,
            SwingBKeyBottom = b?.KeyBottom ?? 0,
            SwingBTfToken = Loop6ChartTimeframeToken.FromBarsTimeFrame(TimeFrame),
            UsesM5SwingC = usesM5SwingC,
            SwingBFromM5Anchor = swingBFromM5,
            StructureSwingBBar = swingBFromM5
                ? b!.StructurePivotBar
                : usesM5SwingC && b is not null
                    ? R1R2M5Structure.ResolveM5SwingBBarForC(in b, in cfg)
                    : 0,
            OriginalSwingBType = b?.Type ?? 0,
            OriginalSwingBSource = b?.Source ?? SwingBSource.Active,
            DeferredAtBar = ev.ChartBarIndex,
            DeferredAtTimeUtc = Server.TimeInUtc,
            CachedDFire = dFireCache.DFire,
            CachedDPrimeFire = dFireCache.DPrimeFire,
            FireBarDReferencePrice = dFireCache.ReferencePrice,
            NearDCancelZoneHigh = dFireCache.NearDCancelZone?.EdgeTop ?? 0,
            NearDCancelZoneLow = dFireCache.NearDCancelZone?.EdgeBottom ?? 0,
            NearDCancelZoneIsOb = dFireCache.NearDCancelZone?.IsOb ?? false,
            NearDCancelZoneTfToken = dFireCache.NearDCancelZone?.TfToken ?? "",
        };

        Action<string>? tierLog = PreferM15CompoundRules && EnableLog ? (Action<string>)Log : null;
        var enqueueResult = _deferredSplitFires.TryEnqueueWithTierFilter(
            deferredItem, PreferM15CompoundRules, tierLog);

        if (enqueueResult == DeferredEnqueueResult.DuplicateSlotAndB)
        {
            if (EnableLog)
                Log($"[L6BT] DEFERRED-SPLIT dedup R{ev.SlotIndex + 1} {ev.Direction} fireBar={ev.ChartBarIndex} B={bPivot} (already queued)");
            return;
        }

        if (enqueueResult == DeferredEnqueueResult.SkippedLowerTier)
            return;

        if (EnableLog || PrintCompoundFires)
            Log($"[L6BT] DEFERRED-SPLIT enqueue R{ev.SlotIndex + 1} {ev.Direction} fireBar={ev.ChartBarIndex} B={bPivot} (waiting for swing-C; queueSize={_deferredSplitFires.Count}) why=\"{reason}\"");
    }

    void ProcessDeferredSplitFires(in TradeMapperConfig cfg)
    {
        if (_deferredSplitFires.Count == 0)
            return;

        var i = 0;
        while (i < _deferredSplitFires.Count)
        {
            var item = _deferredSplitFires[i];
            var ev = item.Ev;
            var dirStr = ev.Direction == SignalDirection.Buy ? "buy" : "sell";

            // ─ TTL 1: B confirmed broken before C confirmed → drop ──────────────
            //   Mirrors HandleSwingBrokenExits's pending cancel for a real pending order on B.
            if (item.OriginalSwingBPivotBar > 0
                && R1R2M5Structure.IsSwingBBroken(
                    item.SwingBFromM5Anchor, item.StructureSwingBBar, item.OriginalSwingBPivotBar,
                    _host.State, ResolveM5FallbackState()))
            {
                DropDeferredAt(i, in ev, "b-broken",
                    Loop6SkipReasonCodes.DeferredSplit,
                    $"deferred-split drop b-broken B={item.OriginalSwingBPivotBar} heldBars={Bars.Count - 1 - item.DeferredAtBar}");
                continue;
            }

            // ─ TTL 2: Friday-flat / news entry guards block this slot → drop ────
            // (These helpers log their own skip rows; we just remove the entry.)
            if (IsBlockedByFridayFlat(ev.SlotIndex, dirStr, ev.ChartBarIndex))
            {
                if (EnableLog)
                    Log($"[L6BT] DEFERRED-SPLIT drop R{ev.SlotIndex + 1} fireBar={ev.ChartBarIndex} reason=friday-flat");
                _fireChartLabels?.ClearSwingCCandidateLabels(in ev);
                _deferredSplitFires.RemoveAt(i);
                continue;
            }

            if (IsBlockedByNews(ev.SlotIndex, dirStr,
                    rawEntry: 0, rawSl: 0, rawTp: 0, slStructurePips: 0,
                    chartBar: ev.ChartBarIndex))
            {
                if (EnableLog)
                    Log($"[L6BT] DEFERRED-SPLIT drop R{ev.SlotIndex + 1} fireBar={ev.ChartBarIndex} reason=news-block");
                _fireChartLabels?.ClearSwingCCandidateLabels(in ev);
                _deferredSplitFires.RemoveAt(i);
                continue;
            }

            // ─ TTL 3: pending-invalidation rules (treat deferred fire as if a phantom
            //   pending order A were sitting at B). Whenever order A would be cancelled
            //   live, the deferred fire expires too. ─────────────────────────────
            if (TryDeferredPendingInvalidation(i, in ev, item, in cfg))
                continue;

            // ─ Re-run the mapper now that more bars have closed. ────────────────
            // B validity was established at fire time — do not re-resolve from current
            // pivot flags (B may have been promoted to MAIN_C/FAKE while waiting for C).
            var pinnedB = BuildPinnedSwingBFromDeferred(item);
            TryGetChartBarOhlc(item.DeferredAtBar, out var fireHigh, out var fireLow);
            // deferred split: SlEvalTime = current (just-closed) bar — bot may know data up to now.
            var evCfg = ApplyH4SlConfig(in cfg, ev.Direction == SignalDirection.Buy, ResolveSlEvalTimeNow());
            var plans = CompoundFireTradeMapper.TryBuildPlans(
                in ev, _host.State, in evCfg, out var reason, ResolveAtrSeriesForSl, pinnedB,
                fireBarHigh: fireHigh, fireBarLow: fireLow,
                cachedDFireAtFire: item.CachedDFire,
                cachedDPrimeAtFire: item.CachedDPrimeFire);

            if (plans.Count == 2)
            {
                // B may have flipped to a different pivot since defer time — that is a
                // different setup, abandon the stale fire to avoid placing the wrong plan.
                if (item.OriginalSwingBPivotBar > 0
                    && plans[0].SwingBPivotBar != item.OriginalSwingBPivotBar)
                {
                    DropDeferredAt(i, in ev, "b-moved",
                        Loop6SkipReasonCodes.DeferredSplit,
                        $"deferred-split drop b-moved was={item.OriginalSwingBPivotBar} now={plans[0].SwingBPivotBar}");
                    continue;
                }

                if (!_deferredSplitFires.CanExecuteAtIndex(i, PreferM15CompoundRules))
                {
                    DropDeferredAt(i, in ev, "priority-tier",
                        Loop6SkipReasonCodes.DeferredSplit,
                        $"deferred-split drop priority-tier B={item.OriginalSwingBPivotBar} tier {CompoundFirePriorityFilter.GetPriorityTier(ev.SlotIndex)} < max same B/direction");
                    continue;
                }

                var heldBars = Bars.Count - 1 - item.DeferredAtBar;
                if (EnableLog || PrintCompoundFires)
                    Log($"[L6BT] DEFERRED-SPLIT ready R{ev.SlotIndex + 1} {ev.Direction} fireBar={ev.ChartBarIndex} B={item.OriginalSwingBPivotBar} heldBars={heldBars} -> placing 2 legs");

                var pickedBar = plans[0].SwingCPivotBar;
                var cRows = SwingCPivotAudit.CollectAfterB(
                    _host.State, pinnedB, ev.Direction, ev.SlotIndex, in cfg);

                LogSwingCPivotAudit(
                    in ev, pinnedB, in cfg, "defer-exec",
                    chartBar: Bars.Count - 1,
                    pickedCBar: pickedBar,
                    buildReason: reason);

                _fireChartLabels?.MarkCReady(
                    in ev, item.DeferredAtBar, pickedBar, heldBars, plans);
                _fireChartLabels?.MarkSwingCPickedAtPivot(
                    in ev,
                    item.DeferredAtBar,
                    pickedBar,
                    heldBars,
                    ev.Direction == SignalDirection.Buy,
                    FindSwingCRow(cRows, pickedBar));

                if (EnableCompoundFireDebugLog)
                {
                    var curBar = Bars.Count - 1;
                    var curClose = Bars.ClosePrices[curBar];
                    var table = _host.DescribeHtfTouchProbeTableAtChartBar(curBar, curClose);
                    if (table != null)
                    {
                        Log($"[FireDbg] HTF-TOUCH-TABLE @deferred-split chart={curBar} fireBar={ev.ChartBarIndex}");
                        Log(table);
                    }
                }

                ExecuteResolvedPlans(in ev, plans, in cfg, "DEFERRED-SPLIT EXEC");
                var executedTier = CompoundFirePriorityFilter.GetPriorityTier(ev.SlotIndex);
                _deferredSplitFires.EvictLowerTierSameSetup(
                    item.OriginalSwingBPivotBar,
                    ev.Direction,
                    executedTier,
                    PreferM15CompoundRules,
                    PreferM15CompoundRules && EnableLog ? Log : null);
                _deferredSplitFires.RemoveAt(i);
                continue;
            }

            if (plans.Count == 1)
            {
                // Should not occur (we only defer when fallbackRR=false, which never returns a
                // single RR fallback plan). Defensive drop so the queue never gets stuck.
                DropDeferredAt(i, in ev, "unexpected-single",
                    Loop6SkipReasonCodes.DeferredSplit,
                    $"deferred-split unexpected single plan: {reason}");
                continue;
            }

            // plans.Count == 0
            var pinnedBForC = BuildPinnedSwingBFromDeferred(item);
            if (SwingCWaitDiagnostic.IsAwaitingSwingCConfirmation(
                    _host.State, pinnedBForC, ev.Direction, ev.SlotIndex, in cfg))
            {
                // Still no confirmed swing-C — keep waiting.
                if (EnableCompoundFireDebugLog)
                    LogSwingCPivotAudit(
                        in ev, pinnedBForC, in cfg, "defer-wait",
                        chartBar: Bars.Count - 1,
                        buildReason: reason);
                UpdateFireChartWaitC(in ev, item, reason);
                i++;
                continue;
            }

            LogSwingCPivotAudit(
                in ev, pinnedBForC, in cfg, "defer-drop",
                chartBar: Bars.Count - 1,
                buildReason: reason);
            // Swing-C confirmed but plan build failed → abandon deferred fire (no entry).
            var (code, detail) = Loop6SkipReasonCodes.Map(reason, $"R{ev.SlotIndex + 1} deferred-split");
            DropDeferredAt(i, in ev, "c-invalid", code, detail);
        }
    }

    /// <summary>
    /// Run pending-invalidation rules that still apply while a fire is deferred (HL M15 BC/AB, near-D, etc.).
    /// Post-B-push-obstacle is intentionally skipped: deferred-split waits for swing-C to confirm and
    /// post-B push swings are often the C candidate itself — cancelling on zone overlap would drop
    /// valid setups before entry.
    /// </summary>
    bool TryDeferredPendingInvalidation(int index, in CompoundFireEvent ev, DeferredSplitFire item, in TradeMapperConfig cfg)
    {
        var ctx = BuildDeferredPendingContext(in ev, item);

        if (EnableCancelOnNearDZone && _nearDCancelCfg.AppliesToSlot(ev.SlotIndex))
        {
            var nearD = NearDZoneCancelRule.EvaluateWithProbe(in ctx, ResolvePendingCancelProbe(ctx.IsBuy, PendingCancelProbeKind.NearD));
            if (nearD.ShouldCancel)
            {
                Print(NearDZoneCancelRule.FormatCancelLog(in ctx, in nearD));
                DropDeferredAt(index, in ev, NearDZoneCancelRule.CancelReason,
                    Loop6SkipReasonCodes.DeferredSplit,
                    $"deferred-split drop {NearDZoneCancelRule.CancelReason} Dref={item.FireBarDReferencePrice:0.#####}");
                return true;
            }
        }

        if (EnableCancelHlM15BcAbRatio && _hlM15BcAbCancelCfg.AppliesToSlot(ev.SlotIndex))
        {
            var bcAb = HlM15BcAbRatioCancelRule.Evaluate(in ctx, _host, in _hlM15BcAbCancelCfg);
            if (!string.IsNullOrEmpty(bcAb.VerboseSkip))
                Log($"[L6BT] {bcAb.VerboseSkip}");
            if (bcAb.ShouldCancel)
            {
                Print(HlM15BcAbRatioCancelRule.FormatCancelLog(in ctx, in bcAb, in _hlM15BcAbCancelCfg));
                DropDeferredAt(index, in ev, HlM15BcAbRatioCancelRule.CancelReason,
                    Loop6SkipReasonCodes.DeferredSplit,
                    $"deferred-split drop {HlM15BcAbRatioCancelRule.CancelReason}");
                return true;
            }
        }

        return false;
    }

    static SwingBResult? BuildPinnedSwingBFromDeferred(DeferredSplitFire item)
    {
        if (item.OriginalSwingBPivotBar <= 0)
            return null;

        return new SwingBResult
        {
            PivotIndex = item.OriginalSwingBPivotIndex,
            PivotBar = item.OriginalSwingBPivotBar,
            Type = item.OriginalSwingBType,
            KeyTop = item.SwingBKeyTop,
            KeyBottom = item.SwingBKeyBottom,
            Source = item.OriginalSwingBSource,
            StructurePivotBar = item.SwingBFromM5Anchor ? item.StructureSwingBBar : 0,
        };
    }

    PendingOrderContext BuildDeferredPendingContext(in CompoundFireEvent ev, DeferredSplitFire item)
    {
        var isBuy = ev.Direction == SignalDirection.Buy;
        return new PendingOrderContext
        {
            Label = $"{LabelPrefix}R{ev.SlotIndex + 1}|B{item.OriginalSwingBPivotBar}|DEFER",
            RuleSlot = ev.SlotIndex,
            Direction = ev.Direction,
            IsBuy = isBuy,
            SwingBPivotBar = item.OriginalSwingBPivotBar,
            SwingBPivotIndex = item.OriginalSwingBPivotIndex,
            SwingBTfToken = string.IsNullOrWhiteSpace(item.SwingBTfToken) ? "15" : item.SwingBTfToken,
            SwingBKeyHigh = item.SwingBKeyTop,
            SwingBKeyLow = item.SwingBKeyBottom,
            NearDCancelZoneHigh = item.NearDCancelZoneHigh,
            NearDCancelZoneLow = item.NearDCancelZoneLow,
            NearDCancelZoneIsOb = item.NearDCancelZoneIsOb,
            NearDCancelZoneTfToken = item.NearDCancelZoneTfToken ?? "",
            FireChartBarIndex = ev.ChartBarIndex,
            // EntryPrice / StopLoss / TP are unused by post-B push and HL-M15 BC/AB rules;
            // leave as default. C is unknown by definition while deferred.
        };
    }

    bool TryGetChartBarOhlc(int chartBarIndex, out double high, out double low)
    {
        high = double.NaN;
        low = double.NaN;
        if (chartBarIndex < 0 || chartBarIndex >= Bars.Count)
            return false;
        high = Bars.HighPrices[chartBarIndex];
        low = Bars.LowPrices[chartBarIndex];
        return true;
    }

    void DropDeferredAt(int index, in CompoundFireEvent ev, string shortReason, string code, string detail)
    {
        if (index >= 0 && index < _deferredSplitFires.Count)
        {
            var item = _deferredSplitFires[index];
            _fireChartLabels?.ClearSwingCCandidateLabels(in ev);
            _fireChartLabels?.MarkDeferredDrop(
                in ev,
                item.DeferredAtBar,
                ev.Direction == SignalDirection.Buy,
                $"{shortReason}: {detail}");
        }

        if (EnableLog)
            Log($"[L6BT] DEFERRED-SPLIT drop R{ev.SlotIndex + 1} {ev.Direction} fireBar={ev.ChartBarIndex} reason={shortReason} detail=\"{detail}\"");
        LogCsvSkip(ev.SlotIndex, ev.Direction == SignalDirection.Buy ? "buy" : "sell", code, detail,
            chartBar: ev.ChartBarIndex);
        _deferredSplitFires.RemoveAt(index);
    }

    // ── SetupAtCondFire — pin B/D geometry at cond-fire bar N ────────────────

    /// <summary>
    /// Callback invoked by <see cref="PerSymbolSignalHost.SetAnchorCreatedCallback"/> during
    /// <c>RunOneTick</c> at the chart bar containing cond-fire source bar N.
    /// Resolves B/D from live state (which IS bar N state) and caches by sourceEventKey.
    /// </summary>
    void PinSetupAtCondFire(CompoundEventAnchor anchor)
    {
        var key = anchor.SourceEventKey;
        if (string.IsNullOrEmpty(key) || _pinnedSwingBByKey.ContainsKey(key))
            return;

        var isBuy = anchor.Direction == SignalDirection.Buy;
        var cfg = BuildMapperConfig();
        var b = CompoundFireTradeMapper.ResolveSwingB(_host.State, isBuy, anchor.SlotIndex, in cfg, out var bReason);
        if (b is null)
        {
            if (EnableLog)
                Log($"[L6BT] SACF-PIN-SKIP R{anchor.SlotIndex + 1} {anchor.Direction} sourceBar={anchor.SourceBarIndex} noB={bReason}");
            return;
        }

        _pinnedSwingBByKey[key] = b;

        // Also cache D-fire at cond bar N's OHLC.
        TryGetChartBarOhlc(anchor.StagedAtChartBarIndex, out var condHigh, out var condLow);
        var ev = new CompoundFireEvent(
            anchor.SlotIndex, anchor.RuleId, anchor.Direction, "15",
            anchor.StagedAtChartBarIndex, DateTime.MinValue, 0,
            anchor.SourceBarIndex, anchor.SourceTfToken, key);
        _pinnedDFireByKey[key] = CompoundFireTradeMapper.ResolveDFireAtFire(
            _host.State, in cfg, in ev, b, condHigh, condLow);

        if (EnableLog)
            Log($"[L6BT] SACF-PIN R{anchor.SlotIndex + 1} {anchor.Direction} " +
                $"sourceBar={anchor.SourceBarIndex} condChartBar={anchor.StagedAtChartBarIndex} " +
                $"B={b.PivotBar} Bsrc={b.Source}");
    }

    /// <summary>Remove pinned geometry entries that are no longer needed (submitted or expired).</summary>
    void PurgePinnedSetupKeys(string sourceEventKey)
    {
        _pinnedSwingBByKey.Remove(sourceEventKey);
        _pinnedDFireByKey.Remove(sourceEventKey);
    }

    void DropAllDeferredOnCutoff()
    {
        if (_deferredSplitFires.Count == 0)
            return;

        var bkk = Loop6BangkokSession.GetBangkokTime(Server.TimeInUtc);
        var detail = $"deferred-split drop entry-cutoff Bangkok hour>={EntryCutoffHourBangkok} bkkHour={bkk.Hour}";
        for (var i = 0; i < _deferredSplitFires.Count; i++)
        {
            var item = _deferredSplitFires[i];
            var ev = item.Ev;
            LogCsvSkip(ev.SlotIndex, ev.Direction == SignalDirection.Buy ? "buy" : "sell",
                Loop6SkipReasonCodes.EntryCutoffBangkok, detail, chartBar: ev.ChartBarIndex);
        }
        if (EnableLog)
            Log($"[L6BT] DEFERRED-SPLIT drop {_deferredSplitFires.Count} on entry-cutoff bkkHour={bkk.Hour}");
        _deferredSplitFires.Clear();
    }

    /// <summary>
    /// Cancel all pending orders (limit/stop) when entry cutoff hour is reached.
    /// Runs every bar during the cutoff window; idempotent — no effect once orders are already gone.
    /// </summary>
    void CancelAllPendingOnCutoff()
    {
        var pendings = _exec.OurPendingOrders().ToArray();
        if (pendings.Length == 0)
            return;

        var bkk = Loop6BangkokSession.GetBangkokTime(Server.TimeInUtc);
        if (EnableLog)
            Log($"[L6BT] ENTRY CUTOFF cancel {pendings.Length} pending(s) bkkHour={bkk.Hour}");

        foreach (var order in pendings)
        {
            if (order.Label is null) continue;
            if (_exec.CancelPendingByLabel(order.Label, "entry-cutoff"))
                RemoveTrackedPending(order.Label);
        }
    }

    MarketFillValidationParams BuildMarketFillValidationParams(double riskPercentForLeg)
    {
        var spreadSnap = ResolveSpreadSnapshot();
        var isGoldSymbol = SpreadResolver.UsesPriceSpreadOverride(SymbolName, _sizingProfile.AssetType);
        return new MarketFillValidationParams
        {
            EnableMinSlConstraint = EnableMinSlConstraint,
            BaseRewardRisk = RewardRisk,
            SkipIfSwingCRrBelowBase = _swingCTpCfg.SkipIfSwingCRrBelowBase,
            MoveEntryIfSwingCRrBelowBase = _swingCTpCfg.MoveEntryIfSwingCRrBelowBase,
            EnableSpreadGate = EnableSpreadGate,
            MaxSpreadGate = MaxSpreadGate,
            SpreadGateIsPriceMode = isGoldSymbol,
            SpreadGateValue = isGoldSymbol ? spreadSnap.Price : spreadSnap.Pips,
            AssetType = _sizingProfile.AssetType,
            CustomContractSize = _sizingProfile.ContractSize,
            AccountBalanceFtmo = AccountBalanceFtmo,
            RiskPercentForLeg = riskPercentForLeg,
            ConvUsdPerQuote = ResolveConvUsdPerQuote(_sizingProfile.QuoteCurrency),
            SpreadPips = spreadSnap.Pips,
            RoundPrecision = RoundPrecision,
            SymbolName = SymbolName,
            TickSize = Symbol.TickSize,
            PipSize = Symbol.PipSize > 0 ? Symbol.PipSize : Symbol.TickSize,
            IsCryptoSymbol = _sizingProfile.IsCrypto,
            QuoteCurrency = _sizingProfile.QuoteCurrency,
            BaseAssetName = _sizingProfile.BaseAssetName,
        };
    }

    /// <returns>True when a limit order was placed.</returns>
    bool ExecutePlan(
        TradePlan plan,
        int ruleSlot,
        in CompoundFireEvent ev,
        double plannedRiskUsd,
        double lotSizingRiskPips,
        double riskPercentForLeg,
        bool skipParallelGate = false,
        bool skipFlip = false)
    {
        if (_book.HasDedup(plan.DedupKey))
        {
            Log($"[L6BT] dedup skip {plan.DedupKey}");
            LogCsvSkip(ruleSlot, plan.IsBuy ? "buy" : "sell", Loop6SkipReasonCodes.Other,
                $"dedup {plan.DedupKey}", plan.RawEntryBid, plan.RawSlBid, plan.RawTpBid,
                plan.SlPipsBeforeFloor);
            return false;
        }

        if (plan.LotFtmo is not > 0)
        {
            Log($"[L6BT] no lot (conv/balance?) skip {plan.Label}");
            LogCsvSkip(ruleSlot, plan.IsBuy ? "buy" : "sell", Loop6SkipReasonCodes.Other,
                "no lot (conv/balance?)", plan.RawEntryBid, plan.RawSlBid, plan.RawTpBid,
                plan.SlPipsBeforeFloor);
            return false;
        }

        // Flip (legacy): when enabled, close/cancel opposite exposure before opening.
        // When disabled (default), opposite pendings/positions coexist; unfilled orders cancel only via invalidation gates.
        if (!skipFlip && AllowFlip && _exec.HasOppositeLive(plan.IsBuy))
        {
            var flipLabels = new System.Collections.Generic.List<string>();
            foreach (var p in _exec.OurPositions())
            {
                if (p.Label == null) continue;
                if (p.TradeType == (plan.IsBuy ? TradeType.Sell : TradeType.Buy))
                {
                    flipLabels.Add(p.Label);
                    _closeReasonAudit.RecordManualClose(p.Label, "flip");
                    _execAudit.PrepareClose(p.Label, "flip");
                }
            }
            _exec.CloseAndCancelOpposite(plan.IsBuy, "flip");
            AuditManualCloses(flipLabels, "flip");
        }

        // Parallel gate: when disabled, block any new setup while any live position/pending exists (split legs exempt).
        if (!skipParallelGate && !AllowParallel && _exec.HasAnyLive())
        {
            Log($"[L6BT] parallel disabled, live exists, skip {plan.Label}");
            LogCsvSkip(ruleSlot, plan.IsBuy ? "buy" : "sell", Loop6SkipReasonCodes.Other,
                "parallel disabled", plan.RawEntryBid, plan.RawSlBid, plan.RawTpBid,
                plan.SlPipsBeforeFloor);
            return false;
        }

        // Defensive limit-side check — wrong side may fall back to MARKET when enabled.
        var sideOk = plan.IsBuy ? plan.EntryLimit < Symbol.Ask : plan.EntryLimit > Symbol.Bid;
        var useMarketFallback = false;
        MarketFillValidationResult? marketFill = null;
        var lotFtmo = plan.LotFtmo!.Value;
        var sizingRiskPips = lotSizingRiskPips;

        if (!sideOk)
        {
            if (!EnableMarketWrongSideFallback)
            {
                Log($"[L6BT] limit wrong side (entry={plan.EntryLimit} bid={Symbol.Bid} ask={Symbol.Ask}) skip {plan.Label}");
                LogCsvSkip(ruleSlot, plan.IsBuy ? "buy" : "sell", Loop6SkipReasonCodes.OrderDistanceInvalid,
                    $"entry={plan.EntryLimit} bid={Symbol.Bid} ask={Symbol.Ask}",
                    plan.RawEntryBid, plan.RawSlBid, plan.RawTpBid, plan.SlPipsBeforeFloor);
                return false;
            }

            useMarketFallback = true;
            Log($"[L6BT] limit wrong side (entry={plan.EntryLimit} bid={Symbol.Bid} ask={Symbol.Ask}) → MARKET fallback for {plan.Label}");
        }

        if (useMarketFallback)
        {
            var fillParams = BuildMarketFillValidationParams(riskPercentForLeg);
            if (!MarketFallbackGate.TryValidateMarketFill(
                    in plan, Symbol.Bid, Symbol.Ask, in fillParams, out var fillResult))
            {
                Log($"[L6BT] market fill validation blocked ({fillResult.RejectReason}) skip {plan.Label}");
                LogCsvSkip(ruleSlot, plan.IsBuy ? "buy" : "sell", Loop6SkipReasonCodes.OrderDistanceInvalid,
                    fillResult.RejectReason, plan.RawEntryBid, plan.RawSlBid, plan.RawTpBid, plan.SlPipsBeforeFloor);
                return false;
            }

            marketFill = fillResult;
            lotFtmo = fillResult.LotFtmoAtFill!.Value;
            sizingRiskPips = fillResult.SlPipsAtFill;
            Print($"[L6BT] MARKET_FILL ok {plan.Label}: fill={fillResult.FillPrice:0.#####} " +
                  $"slPips={fillResult.SlPipsAtFill:0.##} tpPips={fillResult.TpPipsAtFill:0.##} " +
                  $"RR={fillResult.RrAtFill:0.##} lot={lotFtmo:0.###} (plan lot={plan.LotFtmo:0.###})");
        }

        var units = Symbol.NormalizeVolumeInUnits(
            Symbol.QuantityToVolumeInUnits(lotFtmo),
            RoundingMode.Down);

        if (units < Symbol.VolumeInUnitsMin)
        {
            Log($"[L6BT] lot {lotFtmo} -> {units}u < min {Symbol.VolumeInUnitsMin}, skip {plan.Label}");
            LogCsvSkip(ruleSlot, plan.IsBuy ? "buy" : "sell", Loop6SkipReasonCodes.Other,
                $"volume {units}u < min {Symbol.VolumeInUnitsMin}", plan.RawEntryBid, plan.RawSlBid, plan.RawTpBid,
                plan.SlPipsBeforeFloor);
            return false;
        }

        var useNoTp = _virtualXrCfg.PureVirtualMode
                      || (_swingCTpCfg.AppliesToSlot(ruleSlot)
                          && _swingCTpCfg.TpMode == SwingCTpMode.NoTpUntilC);
        var expectTpOnPlace = !useNoTp;

        foreach (var line in _execAudit.FormatPlanAbs(in plan, RewardRisk, plannedRiskUsd, units))
            Print(line);
        _execAudit.RecordPlanned(in plan, RewardRisk, expectTpOnPlace, plannedRiskUsd, units);

        if (_csvLog != null)
        {
            _csvLog.LogPlan(in plan, in ev, BuildAnalyticsContext(),
                "SUBMITTED", volumeUnits: units,
                plannedRiskUsd: plannedRiskUsd, lotSizingRiskPips: sizingRiskPips);
        }

        PlaceOrderResult placeResult;
        if (useMarketFallback)
        {
            var fillPrice = marketFill!.FillPrice;
            placeResult = useNoTp
                ? _exec.PlaceMarketNoTp(plan, units, fillPrice, Symbol.PipSize)
                : _exec.PlaceMarket(plan, units, fillPrice, Symbol.PipSize);
        }
        else
        {
            placeResult = useNoTp
                ? _exec.PlaceLimitNoTp(plan, units)
                : _exec.PlaceLimit(plan, units);
        }

        if (!placeResult.Success)
        {
            var placeKind = useMarketFallback ? "PlaceMarket" : "PlaceLimit";
            if (_csvLog != null)
            {
                _csvLog.LogPlan(in plan, in ev, BuildAnalyticsContext(),
                    "REJECTED", rejectReason: $"{placeKind} failed",
                    volumeUnits: units, plannedRiskUsd: plannedRiskUsd,
                    lotSizingRiskPips: sizingRiskPips);
            }
            LogCsvSkip(ruleSlot, plan.IsBuy ? "buy" : "sell", Loop6SkipReasonCodes.Other,
                $"{placeKind} FAILED", plan.RawEntryBid, plan.RawSlBid, plan.RawTpBid,
                plan.SlPipsBeforeFloor);
            return false;
        }

        var money = BuildAuditMoneyContext();
        var order = placeResult.Order ?? _exec.TryGetPendingByLabel(plan.Label);
        if (order != null)
            _execAudit.TryAuditOrder(order, in money, Print);
        var position = placeResult.Position ?? _exec.TryGetPositionByLabel(plan.Label);
        if (position != null)
            _execAudit.TryAuditPosition(position, in money, Print);

        _book.Track(PendingOrderContext.FromPlan(plan, ruleSlot, ev.ChartBarIndex), plan.TakeProfit);
        return true;
    }

    void AuditLiveExecutions()
    {
        var money = BuildAuditMoneyContext();
        foreach (var order in _exec.OurPendingOrders())
            _execAudit.SyncProtectionFromPending(order);
        foreach (var position in _exec.OurPositions())
            _execAudit.SyncProtectionFromPosition(position);
        foreach (var order in _exec.OurPendingOrders())
            _execAudit.TryAuditOrder(order, in money, Print);
        foreach (var position in _exec.OurPositions())
            _execAudit.TryAuditPosition(position, in money, Print);
    }

    void ReconcileRemovedLabels(IReadOnlyList<string> removedLabels)
    {
        var money = BuildAuditMoneyContext();
        foreach (var label in removedLabels)
            _execAudit.OnBookLabelRemoved(label, History, SymbolName, in money, Print);
    }

    void AuditCloseWithExcursion(string label, ClosedTradeData data, string reason)
    {
        var hasExcursion = _tradeExcursions.TryRemove(label, out var excursion);
        _execAudit.AuditClose(label, data, reason, Print, in excursion, hasExcursion);
    }

    void AuditManualCloses(IEnumerable<string> labels, string reason)
    {
        foreach (var label in labels)
            AuditCloseWithExcursion(label, FindClosedTradeData(label), reason);
    }

    /// <summary>Latest closed trade for a label as audit data, or empty when none recorded yet.</summary>
    ClosedTradeData FindClosedTradeData(string label)
    {
        HistoricalTrade? trade = null;
        foreach (var t in History)
        {
            if (string.Equals(t.Label, label, StringComparison.Ordinal)
                && string.Equals(t.SymbolName, SymbolName, StringComparison.Ordinal))
                trade = t;
        }

        return trade != null ? ExecutionAuditTracker.FromHistory(trade) : ClosedTradeData.Empty;
    }

    void RemoveTrackedPending(string label)
    {
        _execAudit.RecordPendingRemoved(label);
        _book.RemoveByLabel(label);
    }

    void CloseTrackedPosition(string label, string reason)
    {
        _execAudit.PrepareClose(label, reason);
        _exec.CloseAndCancelByLabel(label, reason);
        _book.RemoveByLabel(label);

        // Positions.Closed may already have audited this synchronously; AuditClose dedups by label.
        AuditCloseWithExcursion(label, FindClosedTradeData(label), reason);
    }

    ExecutionAuditMoneyContext BuildAuditMoneyContext() => new()
    {
        PipSize = AuditPipSize(),
        PipValuePerLot = Symbol.PipValue,
        LotVolumeInUnits = Symbol.QuantityToVolumeInUnits(1.0),
        UsePriceDistanceTimesVolume = UsesPriceDistanceTimesVolumeApprox(),
    };

    bool UsesPriceDistanceTimesVolumeApprox() =>
        _sizingProfile.AssetType == KlAssetType.XAUUSD
        || _sizingProfile.IsCrypto
        || _sizingProfile.ContractSize is > 0 and <= 100;

    double AuditPipSize() => Symbol.PipSize > 0 ? Symbol.PipSize : Symbol.TickSize;

    void FinalizeExecutionAuditCloses()
    {
        foreach (var trade in History)
        {
            if (!string.Equals(trade.SymbolName, SymbolName, StringComparison.Ordinal))
                continue;
            if (trade.Label is null || !trade.Label.StartsWith(LabelPrefix, StringComparison.Ordinal))
                continue;

            var reason = HistoryCloseReasonClassifier.Classify(trade);
            AuditCloseWithExcursion(trade.Label, ExecutionAuditTracker.FromHistory(trade), reason);
        }
    }

    int CountHistoryClosed()
    {
        var n = 0;
        foreach (var trade in History)
        {
            if (!string.Equals(trade.SymbolName, SymbolName, StringComparison.Ordinal))
                continue;
            if (trade.Label is null || !trade.Label.StartsWith(LabelPrefix, StringComparison.Ordinal))
                continue;
            n++;
        }

        return n;
    }

    /// <summary>
    /// Real-time per-position close audit. Fires for every closed position (including limit orders
    /// that fill and hit TP/SL between bars), so clamped metrics cover the full History.
    /// </summary>
    void OnPositionClosedAudit(PositionClosedEventArgs args)
    {
        var pos = args.Position;
        if (!string.Equals(pos.SymbolName, SymbolName, StringComparison.Ordinal))
            return;
        if (pos.Label is null || !pos.Label.StartsWith(LabelPrefix, StringComparison.Ordinal))
            return;

        _execAudit.SyncProtectionFromClosedPosition(pos);

        var reason = MapPositionCloseReason(args.Reason);
        var data = BuildClosedTradeData(pos);
        ExcursionSnapshot excursion = default;
        var hasExcursion = pos.Label != null && _tradeExcursions.TryRemove(pos.Label, out excursion);
        _execAudit.AuditClose(
            pos.Label!, data, reason, Print,
            in excursion, hasExcursion);
        LogCsvClose(pos, reason, data, in excursion, hasExcursion);

        if (_splitPairedCloseGuard.Remove(pos.Label))
        {
            LogPositionCloseDiagnostic(args);
            return;
        }

        TrySplitLegPairedSiblingOnClose(pos.Label!, reason);

        LogPositionCloseDiagnostic(args);
    }

    static bool IsBacktestRunningMode(RunningMode mode) => mode != RunningMode.RealTime;

    enum PendingCancelProbeKind
    {
        /// <summary>Near-D zone touch: realtime BUY=Ask SELL=Bid; backtest BUY=High SELL=Low.</summary>
        NearD,
        /// <summary>Near-B zone touch: realtime BUY=Ask SELL=Bid; backtest BUY=Low SELL=High.</summary>
        NearBTouchB,
        /// <summary>TpC reach: realtime BUY=Bid SELL=Ask; backtest BUY=High SELL=Low.</summary>
        NearBReachTpC,
    }

    /// <summary>
    /// Probe price for pending-cancel touch checks. Backtest OnBar uses the last <b>closed</b>
    /// bar's High/Low (not Bars.Count-1 forming bar). Realtime uses Ask/Bid.
    /// </summary>
    double ResolvePendingCancelProbe(bool isBuy, PendingCancelProbeKind kind)
    {
        if (NearDCancelBacktestUseHighLow && IsBacktestRunningMode(RunningMode) && Bars.Count > 0)
        {
            var closeIdx = NearDZoneCancelRule.BacktestLastClosedBarIndex(
                Bars.Count, _host?.LastClosedBarProcessedThisTick ?? -1);
            if (closeIdx < 0 || closeIdx >= Bars.Count)
                return isBuy ? Symbol.Ask : Symbol.Bid;

            var hi = Bars.HighPrices[closeIdx];
            var lo = Bars.LowPrices[closeIdx];
            return kind switch
            {
                PendingCancelProbeKind.NearD => isBuy ? hi : lo,
                PendingCancelProbeKind.NearBTouchB => isBuy ? lo : hi,
                PendingCancelProbeKind.NearBReachTpC => isBuy ? hi : lo,
                _ => isBuy ? Symbol.Ask : Symbol.Bid,
            };
        }

        return kind switch
        {
            PendingCancelProbeKind.NearD => isBuy ? Symbol.Ask : Symbol.Bid,
            PendingCancelProbeKind.NearBTouchB => isBuy ? Symbol.Ask : Symbol.Bid,
            PendingCancelProbeKind.NearBReachTpC => isBuy ? Symbol.Bid : Symbol.Ask,
            _ => isBuy ? Symbol.Ask : Symbol.Bid,
        };
    }

    void CancelSwingCBrokenWaitDSplitSibling(string triggerLabel)
    {
        if (_swingCTpCfg.TpMode != SwingCTpMode.SplitTpAtCAndD)
            return;
        if (!SplitLegPairedCloseRule.TryGetSiblingLabel(triggerLabel, out var sibling))
            return;
        if (!_exec.HasPendingByLabel(sibling))
            return;

        Print(SwingCBrokenWaitDCancelRule.FormatPairCancelLog(triggerLabel, sibling));
        if (_exec.CancelPendingByLabel(sibling, SwingCBrokenWaitDCancelRule.CancelReason))
        {
            RemoveTrackedPending(sibling);
            if (_book.TryGetContext(sibling, out var sibCtx))
                DrawFireChartCancel(in sibCtx, SwingCBrokenWaitDCancelRule.CancelReason);
        }
    }

    /// <summary>
    /// SplitTpAtCAndD: cancel pending sibling on SL / same-TP;
    /// in backtest also force-close filled sibling (SL sync, same-TP sync).
    /// </summary>
    void TrySplitLegPairedSiblingOnClose(string closedLabel, string triggerReason)
    {
        if (_swingCTpCfg.TpMode != SwingCTpMode.SplitTpAtCAndD)
            return;
        if (string.Equals(triggerReason, PostFillCRolloverRule.CloseReason, StringComparison.Ordinal))
            return;
        if (!SplitLegPairedCloseRule.TryGetSiblingLabel(closedLabel, out var sibling))
            return;

        var closedTp = ResolveLegTakeProfit(closedLabel);
        var siblingTp = ResolveLegTakeProfit(sibling);
        var tick = Symbol.TickSize > 0 ? Symbol.TickSize : Symbol.PipSize;

        if (!SplitLegPairedCloseRule.ShouldPairedClose(triggerReason, closedTp, siblingTp, tick))
        {
            if (string.Equals(triggerReason, "TakeProfit", StringComparison.Ordinal))
                Print(SplitLegPairedCloseRule.FormatSkipLog(closedLabel, sibling, triggerReason, closedTp, siblingTp));
            return;
        }

        if (_exec.HasPendingByLabel(sibling))
        {
            Print(SplitLegPairedCloseRule.FormatCancelLog(closedLabel, sibling, triggerReason));
            if (_exec.CancelPendingByLabel(sibling, SplitLegPairedCloseRule.CloseReason))
                RemoveTrackedPending(sibling);
            return;
        }

        if (!IsBacktestRunningMode(RunningMode))
            return;

        if (_exec.TryGetPositionByLabel(sibling) == null)
            return;

        Print(SplitLegPairedCloseRule.FormatCloseLog(closedLabel, sibling, triggerReason));
        _splitPairedCloseGuard.Add(sibling);
        _closeReasonAudit.RecordManualClose(sibling, SplitLegPairedCloseRule.CloseReason);
        CloseTrackedPosition(sibling, SplitLegPairedCloseRule.CloseReason);
    }

    double ResolveLegTakeProfit(string label)
    {
        if (_book.TryGetSetup(label, out var setup))
        {
            if (setup.CurrentTakeProfit > 0)
                return setup.CurrentTakeProfit;
            if (setup.Context.OriginalTakeProfit > 0)
                return setup.Context.OriginalTakeProfit;
        }

        if (_execAudit.TryGetPlanned(label, out var planned) && planned.PlannedTp > 0)
            return planned.PlannedTp;

        return 0;
    }

    /// <summary>
    /// Real-time per-position open audit. cTrader applies pip-based protection relative to the
    /// actual fill, so this either audits the fill-relative SL/TP (ActualFillRelative) or rewrites
    /// protection to the planned absolute prices (PlannedAbsolute).
    /// </summary>
    void OnPositionOpenedAudit(PositionOpenedEventArgs args)
    {
        var pos = args.Position;
        if (!string.Equals(pos.SymbolName, SymbolName, StringComparison.Ordinal))
            return;
        if (pos.Label is null || !pos.Label.StartsWith(LabelPrefix, StringComparison.Ordinal))
            return;

        if (_protectionAnchorMode == ProtectionAnchorMode.PlannedAbsolute)
            SyncProtectionToPlannedAbsolute(pos);
        else
            _execAudit.AuditProtectionActualFill(pos, AuditPipSize(), Print);

        LogSpreadAtFill(pos);
        RegisterTradeExcursionOnOpen(pos);

        if (_csvLog != null && pos.Label != null)
        {
            var ruleSlot = TryParseRuleSlotFromLabel(pos.Label) ?? 0;
            if (_book.TryGetContext(pos.Label, out var bookCtx))
                ruleSlot = bookCtx.RuleSlot;

            if (_execAudit.TryGetPlanned(pos.Label, out var planned))
            {
                _csvLog.LogFill(
                    BuildAnalyticsContext(), pos.Label, ruleSlot,
                    pos.TradeType == TradeType.Buy,
                    planned.PlannedEntry, pos.EntryPrice, Server.TimeInUtc,
                    pos.VolumeInUnits, planned.PlannedRiskUsd,
                    planned.SpreadPipsAtPlan, planned.SpreadSourceAtPlan);
            }
        }

        _execAudit.SyncProtectionFromClosedPosition(pos);
        _execAudit.TryAuditPosition(pos, BuildAuditMoneyContext(), Print);

        if (_virtualXrCfg.Enabled)
        {
            _execAudit.SetupVirtualXrForPosition(
                pos.Label,
                pos.EntryPrice,
                pos.StopLoss ?? 0,
                pos.TradeType == TradeType.Buy,
                in _virtualXrCfg,
                Print);

            if (_virtualXrCfg.EmergencyBrokerTpR > 0 && !_virtualXrCfg.UseBrokerTp)
                ApplyEmergencyVirtualXrBrokerTp(pos);
        }

        LogPositionOpenDiagnostic(pos);
    }

    void LogPositionOpenDiagnostic(Position p)
    {
        if (!EnablePositionSlDiagnosticLog)
            return;

        Print($"[POS-DIAG] OPEN id={p.Id} type={p.TradeType} symbol={p.SymbolName} label={p.Label} " +
              $"entry={p.EntryPrice:0.#####} sl={FmtPosPrice(p.StopLoss)} tp={FmtPosPrice(p.TakeProfit)} volume={p.VolumeInUnits}");
        LogSplitSiblingState(p.Label, "open");
        LogAllLivePositionsSnapshot("after-open");
    }

    void LogPositionCloseDiagnostic(PositionClosedEventArgs args)
    {
        if (!EnablePositionSlDiagnosticLog)
            return;

        var p = args.Position;
        var closePrice = ResolveClosedPositionPrice(p);
        Print($"[POS-DIAG] CLOSE id={p.Id} reason={args.Reason} type={p.TradeType} label={p.Label} " +
              $"entry={p.EntryPrice:0.#####} sl={FmtPosPrice(p.StopLoss)} close={closePrice:0.#####}");
        LogSplitSiblingState(p.Label, "close");
        LogAllLivePositionsSnapshot("after-close");
    }

    static string FmtPosPrice(double? price) =>
        price is > 0 ? price.Value.ToString("0.#####", CultureInfo.InvariantCulture) : "none";

    void LogSplitSiblingState(string? label, string ctx)
    {
        if (!SplitLegPairedCloseRule.TryGetSiblingLabel(label, out var sibling))
            return;

        string state;
        if (_exec.TryGetPositionByLabel(sibling) is { } sibPos)
        {
            state = $"position id={sibPos.Id} sl={FmtPosPrice(sibPos.StopLoss)} tp={FmtPosPrice(sibPos.TakeProfit)}";
        }
        else if (_exec.HasPendingByLabel(sibling))
        {
            state = "pending";
        }
        else
        {
            state = "none";
        }

        Print($"[POS-DIAG] SIBLING ctx={ctx} closed={label} sibling={sibling} state={state}");
    }

    void LogAllLivePositionsSnapshot(string context)
    {
        if (!EnablePositionSlDiagnosticLog)
            return;

        var positions = _exec.OurPositions().ToArray();
        Print($"[POS-DIAG] SNAPSHOT ctx={context} ours={positions.Length} bid={Symbol.Bid:0.#####} ask={Symbol.Ask:0.#####}");
        foreach (var p in positions)
        {
            Print($"[POS-DIAG]   LIVE id={p.Id} type={p.TradeType} label={p.Label} " +
                  $"entry={p.EntryPrice:0.#####} sl={FmtPosPrice(p.StopLoss)} tp={FmtPosPrice(p.TakeProfit)}");
        }
    }

    void LogPositionSlProximityDiagnostics()
    {
        if (!EnablePositionSlDiagnosticLog)
            return;

        var bar = Bars.Count;
        if (bar != _posDiagSlLogBar)
        {
            _posDiagSlLogBar = bar;
            _posDiagSlLoggedThisBar.Clear();
        }

        var near = PositionSlDiagnosticNearPips * AuditPipSize();
        foreach (var p in _exec.OurPositions())
        {
            if (p.StopLoss is not double sl || sl <= 0)
            {
                if (!_posDiagSlLoggedThisBar.Contains(-p.Id))
                {
                    _posDiagSlLoggedThisBar.Add(-p.Id);
                    Print($"[POS-DIAG] NO-SL id={p.Id} label={p.Label} type={p.TradeType} entry={p.EntryPrice:0.#####}");
                }
                continue;
            }

            var nearSl = p.TradeType == TradeType.Buy
                ? Symbol.Bid <= sl + near
                : Symbol.Ask >= sl - near;
            if (!nearSl || _posDiagSlLoggedThisBar.Contains(p.Id))
                continue;

            _posDiagSlLoggedThisBar.Add(p.Id);
            Print($"[POS-DIAG] PRICE time={Server.Time:yyyy-MM-dd HH:mm:ss} bid={Symbol.Bid:0.#####} ask={Symbol.Ask:0.#####} " +
                  $"near-sl id={p.Id} label={p.Label} type={p.TradeType} sl={sl:0.#####} entry={p.EntryPrice:0.#####}");
        }
    }

    double ResolveClosedPositionPrice(Position pos)
    {
        foreach (var t in History)
        {
            if (t.PositionId == pos.Id)
                return t.ClosingPrice;
        }

        return pos.EntryPrice;
    }

    void ApplyEmergencyVirtualXrBrokerTp(Position pos)
    {
        var sl = pos.StopLoss ?? 0;
        if (sl <= 0)
            return;

        var entry = pos.EntryPrice;
        var riskDistance = Math.Abs(entry - sl);
        var isBuy = pos.TradeType == TradeType.Buy;
        var emergencyTp = VirtualXrBarCloseEvaluator.ComputeVirtualXrPrice(
            entry, sl, _virtualXrCfg.EmergencyBrokerTpR, isBuy);

        if (pos.TakeProfit is > 0)
            return;

        var mod = _exec.TryModifyTakeProfit(pos, emergencyTp);
        if (mod.Success)
            _execAudit.UpdateProtectionTakeProfit(pos.Label!, emergencyTp);
    }

    void EvaluateVirtualXrBarCloseExits()
    {
        var tf = VirtualXrTimeframeParser.ToTimeFrame(_virtualXrCfg.ExitTimeframeToken);
        var xrBars = MarketData.GetBars(tf, SymbolName);
        if (xrBars.Count < 2)
            return;

        var idx = xrBars.Count - 2;
        var barTime = xrBars.OpenTimes[idx];
        if (!_execAudit.ShouldProcessVirtualXrBar(barTime))
            return;

        ProcessPendingVirtualXrCloses();

        var bar = new VirtualXrBarOhlc
        {
            OpenTime = barTime,
            Open = xrBars.OpenPrices[idx],
            High = xrBars.HighPrices[idx],
            Low = xrBars.LowPrices[idx],
            Close = xrBars.ClosePrices[idx],
        };
        var nextBarOpen = idx + 1 < xrBars.Count ? xrBars.OpenPrices[idx + 1] : 0;

        foreach (var position in _exec.OurPositions().ToArray())
        {
            if (position.Label is null)
                continue;
            if (!_execAudit.IsVirtualXrActive(position.Label))
                continue;
            if (_execAudit.HasPendingVirtualXrClose(position.Label))
                continue;
            if (_execAudit.HasBBrokenMoveTpToEntry(position.Label))
                continue;
            if (!_execAudit.TryGetProtection(position.Label, out var protection))
                continue;

            var signal = VirtualXrBarCloseEvaluator.EvaluateBar(
                in bar, protection.VirtualXrPrice, position.TradeType == TradeType.Buy);

            if (signal.Touched && !signal.Confirmed)
            {
                _execAudit.RecordVirtualXrTouchNoConfirmLog(position.Label, in bar, Print);
                continue;
            }

            if (!signal.Confirmed)
                continue;

            _execAudit.RecordVirtualXrConfirmed();
            var reason = VirtualXrConfig.CloseReasonForFillMode(_virtualXrCfg.FillMode);
            var marketClose = position.TradeType == TradeType.Buy ? Symbol.Bid : Symbol.Ask;
            var effectiveClose = VirtualXrBarCloseEvaluator.EffectiveCloseForFillMode(
                _virtualXrCfg.FillMode, in bar, nextBarOpen, marketClose);

            Print(VirtualXrExitLog.FormatExitSignal(
                position.Label,
                protection.VirtualXrTriggerR,
                protection.VirtualXrPrice,
                in bar,
                _virtualXrCfg.FillMode,
                effectiveClose));

            if (_virtualXrCfg.FillMode == Execution.VirtualXrFillMode.NextBarOpen)
            {
                _execAudit.EnqueueVirtualXrClose(position.Label, reason, in bar, nextBarOpen);
                continue;
            }

            _execAudit.PrepareVirtualXrClose(
                position.Label,
                reason,
                effectiveClose,
                bar.OpenTime,
                bar.Close,
                signal.Touched);
            ExecuteVirtualXrClose(position.Label, reason);
        }
    }

    void ProcessPendingVirtualXrCloses()
    {
        foreach (var pending in _execAudit.DrainPendingVirtualXrCloses())
        {
            var position = _exec.TryGetPositionByLabel(pending.Label);
            if (position is null)
                continue;

            var marketClose = position.TradeType == TradeType.Buy ? Symbol.Bid : Symbol.Ask;
            var signalBar = pending.SignalBar;
            var effectiveClose = VirtualXrBarCloseEvaluator.EffectiveCloseForFillMode(
                Execution.VirtualXrFillMode.NextBarOpen,
                in signalBar,
                pending.NextBarOpen,
                marketClose);

            _execAudit.PrepareVirtualXrClose(
                pending.Label,
                pending.Reason,
                effectiveClose,
                signalBar.OpenTime,
                signalBar.Close,
                touched: true);
            ExecuteVirtualXrClose(pending.Label, pending.Reason);
        }
    }

    void ExecuteVirtualXrClose(string label, string reason)
    {
        _closeReasonAudit.RecordManualClose(label, reason);
        CloseTrackedPosition(label, reason);
    }

    void SyncProtectionToPlannedAbsolute(Position pos)
    {
        _execAudit.MarkProtectionAudited(pos.Label);

        if (!_execAudit.TryGetPlanned(pos.Label, out var planned))
            return;

        var oldSl = pos.StopLoss ?? 0;
        var oldTp = pos.TakeProfit ?? 0;
        double? newSl = planned.PlannedSl > 0 ? planned.PlannedSl : pos.StopLoss;
        double? newTp = planned.PlannedTp > 0 ? planned.PlannedTp : pos.TakeProfit;

        var result = _exec.TryModifyProtection(pos, newSl, newTp);
        Print(TradePlanRrLog.FormatProtectionSync(
            pos.Label, oldSl, oldTp, newSl ?? 0, newTp ?? 0, result.Success));
        _execAudit.RecordProtectionSync(result.Success);
    }

    static string MapPositionCloseReason(PositionCloseReason reason) => reason switch
    {
        PositionCloseReason.TakeProfit => "TakeProfit",
        PositionCloseReason.StopLoss => "StopLoss",
        PositionCloseReason.StopOut => "StopLoss",
        _ => "Closed",
    };

    ClosedTradeData BuildClosedTradeData(Position pos)
    {
        var rawClose = pos.EntryPrice;
        var closeTime = DateTime.UtcNow;
        foreach (var t in History)
        {
            if (t.PositionId != pos.Id)
                continue;
            rawClose = t.ClosingPrice;
            closeTime = t.ClosingTime;
        }

        return new ClosedTradeData
        {
            HasData = true,
            RawClose = rawClose,
            GrossProfit = pos.GrossProfit,
            Commission = pos.Commissions,
            NetProfit = pos.NetProfit,
            CloseTime = closeTime,
        };
    }

    void HandleSwingBrokenExits()
    {
        var cfg = _bBrokenCfg;
        foreach (var setup in System.Linq.Enumerable.ToArray(_book.Entries))
        {
            if (!R1R2M5Structure.IsSwingBBroken(
                    setup.Context.SwingBFromM5Anchor, setup.Context.StructureSwingBBar, setup.Context.SwingBPivotBar,
                    _host.State, ResolveM5FallbackState()))
                continue;

            var label = setup.Context.Label;

            if (_exec.HasPendingByLabel(label))
            {
                Print(BBrokenPositionHandler.FormatPendingCancelLog(label));
                _exec.CancelPendingByLabel(label, "B-broken");
                RemoveTrackedPending(label);
                continue;
            }

            var position = _exec.TryGetPositionByLabel(label);
            var action = BBrokenPositionHandler.EvaluateOpenPosition(
                setup,
                position != null,
                position?.TakeProfit,
                in cfg);

            if (action is null)
                continue;

            if (action.AlreadyAtEntry)
                continue;

            if (action.ShouldCloseImmediately)
            {
                _closeReasonAudit.RecordManualClose(label, "B-broken");
                CloseTrackedPosition(label, "B-broken");
                continue;
            }

            if (!action.ShouldMoveTpToEntry || position is null)
                continue;

            var mod = _exec.TryModifyTakeProfit(position, action.NewTakeProfit);
            Print(BBrokenPositionHandler.FormatMoveTpLog(in action, mod.Success));
            if (mod.Success)
            {
                _book.MarkTpMovedToEntry(label, action.NewTakeProfit);
                _execAudit.RecordBBrokenMoveTpToEntry(
                    label,
                    action.NewTakeProfit,
                    action.OldTakeProfit,
                    position.StopLoss ?? 0,
                    Time);
                continue;
            }

            Print(BBrokenPositionHandler.FormatMoveTpRejectLog(
                in action, mod.Error ?? "rejected", cfg.MoveTpRejectMode));
            if (cfg.MoveTpRejectMode == BBrokenTpRejectFallbackMode.CloseImmediately)
            {
                _closeReasonAudit.RecordManualClose(label, "B-broken-tp-reject");
                CloseTrackedPosition(label, "B-broken");
            }
        }
    }

    /// <summary>
    /// Re-point each live setup's TP to the confirmed swing-C edge once C appears. The entry was
    /// placed with the default (RR) TP; this is the only place that swaps it to the C edge.
    /// </summary>
    void UpdateSwingCTakeProfits()
    {
        var m5State = ResolveM5FallbackState();
        var swingCCrossTfDefault = BuildSwingCCrossTfContext();

        foreach (var setup in System.Linq.Enumerable.ToArray(_book.Entries))
        {
            var label = setup.Context.Label;
            var structState = R1R2M5Structure.ResolveMonitorState(setup.Context, _host.State, m5State);
            var swingCCrossTf = setup.Context.UsesM5SwingC ? null : swingCCrossTfDefault;

            var action = SwingCEdgeTpUpdater.Evaluate(
                setup,
                structState,
                _swingCTpCfg.WantsLiveTpUpdate,
                _swingCTpCfg.UseSwingCEdgeTakeProfit,
                _swingCTpCfg.RuleSlotIndices,
                Symbol.TickSize,
                _swingCTpCfg.TpMode,
                RewardRisk,
                _swingCTpCfg.SkipIfSwingCRrBelowBase,
                swingCCrossTf);

            if (action.ShouldCancel)
            {
                Print(SwingCEdgeTpUpdater.FormatCancelLog(label, in action));
                var cancelPending = _exec.TryGetPendingByLabel(label);
                if (cancelPending != null)
                {
                    if (_exec.CancelPendingByLabel(label, SwingCEdgeTpUpdater.CancelReasonSwingCRrBelowBase))
                        RemoveTrackedPending(label);
                }
                else
                {
                    var cancelPosition = _exec.TryGetPositionByLabel(label);
                    if (cancelPosition != null)
                    {
                        _closeReasonAudit.RecordManualClose(label, SwingCEdgeTpUpdater.CancelReasonSwingCRrBelowBase);
                        CloseTrackedPosition(label, SwingCEdgeTpUpdater.CancelReasonSwingCRrBelowBase);
                    }
                }

                continue;
            }

            if (!action.ShouldUpdate)
                continue;

            var pending = _exec.TryGetPendingByLabel(label);
            if (pending != null)
            {
                var mod = _exec.TryModifyPendingTakeProfit(pending, action.NewTakeProfit);
                Print(SwingCEdgeTpUpdater.FormatUpdateLog(label, setup.Context.IsBuy, in action, placedOnPending: true, mod.Success));
                if (mod.Success)
                {
                    _book.MarkTpUpdatedToSwingC(label, action.NewTakeProfit);
                    _execAudit.UpdateProtectionTakeProfit(label, action.NewTakeProfit);
                }
                else
                    Print(SwingCEdgeTpUpdater.FormatUpdateRejectLog(label, in action, mod.Error ?? "rejected"));
                continue;
            }

            var position = _exec.TryGetPositionByLabel(label);
            if (position == null)
                continue;

            var modPos = _exec.TryModifyTakeProfit(position, action.NewTakeProfit);
            Print(SwingCEdgeTpUpdater.FormatUpdateLog(label, setup.Context.IsBuy, in action, placedOnPending: false, modPos.Success));
            if (modPos.Success)
            {
                _book.MarkTpUpdatedToSwingC(label, action.NewTakeProfit);
                _execAudit.UpdateProtectionTakeProfit(label, action.NewTakeProfit);
            }
            else
                Print(SwingCEdgeTpUpdater.FormatUpdateRejectLog(label, in action, modPos.Error ?? "rejected"));
        }
    }

    void SyncObstacleTfStatesForCancel()
    {
        if (Bars.Count < 2)
            return;

        var closeIdx = Bars.Count - 2;
        var chartClose = Bars.OpenTimes[closeIdx].Add(EstimateChartBarPeriod());
        _host.SyncGateTfStates(chartClose, realtimeEdge: false);
    }

    void EvaluatePendingInvalidations()
    {
        foreach (var order in _exec.OurPendingOrders().ToArray())
        {
            if (order.Label is null)
                continue;
            if (!_book.TryGetContext(order.Label, out var ctx))
                continue;

            if (EnableCancelOnPostBPushObstacle && _postBCancelCfg.AppliesToSlot(ctx.RuleSlot))
            {
                var postB = PostBPushObstacleCancelRule.Evaluate(in ctx, _host, in _postBCancelCfg);
                _slotAudit.AddPendingCancelPostBPushObstacleCrossTfSelfSkipped(
                    ctx.RuleSlot, postB.CrossTfSelfIdentitySkipped);
                if (postB.ShouldCancel)
                {
                    Print(PostBPushObstacleCancelRule.FormatCancelLog(in ctx, in postB));
                    if (_exec.CancelPendingByLabel(order.Label, PostBPushObstacleCancelRule.CancelReason))
                    {
                        _slotAudit.RecordPendingCancelPostBPushObstacle(ctx.RuleSlot);
                        _slotAudit.RecordPendingCancelPostBPushObstacleMatchKind(
                            ctx.RuleSlot,
                            sameTf: postB.MatchKind == PostBPushCancelMatchKind.SameTf);
                        RemoveTrackedPending(order.Label);
                        DrawFireChartCancel(in ctx, PostBPushObstacleCancelRule.CancelReason);
                    }
                    continue;
                }
            }

            if (EnableCancelOnNearDZone && _nearDCancelCfg.AppliesToSlot(ctx.RuleSlot))
            {
                var nearD = NearDZoneCancelRule.EvaluateWithProbe(in ctx, ResolvePendingCancelProbe(ctx.IsBuy, PendingCancelProbeKind.NearD));
                if (nearD.ShouldCancel)
                {
                    Print(NearDZoneCancelRule.FormatCancelLog(in ctx, in nearD));
                    if (_exec.CancelPendingByLabel(order.Label, NearDZoneCancelRule.CancelReason))
                    {
                        RemoveTrackedPending(order.Label);
                        DrawFireChartCancel(in ctx, NearDZoneCancelRule.CancelReason);
                    }
                    // The split sibling leg (C ↔ D, same SwingBPivotBar) will be processed in its own
                    // foreach iteration (or already in this same pass if still pending) and naturally
                    // hits the same gate ⇒ pair cancel without explicit lookup.
                    continue;
                }
            }

            if (EnableCancelOnNearBThenCTp && _nearBCancelCfg.AppliesToSlot(ctx.RuleSlot))
            {
                // Step 1: update latch if price touches near-B zone.
                var touchResult = NearBThenCTpCancelRule.EvaluateTouchWithProbe(
                    in ctx, ResolvePendingCancelProbe(ctx.IsBuy, PendingCancelProbeKind.NearBTouchB));
                if (touchResult.TouchedB)
                    _book.MarkNearBZoneTouched(order.Label);

                // Step 2: cancel when latch set AND price has reached TP C.
                if (_book.TryGetSetup(order.Label, out var tracked))
                {
                    var cancelResult = NearBThenCTpCancelRule.EvaluateCancelWithProbe(
                        in ctx, tracked.NearBZoneTouched,
                        ResolvePendingCancelProbe(ctx.IsBuy, PendingCancelProbeKind.NearBReachTpC));
                    if (cancelResult.ShouldCancel)
                    {
                        Print(NearBThenCTpCancelRule.FormatCancelLog(in ctx, in cancelResult));
                        if (_exec.CancelPendingByLabel(order.Label, NearBThenCTpCancelRule.CancelReason))
                        {
                            RemoveTrackedPending(order.Label);
                            DrawFireChartCancel(in ctx, NearBThenCTpCancelRule.CancelReason);
                        }
                        // Sibling leg (C ↔ D) has same TpCLegPrice → will hit same gate in its iteration.
                        continue;
                    }
                }
            }

            if (EnableCancelHlM15BcAbRatio && _hlM15BcAbCancelCfg.AppliesToSlot(ctx.RuleSlot))
            {
                var bcAb = HlM15BcAbRatioCancelRule.Evaluate(in ctx, _host, in _hlM15BcAbCancelCfg);
                if (!string.IsNullOrEmpty(bcAb.VerboseSkip))
                    Log($"[L6BT] {bcAb.VerboseSkip}");
                if (bcAb.ShouldCancel)
                {
                    Print(HlM15BcAbRatioCancelRule.FormatCancelLog(in ctx, in bcAb, in _hlM15BcAbCancelCfg));
                    if (_exec.CancelPendingByLabel(order.Label, HlM15BcAbRatioCancelRule.CancelReason))
                    {
                        _slotAudit.RecordPendingCancelHlM15BcAbRatio(ctx.RuleSlot);
                        RemoveTrackedPending(order.Label);
                        DrawFireChartCancel(in ctx, HlM15BcAbRatioCancelRule.CancelReason);
                    }
                    continue;
                }
            }

            if (EnableCancelOnSwingCBrokenWaitD && _swingCBrokenWaitDCancelCfg.AppliesToSlot(ctx.RuleSlot))
            {
                var cBroken = SwingCBrokenWaitDCancelRule.Evaluate(in ctx, _host);
                if (cBroken.ShouldCancel)
                {
                    Print(SwingCBrokenWaitDCancelRule.FormatCancelLog(in ctx, in cBroken));
                    if (_exec.CancelPendingByLabel(order.Label, SwingCBrokenWaitDCancelRule.CancelReason))
                    {
                        RemoveTrackedPending(order.Label);
                        DrawFireChartCancel(in ctx, SwingCBrokenWaitDCancelRule.CancelReason);
                        CancelSwingCBrokenWaitDSplitSibling(order.Label);
                    }
                    continue;
                }
            }

            if (EnableCancelOnStalePendingSwing && _stalePendingSwingCancelCfg.AppliesToSlot(ctx.RuleSlot))
            {
                var stale = StalePendingSwingCancelRule.Evaluate(in ctx, _host, in _stalePendingSwingCancelCfg);
                if (stale.ShouldCancel)
                {
                    Print(StalePendingSwingCancelRule.FormatCancelLog(in ctx, in stale));
                    if (_exec.CancelPendingByLabel(order.Label, StalePendingSwingCancelRule.CancelReason))
                    {
                        RemoveTrackedPending(order.Label);
                        DrawFireChartCancel(in ctx, StalePendingSwingCancelRule.CancelReason);
                    }
                    continue;
                }
            }
        }
    }

    // ── Post-fill C-rollover (SplitTpAtCAndD only) ──────────────────────────

    static readonly EntrySlZoneGateConfig s_rolloverScanCfg = new()
    {
        IncludeKeyLevels = true,
        IncludeOrderBlocks = true,
        IncludeBrokenKeyLevels = true,
        RequireSameColor = false,
    };

    /// <summary>Record the chart bar at which each tracked split order first becomes an open position.</summary>
    void CaptureFillBars()
    {
        if (!EnableTrading || !EnablePostFillCRollover)
            return;

        var bar = _host.ChartShellLastBarIndex;
        foreach (var setup in System.Linq.Enumerable.ToArray(_book.Entries))
        {
            if (setup.FillBarIndex >= 0)
                continue;
            var label = setup.Context?.Label;
            if (string.IsNullOrEmpty(label))
                continue;
            if (_exec.TryGetPositionByLabel(label!) == null)
                continue;

            _book.MarkFillBar(label!, bar);
            if (label!.EndsWith("|C", StringComparison.Ordinal) || label.EndsWith("|D", StringComparison.Ordinal))
                Print(PostFillCRolloverRule.FormatFillBarLog(label, bar));
        }
    }

    static string RolloverWatchKey(int ruleSlot, int swingBBar) => $"{ruleSlot}:{swingBBar}";

    /// <summary>
    /// OnTick gate: once a split leg has filled and price reaches entry + 0.25R profit,
    /// roll the target down (BUY) / up (SELL) to a freshly-formed M15 obstacle below C. Closes both legs
    /// re-enters with new C = obstacle and new D = old C (default) or original D when keepOriginalTpD is enabled.
    /// </summary>
    void EvaluatePostFillCRollovers()
    {
        if (_host == null || !EnableTrading || !EnablePostFillCRollover)
            return;
        if (_swingCTpCfg.TpMode != SwingCTpMode.SplitTpAtCAndD)
            return;

        var chartTf = Loop6ChartTimeframeToken.FromBarsTimeFrame(TimeFrame);
        if (chartTf != "15")
            return; // rule scoped to M15 charts only

        var processed = new HashSet<(int, int)>();
        foreach (var setup in System.Linq.Enumerable.ToArray(_book.Entries))
        {
            var ctx = setup.Context;
            if (ctx is null)
                continue;
            if (!_postFillCRolloverSlots.Contains(ctx.RuleSlot))
                continue;
            if (ctx.TpCLegPrice <= 0)
                continue; // not a split plan
            if (!ctx.Label.EndsWith("|C", StringComparison.Ordinal))
                continue; // only the C leg carries the genuine C-zone geometry

            var key = (ctx.RuleSlot, ctx.SwingBPivotBar);
            if (!processed.Add(key))
                continue;

            var dLabel = ReplaceLegSuffix(ctx.Label, "D");
            var openPos = _exec.TryGetPositionByLabel(ctx.Label) ?? _exec.TryGetPositionByLabel(dLabel);
            if (openPos is null)
                continue; // nothing filled yet

            var entry = ctx.EntryPrice;
            var sl = ctx.StopLoss;
            var trigger = PostFillCRolloverRule.ComputeTriggerPrice(entry, sl, ctx.IsBuy);
            var triggerReached = ctx.IsBuy ? Symbol.Bid >= trigger : Symbol.Ask <= trigger;
            var watchKey = RolloverWatchKey(ctx.RuleSlot, ctx.SwingBPivotBar);

            var fillBar = setup.FillBarIndex;
            if (fillBar < 0)
            {
                if (_book.TryGetSetup(dLabel, out var dSetup) && dSetup.FillBarIndex >= 0)
                    fillBar = dSetup.FillBarIndex;
                else
                    fillBar = _host.ChartShellLastBarIndex;
            }

            if (!triggerReached || trigger <= 0)
                continue;

            if (_rolloverBeLoggedKeys.Add(watchKey))
            {
                Print(PostFillCRolloverRule.FormatTriggerReachedLog(
                    ctx.Label, ctx.IsBuy, entry, sl, trigger,
                    Symbol.Bid, Symbol.Ask, fillBar, ctx.SwingCEdgeTop, ctx.SwingCEdgeBottom));
            }

            var zones = ScanM15RolloverZones(chartTf);
            var decision = PostFillCRolloverRule.Evaluate(
                ctx.IsBuy, ctx.EntryPrice, ctx.SwingCEdgeTop, ctx.SwingCEdgeBottom,
                fillBar, trigger, Symbol.Bid, Symbol.Ask, zones);

            if (!decision.ShouldRollover || decision.NewCZone is null)
            {
                if (decision.TriggerReached && _rolloverZoneSkipLoggedKeys.Add(watchKey))
                    Print(PostFillCRolloverRule.FormatSkipLog(ctx.Label, decision.Reason, zones.Count));
                continue;
            }

            _rolloverBeLoggedKeys.Remove(watchKey);
            _rolloverZoneSkipLoggedKeys.Remove(watchKey);
            ExecutePostFillCRollover(ctx, decision, dLabel, trigger);
        }
    }

    IReadOnlyList<ZoneCandidate> ScanM15RolloverZones(string chartTf)
    {
        _host.TryGetSeriesBuffer(chartTf, out var buf);
        return MultiTfZoneSnapshot.Collect(_host.State, chartTf, in s_rolloverScanCfg, buf);
    }

    void ExecutePostFillCRollover(
        PendingOrderContext cCtx,
        PostFillCRolloverDecision decision,
        string dLabel,
        double trigger)
    {
        var isBuy = cCtx.IsBuy;
        var newZone = decision.NewCZone!;
        var keepOriginalTpD = PostFillCRolloverKeepOriginalTpD;
        PendingOrderContext? dCtx = null;
        if (keepOriginalTpD && _book.TryGetSetup(dLabel, out var dSetup))
            dCtx = dSetup.Context;

        var dMode = keepOriginalTpD && dCtx is not null && dCtx.SwingCEdgeTop > 0 && dCtx.SwingCEdgeBottom > 0
            ? "keep-original-D"
            : "oldC-as-D";
        if (keepOriginalTpD && dCtx is null)
            Print($"[L6BT] ROLLOVER keepOriginalTpD=ON but D context missing for {dLabel} — fallback oldC-as-D");

        Print($"[L6BT] POST-FILL C-ROLLOVER TRIGGER R{cCtx.RuleSlot + 1} B{cCtx.SwingBPivotBar} {(isBuy ? "BUY" : "SELL")} " +
              $"trigger={trigger:0.#####} profitR={PostFillCRolloverRule.TriggerProfitR:0.##} " +
              $"newC=[{newZone.Low:0.#####}..{newZone.High:0.#####}] " +
              $"dMode={dMode} " +
              (dMode == "keep-original-D"
                  ? $"keepD=[{dCtx!.SwingCEdgeBottom:0.#####}..{dCtx.SwingCEdgeTop:0.#####}] "
                  : $"oldC->D=[{cCtx.SwingCEdgeBottom:0.#####}..{cCtx.SwingCEdgeTop:0.#####}] ") +
              $"detail={decision.Reason}");

        // 1) Close both legs: open → market, pending → cancel.
        CloseOrCancelRolloverLeg(cCtx.Label);
        CloseOrCancelRolloverLeg(dLabel);

        // 2) New C = fresh obstacle; new D = old C zone (default) or original D zone (keepOriginalTpD).
        var newC = new SwingCEdgeResult
        {
            PivotIndex = newZone.PivotIndex ?? -1,
            PivotBar = newZone.PivotBar ?? -1,
            PivotType = isBuy ? SwingBResolver.TypeHigh : SwingBResolver.TypeLow,
            TakeProfit = isBuy ? newZone.Low : newZone.High,
            EdgeTop = newZone.High,
            EdgeBottom = newZone.Low,
            EdgeKind = newZone.Source.ToString(),
            IsOb = newZone.Source == ZoneSourceKind.OrderBlock,
            TfToken = "15",
        };
        SwingCEdgeResult newD;
        if (dMode == "keep-original-D")
        {
            newD = new SwingCEdgeResult
            {
                PivotIndex = dCtx!.SwingCPivotIndex,
                PivotBar = dCtx.SwingCPivotBar,
                PivotType = isBuy ? SwingBResolver.TypeHigh : SwingBResolver.TypeLow,
                TakeProfit = isBuy ? dCtx.SwingCEdgeBottom : dCtx.SwingCEdgeTop,
                EdgeTop = dCtx.SwingCEdgeTop,
                EdgeBottom = dCtx.SwingCEdgeBottom,
                EdgeKind = "rollover-keepD",
                TfToken = "15",
            };
        }
        else
        {
            newD = new SwingCEdgeResult
            {
                PivotIndex = cCtx.SwingCPivotIndex,
                PivotBar = cCtx.SwingCPivotBar,
                PivotType = isBuy ? SwingBResolver.TypeHigh : SwingBResolver.TypeLow,
                TakeProfit = isBuy ? cCtx.SwingCEdgeBottom : cCtx.SwingCEdgeTop,
                EdgeTop = cCtx.SwingCEdgeTop,
                EdgeBottom = cCtx.SwingCEdgeBottom,
                EdgeKind = "rollover-oldC",
                TfToken = "15",
            };
        }

        // 3) Pinned B from stored context (original entry/SL keylevel).
        var b = new SwingBResult
        {
            PivotIndex = cCtx.SwingBPivotIndex,
            PivotBar = cCtx.SwingBPivotBar,
            Type = isBuy ? SwingBResolver.TypeHigh : SwingBResolver.TypeLow,
            KeyTop = cCtx.SwingBKeyHigh,
            KeyBottom = cCtx.SwingBKeyLow,
            Source = SwingBSource.Active,
        };

        // 4) Synthetic fire event for the re-entry.
        var chartTf = Loop6ChartTimeframeToken.FromBarsTimeFrame(TimeFrame);
        var ev = new CompoundFireEvent(
            cCtx.RuleSlot, "rollover", cCtx.Direction, chartTf,
            _host.ChartShellLastBarIndex, Server.Time, Symbol.Bid, _host.ChartShellLastBarIndex);

        var cfg = BuildMapperConfig();
        // post-fill C-rollover: SlEvalTime = current (just-closed) bar.
        var evCfg = ApplyH4SlConfig(in cfg, cCtx.Direction == SignalDirection.Buy, ResolveSlEvalTimeNow());
        var plans = CompoundFireTradeMapper.TryBuildPlans(
            in ev, _host.State, in evCfg, out var reason, ResolveAtrSeriesForSl,
            pinnedB: b, injectedSplitC: newC, injectedSplitD: newD, rolloverMode: true);

        Print($"[L6BT] POST-FILL C-ROLLOVER RE-ENTER R{cCtx.RuleSlot + 1} B{cCtx.SwingBPivotBar} mapperReason={reason} plans={plans.Count}");

        if (plans.Count == 0)
        {
            Print(PostFillCRolloverRule.FormatReenterSkipLog(cCtx.Label, reason));
            return;
        }

        foreach (var plan in plans)
        {
            Print(PostFillCRolloverRule.FormatReenterPlanLog(
                plan.Label, plan.TpLegTag ?? "", plan.EntryLimit, plan.StopLoss, plan.TakeProfit));
        }

        ExecuteResolvedPlans(in ev, plans, in cfg, "ROLLOVER");
    }

    void CloseOrCancelRolloverLeg(string label)
    {
        if (_exec.TryGetPositionByLabel(label) != null)
        {
            Print(PostFillCRolloverRule.FormatCloseLegLog(label));
            _closeReasonAudit.RecordManualClose(label, PostFillCRolloverRule.CloseReason);
            CloseTrackedPosition(label, PostFillCRolloverRule.CloseReason);
        }
        else if (_exec.HasPendingByLabel(label))
        {
            Print(PostFillCRolloverRule.FormatCancelLegLog(label));
            if (_exec.CancelPendingByLabel(label, PostFillCRolloverRule.CloseReason))
                RemoveTrackedPending(label);
        }
    }

    static string ReplaceLegSuffix(string label, string newLeg)
    {
        var idx = label.LastIndexOf('|');
        return idx >= 0 ? label.Substring(0, idx + 1) + newLeg : label;
    }

    protected override void OnStop()
    {
        _h4SlPlanOverlay?.Clear();
        _h4SlDebugPanel?.Clear(Chart);

        Print("[L6BT] Slot summary:");
        for (var slot = 0; slot < SlotAuditTracker.SlotCount; slot++)
        {
            var (fire, plan, gatePass, gateSkip, orders) = _slotAudit.Get(slot);
            var pendingCancelObstacle = _slotAudit.GetPendingCancelPostBPushObstacle(slot);
            var pendingCancelObstacleSameTf = _slotAudit.GetPendingCancelPostBPushObstacleSameTf(slot);
            var pendingCancelObstacleCrossTf = _slotAudit.GetPendingCancelPostBPushObstacleCrossTf(slot);
            var pendingCancelObstacleCrossTfSelfSkipped = _slotAudit.GetPendingCancelPostBPushObstacleCrossTfSelfSkipped(slot);
            var pendingCancelBcAb = _slotAudit.GetPendingCancelHlM15BcAbRatio(slot);
            Print($"[L6BT] R{slot + 1} fire={fire} plan={plan} gatePass={gatePass} gateSkip={gateSkip} orders={orders} pendingCancelPostBPushObstacle={pendingCancelObstacle} pendingCancelHlM15BcAbRatio={pendingCancelBcAb}");
            Print($"[L6BT] R{slot + 1} cancelPostBPushObstacle.byMatchKind: sameTf={pendingCancelObstacleSameTf} crossTf={pendingCancelObstacleCrossTf} crossTfSelfSkipped={pendingCancelObstacleCrossTfSelfSkipped}");
        }

        var r3r6configured = !string.IsNullOrWhiteSpace(CompoundRule3)
                          || !string.IsNullOrWhiteSpace(CompoundRule4)
                          || !string.IsNullOrWhiteSpace(CompoundRule5)
                          || !string.IsNullOrWhiteSpace(CompoundRule6);

        if (r3r6configured)
        {
            bool anyR3R6Fired = false;
            for (var s = 2; s < 6; s++)
            {
                var (f, _, _, _, _) = _slotAudit.Get(s);
                if (f > 0) { anyR3R6Fired = true; break; }
            }

            if (!anyR3R6Fired)
            {
                Print("[L6BT] DIAG: R3-R6 configured but fire=0 — final condition snapshot:");
                DumpR3R6ConditionDebug();
            }
        }

        HistoryCloseReasonClassifier.AuditHistory(History, LabelPrefix, SymbolName, _closeReasonAudit);
        _closeReasonAudit.PrintSummary(Print);
        FinalizeExecutionAuditCloses();
        _execAudit.PrintExecutionSummary(Print, CountHistoryClosed());

        if (_csvLog != null)
            Print($"[L6BT] Detailed CSV logs: {_csvLog.RunDirectory}");

        Positions.Closed -= OnPositionClosedAudit;
        Positions.Opened -= OnPositionOpenedAudit;

        try { _host?.Dispose(); }
        catch { /* ignore on stop */ }
    }

    void DumpR3R6ConditionDebug()
    {
        if (_host == null) return;

        var bar = _host.ChartShellLastBarIndex;
        Print($"[L6BT] R3-R6 cond debug at chartBar={bar} onBar#={_onBarCount}:");

        // M15 event conditions (chart shell)
        Print($"[L6BT]   M15:condBuyEventHLM15   = {_host.FormatConditionStatus("15", AlertConditionId.CondBuyEventHLM15)}");
        Print($"[L6BT]   M15:condSellEventHLM15  = {_host.FormatConditionStatus("15", AlertConditionId.CondSellEventHLM15)}");
        Print($"[L6BT]   M15:condBuyEventNGM15   = {_host.FormatConditionStatus("15", AlertConditionId.CondBuyEventNGM15)}");
        Print($"[L6BT]   M15:condSellEventNGM15  = {_host.FormatConditionStatus("15", AlertConditionId.CondSellEventNGM15)}");

        // M15 state conditions
        Print($"[L6BT]   M15:canBuyReal           = {_host.FormatConditionStatus("15", AlertConditionId.CanBuyReal)}");
        Print($"[L6BT]   M15:canSellReal           = {_host.FormatConditionStatus("15", AlertConditionId.CanSellReal)}");

        // M5 state (R3-R6 preset uses canBuyReal/canSellReal — no M15-close-edge gate)
        Print($"[L6BT]   M5:canBuyReal            = {_host.FormatConditionStatus("5", AlertConditionId.CanBuyReal)}");
        Print($"[L6BT]   M5:canSellReal           = {_host.FormatConditionStatus("5", AlertConditionId.CanSellReal)}");

        // H1 / H4 states
        Print($"[L6BT]   H1:canBuyReal            = {_host.FormatConditionStatus("60", AlertConditionId.CanBuyReal)}");
        Print($"[L6BT]   H1:canSellReal           = {_host.FormatConditionStatus("60", AlertConditionId.CanSellReal)}");
        Print($"[L6BT]   H1:canBuyTouchM5         = {_host.FormatConditionStatus("60", AlertConditionId.CanBuyTouchM5)}");
        Print($"[L6BT]   H1:canSellTouchM5        = {_host.FormatConditionStatus("60", AlertConditionId.CanSellTouchM5)}");
        Print($"[L6BT]   H4:canBuyReal            = {_host.FormatConditionStatus("240", AlertConditionId.CanBuyReal)}");
        Print($"[L6BT]   H4:canSellReal           = {_host.FormatConditionStatus("240", AlertConditionId.CanSellReal)}");
        Print($"[L6BT]   H4:canBuyTouchM5         = {_host.FormatConditionStatus("240", AlertConditionId.CanBuyTouchM5)}");
        Print($"[L6BT]   H4:canSellTouchM5        = {_host.FormatConditionStatus("240", AlertConditionId.CanSellTouchM5)}");

        Print($"[L6BT]   SyncMode={CompoundSyncMode} EventWindow={CompoundEventValidBars} InjectM15Edge={InjectM15CloseEdgeSignal}");
        Print($"[L6BT]   MinTfToken={_host.CompoundMinTfToken} MinTfMinutes={_host.CompoundMinTfMinutes}");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────
    void Log(string msg)
    {
        if (EnableLog)
            Print(msg);
    }

    static int VnHourToUtc(int vnHour) => ((vnHour - 7) % 24 + 24) % 24;

    bool IsEntryBlockedByBangkokCutoff() =>
        EntryCutoffGuard.IsEntryBlocked(
            Server.TimeInUtc,
            UseEntryCutoffBangkok,
            EntryCutoffHourBangkok,
            StopHourVn);

    /// <summary>True when <paramref name="hour"/> is in the cyclic interval [start, stop).</summary>
    static bool InWindow(int hour, int start, int stop)
    {
        if (start == stop) return true;          // full day
        if (start < stop) return hour >= start && hour < stop;
        return hour >= start || hour < stop;     // wraps midnight
    }

    double ResolveSpreadPips() => ResolveSpreadSnapshot().Pips;

    SpreadResolution ResolveSpreadSnapshot() =>
        SpreadResolver.Resolve(
            SpreadPipsOverride,
            Symbol.Spread,
            Symbol.PipSize,
            Symbol.TickSize,
            SymbolName,
            _sizingProfile.AssetType);

    Loop6AnalyticsContext BuildAnalyticsContext()
    {
        var spread = ResolveSpreadSnapshot();
        return new Loop6AnalyticsContext
        {
            RunId = _csvLog?.RunId ?? "",
            Symbol = SymbolName,
            Timeframe = TimeFrame.ToString(),
            ServerTimeUtc = Server.TimeInUtc,
            Bid = Symbol.Bid,
            Ask = Symbol.Ask,
            SymbolSpreadPrice = Symbol.Spread,
            PipSize = Symbol.PipSize > 0 ? Symbol.PipSize : Symbol.TickSize,
            TickSize = Symbol.TickSize,
            Spread = spread,
            SpreadOverrideInput = SpreadPipsOverride,
            AccountEquity = Account.Equity,
            RiskPercent = RiskPercent,
            ConfiguredRewardRisk = RewardRisk,
            EnableMinSlConstraint = EnableMinSlConstraint,
            MinSlBacktestMode = MinSlBacktestMode,
            MinSlSpreadMultiple = MinSlSpreadMultiple,
            MinSlPipsForex = MinSlPipsForex,
            AssetType = _sizingProfile.AssetType,
        };
    }

    string ResolveSpreadOverrideMode()
    {
        if (SpreadPipsOverride <= 0)
            return "Auto";
        return SpreadResolver.UsesPriceSpreadOverride(SymbolName, _sizingProfile.AssetType)
            ? "GoldPrice"
            : "Pips";
    }

    void LogCsvSkip(int ruleSlot, string direction, string code, string detail, int? chartBar = null) =>
        _csvLog?.LogSkip(BuildAnalyticsContext(), ruleSlot, direction, code, detail, chartBar: chartBar);

    void LogCsvSkip(
        int ruleSlot,
        string direction,
        string code,
        string detail,
        double rawEntry,
        double rawSl,
        double rawTp,
        double slStructurePips,
        int? chartBar = null) =>
        _csvLog?.LogSkip(BuildAnalyticsContext(), ruleSlot, direction, code, detail,
            rawEntry, rawSl, rawTp, slStructurePips, chartBar);

    void RegisterTradeExcursionOnOpen(Position pos)
    {
        if (pos.Label is null)
            return;

        var initialSl = pos.StopLoss ?? 0;
        if (_execAudit.TryGetPlanned(pos.Label, out var planned) && planned.PlannedSl > 0)
            initialSl = planned.PlannedSl;

        _tradeExcursions.OnOpened(
            pos.Label, pos.TradeType == TradeType.Buy,
            pos.EntryPrice, initialSl, AuditPipSize(),
            Server.TimeInUtc, Bars.Count - 1);
    }

    void UpdateTradeExcursionsOnBar()
    {
        if (!EnableTrading || Bars.Count < 1)
            return;

        var hi = Bars.HighPrices[Bars.Count - 1];
        var lo = Bars.LowPrices[Bars.Count - 1];
        foreach (var position in _exec.OurPositions())
        {
            if (position.Label != null)
                _tradeExcursions.UpdateBar(position.Label, hi, lo);
        }
    }

    void LogCsvClose(Position pos, string reason, ClosedTradeData data, in ExcursionSnapshot excursion, bool hasExcursion)
    {
        if (_csvLog == null || pos.Label is null)
            return;

        var ruleSlot = 0;
        if (_book.TryGetContext(pos.Label, out var bookCtx))
            ruleSlot = bookCtx.RuleSlot;
        else if (TryParseRuleSlotFromLabel(pos.Label) is int rs)
            ruleSlot = rs;

        _execAudit.TryGetPlanned(pos.Label, out var planned);
        var initialSl = planned.PlannedSl;
        var finalSl = pos.StopLoss ?? initialSl;
        var tp = pos.TakeProfit ?? planned.PlannedTp;
        var plannedRisk = planned.PlannedRiskUsd;

        var closeTime = data.HasData ? data.CloseTime : Server.TimeInUtc;

        _csvLog.LogClose(
            BuildAnalyticsContext(),
            pos.Label,
            ruleSlot,
            pos.TradeType == TradeType.Buy,
            reason,
            closeTime,
            pos.EntryPrice,
            data.HasData ? data.RawClose : pos.EntryPrice,
            initialSl,
            finalSl,
            tp,
            pos.VolumeInUnits,
            data.HasData ? data.GrossProfit : pos.GrossProfit,
            data.HasData ? data.Commission : pos.Commissions,
            data.HasData ? data.NetProfit : pos.NetProfit,
            plannedRisk,
            in excursion,
            Bars.Count - 1);
    }

    static string EffectiveCompoundRule(string ruleText, bool enabled) =>
        enabled ? CompoundAlertPresets.NormalizeNgEventTrigger(ruleText) : "";

    string FormatCompoundRuleEnableFlags() =>
        $"R1={(EnableCompoundRule1 ? "ON" : "OFF")} R2={(EnableCompoundRule2 ? "ON" : "OFF")} " +
        $"R3={(EnableCompoundRule3 ? "ON" : "OFF")} R4={(EnableCompoundRule4 ? "ON" : "OFF")} " +
        $"R5={(EnableCompoundRule5 ? "ON" : "OFF")} R6={(EnableCompoundRule6 ? "ON" : "OFF")}";

    static int? TryParseRuleSlotFromLabel(string label)
    {
        var parts = label.Split('|');
        foreach (var part in parts)
        {
            if (part.Length >= 2 && part[0] == 'R' && int.TryParse(part.AsSpan(1), out var rNum) && rNum >= 1)
                return rNum - 1;
        }
        return null;
    }

    void LogSpreadAtFill(Position pos)
    {
        if (pos.Label is null)
            return;

        var fillSpread = ResolveSpreadSnapshot();
        if (_execAudit.TryGetPlanned(pos.Label, out var planned))
        {
            Print(SpreadTelemetryLog.FormatFill(
                pos.Label,
                planned.SpreadPipsAtPlan,
                string.IsNullOrWhiteSpace(planned.SpreadSourceAtPlan)
                    ? SpreadTelemetrySources.Symbol
                    : planned.SpreadSourceAtPlan,
                fillSpread.Pips,
                Symbol.Bid,
                Symbol.Ask,
                pos.EntryPrice));
        }
        else
        {
            Print(SpreadTelemetryLog.FormatFill(
                pos.Label,
                0,
                "unknown",
                fillSpread.Pips,
                Symbol.Bid,
                Symbol.Ask,
                pos.EntryPrice));
        }
    }

    EntrySlZoneGateConfig BuildGateConfig() => new()
    {
        GatedSlotIndices = EntrySlZoneGateConfig.ParseRulesMask(
            EntrySlZoneGateRulesMask,
            EnableLog ? (Action<string>)Log : null),
        TfTokens = EntrySlZoneGateConfig.ParseTfTokens(EntrySlZoneGateTfTokens),
        IncludeKeyLevels = IncludeKeyLevels,
        IncludeOrderBlocks = IncludeOrderBlocks,
        IncludeBrokenKeyLevels = IncludeBrokenKeyLevels,
        OverlapTolerancePips = OverlapTolerancePips,
        RequireSameColor = RequireSameColor,
        ExcludeSetupSwingBKeyLevel = ExcludeSetupSwingBKeyLevel,
        M5ConfluenceObOnly = M5ConfluenceObOnly,
        PipSize = Symbol.PipSize > 0 ? Symbol.PipSize : Symbol.TickSize,
    };

    PostBPushObstacleCancelRuleConfig BuildPostBPushCancelConfig() => new()
    {
        RuleSlotIndices = PostBPushObstacleCancelRuleConfig.ParseRulesMask(
            CancelPostBPushObstacleRulesMask,
            EnableLog ? (Action<string>)Log : null),
        TfTokens = EntrySlZoneGateConfig.ParseTfTokens(CancelPostBPushObstacleTfTokens),
        IncludeKeyLevels = CancelPostBPushObstacleIncludeKeyLevels,
        IncludeBrokenKeyLevels = CancelPostBPushObstacleIncludeBrokenKeyLevels,
        IncludeOrderBlocks = CancelPostBPushObstacleIncludeOrderBlocks,
        TolerancePips = CancelPostBPushObstacleTolerancePips,
        PipSize = Symbol.PipSize > 0 ? Symbol.PipSize : Symbol.TickSize,
    };

    NearDZoneCancelConfig BuildNearDZoneCancelConfig() => new()
    {
        RuleSlotIndices = PostBPushObstacleCancelRuleConfig.ParseRulesMask(
            CancelNearDZoneRulesMask,
            EnableLog ? (Action<string>)Log : null),
    };

    NearBThenCTpCancelConfig BuildNearBThenCTpCancelConfig() => new()
    {
        RuleSlotIndices = PostBPushObstacleCancelRuleConfig.ParseRulesMask(
            CancelNearBThenCTpRulesMask,
            EnableLog ? (Action<string>)Log : null),
    };

    SwingCBrokenWaitDCancelConfig BuildSwingCBrokenWaitDCancelConfig() => new()
    {
        RuleSlotIndices = PostBPushObstacleCancelRuleConfig.ParseRulesMask(
            CancelSwingCBrokenWaitDRulesMask,
            EnableLog ? (Action<string>)Log : null),
    };

    StalePendingSwingCancelConfig BuildStalePendingSwingCancelConfig() => new()
    {
        RuleSlotIndices = PostBPushObstacleCancelRuleConfig.ParseRulesMask(
            CancelStalePendingSwingRulesMask,
            EnableLog ? (Action<string>)Log : null),
        ActiveSwingThreshold = CancelStalePendingSwingThreshold,
    };

    HlM15BcAbRatioCancelRuleConfig BuildHlM15BcAbCancelConfig() => new()
    {
        RuleSlotIndices = HlM15BcAbRatioCancelRuleConfig.ParseRulesMask(
            CancelHlM15BcAbRatioRulesMask,
            EnableLog ? (Action<string>)Log : null),
        BcAbRatioThreshold = CancelHlM15BcAbRatioThreshold,
        VerboseSkipLog = CancelHlM15BcAbRatioVerbose,
    };

    void SyncAtrAdjustTfState()
    {
        if (Bars.Count < 2)
            return;

        var tf = string.IsNullOrWhiteSpace(AtrAdjustTf) ? "15" : AtrAdjustTf.Trim();
        var chartTf = Loop6ChartTimeframeToken.FromBarsTimeFrame(TimeFrame);
        if (string.Equals(tf, chartTf, StringComparison.Ordinal))
            return;

        var closeIdx = Bars.Count - 2;
        var chartClose = Bars.OpenTimes[closeIdx].Add(EstimateChartBarPeriod());
        _host.EnsureGateTfEngines(new[] { tf }, BuildSnapshot());
        _host.SyncGateTfStates(chartClose, realtimeEdge: false);
    }

    SeriesBuffer? ResolveAtrSeriesForSl(string tfToken)
    {
        if (_host.TryGetSeriesBuffer(tfToken, out var buffer))
            return buffer;
        return null;
    }

    AtrSlWidthAdjustConfig BuildAtrSlWidthConfig() => new()
    {
        UseAtrAdjustedSlWidthMultiplier = UseAtrAdjustedSlWidthMultiplier,
        BaseSlWidthMult = BaseSlWidthMult,
        AtrAdjustTf = AtrAdjustTf,
        AtrAdjustPeriod = AtrAdjustPeriod,
        AtrBaselinePeriod = AtrBaselinePeriod,
        AtrAdjustmentFactor = AtrAdjustmentFactor,
        MinDynamicSlWidthMult = MinDynamicSlWidthMult,
        MaxDynamicSlWidthMult = MaxDynamicSlWidthMult,
        AtrAdjustUseClosedBar = AtrAdjustUseClosedBar,
        AtrAdjustFallbackToBase = AtrAdjustFallbackToBase,
    };

    SwingCEdgeTpConfig BuildSwingCTpConfig() => new()
    {
        UseSwingCEdgeTakeProfit = UseSwingCEdgeTakeProfit,
        SwingCEdgeTpFallbackToRR = SwingCEdgeTpFallbackToRR,
        SkipIfSwingCRrBelowBase = SkipIfSwingCRrBelowBase,
        MoveEntryIfSwingCRrBelowBase = MoveEntryIfSwingCRrBelowBase,
        SplitDLegMinIncrementalRr = SplitDLegMinIncrementalRr,
        UpdateTpWhenSwingCConfirms = UpdateTpWhenSwingCConfirms,
        TpMode = SwingCEdgeTpConfig.ParseTpMode(SwingCTpModeParam),
        RuleSlotIndices = SwingCEdgeTpConfig.ParseRulesMask(
            SwingCEdgeTpRulesMask,
            EnableLog ? (Action<string>)Log : null),
    };

    BBrokenHandlerConfig BuildBBrokenConfig() => new()
    {
        EnableMoveTpToEntryOnBBroken = EnableMoveTpToEntryOnBBroken,
        OpenPositionMode = ParseBBrokenOpenMode(BBrokenOpenPositionMode),
        MoveTpRejectMode = ParseBBrokenRejectMode(BBrokenMoveTpRejectMode),
        TickSize = Symbol.TickSize,
    };

    static BBrokenOpenPositionActionMode ParseBBrokenOpenMode(string mode) =>
        string.Equals(mode, "CloseImmediately", StringComparison.OrdinalIgnoreCase)
            ? BBrokenOpenPositionActionMode.CloseImmediately
            : BBrokenOpenPositionActionMode.MoveTpToEntry;

    static BBrokenTpRejectFallbackMode ParseBBrokenRejectMode(string mode) =>
        string.Equals(mode, "LogOnly", StringComparison.OrdinalIgnoreCase)
            ? BBrokenTpRejectFallbackMode.LogOnly
            : BBrokenTpRejectFallbackMode.CloseImmediately;

    void SyncGateTfStatesForGate()
    {
        if (Bars.Count < 2)
            return;

        var closeIdx = Bars.Count - 2;
        var chartClose = Bars.OpenTimes[closeIdx].Add(EstimateChartBarPeriod());
        _host.SyncGateTfStates(chartClose, realtimeEdge: false);
    }

    // ── H4 SL mode helpers ──────────────────────────────────────────────────

    /// <summary>Feed closed HTF (incl. H4) bars into the shells/series buffers so the H4 SL resolver
    /// reads an up-to-date H4 SeriesBuffer. Only closed bars are fed (realtimeEdge=false).</summary>
    void SyncH4SlTfState()
    {
        if (Bars.Count < 2)
            return;
        var chartTf = Loop6ChartTimeframeToken.FromBarsTimeFrame(TimeFrame);
        if (string.Equals("240", chartTf, StringComparison.Ordinal))
            return;
        var closeIdx = Bars.Count - 2;
        var chartClose = Bars.OpenTimes[closeIdx].Add(EstimateChartBarPeriod());
        _host.SyncGateTfStates(chartClose, realtimeEdge: false);
    }

    /// <summary>"Now" for leak guard = close time of the just-closed chart bar (bot may know up to here).</summary>
    DateTime ResolveSlEvalTimeNow()
    {
        var closeIdx = Math.Max(0, Bars.Count - 2);
        return Bars.OpenTimes[closeIdx].Add(EstimateChartBarPeriod());
    }

    /// <summary>Upper bound (p75) của band fallback khi pool &lt; <see cref="H4SlMinSampleCount"/>.</summary>
    const double H4SlShortPoolFallbackMaxPips = 20.0;

    /// <summary>
    /// Populate the H4 SL fields on a per-event copy of <paramref name="baseCfg"/>: select the BUY/SELL
    /// break-depth pool; đủ <see cref="H4SlMinSampleCount"/> mẫu → stats thật, else band
    /// [<see cref="MinSlPipsForex"/>, <see cref="H4SlShortPoolFallbackMaxPips"/>] (mean = midpoint).
    /// </summary>
    TradeMapperConfig ApplyH4SlConfig(in TradeMapperConfig baseCfg, bool isBuy, DateTime slEvalTime)
    {
        if (!EnableH4SlMode)
            return baseCfg;
        if (!_host.TryGetSeriesBuffer("240", out var h4b) || h4b is null)
            return baseCfg;

        var statsPool = isBuy ? _buyStopBreakDepthStats : _sellStopBreakDepthStats;
        var poolCount = statsPool?.Count ?? 0;
        BreakDepthStats h4Stats;
        if (statsPool is not null && statsPool.TryGetStats(H4SlMinSampleCount, out var realStats))
        {
            h4Stats = realStats;
            if (EnableLog)
                Log($"[H4SL-STATS] symbol={SymbolName} side={(isBuy ? "BUY" : "SELL")} count={realStats.Count} " +
                    $"p25={realStats.P25:0.##} mean={realStats.Mean:0.##} p75={realStats.P75:0.##} pmin={realStats.PMin:0.##} pmax={realStats.PMax:0.##}");
        }
        else
        {
            h4Stats = BuildH4SlShortPoolFallbackStats(poolCount);
            if (EnableLog)
                Log($"[H4SL-STATS] symbol={SymbolName} side={(isBuy ? "BUY" : "SELL")} fallbackBand=true " +
                    $"count={poolCount} minN={H4SlMinSampleCount} p25={h4Stats.P25:0.##} mean={h4Stats.Mean:0.##} p75={h4Stats.P75:0.##}");
        }

        // H1 buffer cần cho: (a) R1/R2 khi H4SlR1R2UseH1, và (b) SlH1 hi-fallback trên mọi slot.
        var needH1 = H4SlR1R2UseH1 || H4SlHiFallbackStat == H4SlHiFallbackRef.SlH1;
        var h1Buf  = needH1 && _host.TryGetSeriesBuffer("60", out var h1b) ? h1b : null;

        return baseCfg with
        {
            EnableH4SlMode      = true,
            H4SeriesBuffer      = h4b,
            H1SeriesBuffer      = h1Buf,
            H4SlR1R2UseH1       = H4SlR1R2UseH1,
            H4BreakDepthStats   = h4Stats,
            SlEvalTime          = slEvalTime,
            H4SlMaxLookbackBars = H4SlMaxLookbackH4Bars,
            H4SlBandLowStat     = H4SlBandLowStat,
            H4SlBandHighStat    = H4SlBandHighStat,
            H4SlHiFallbackStat          = H4SlHiFallbackStat,
            H4SlHiFallbackSlH1NoBarStat = H4SlHiFallbackSlH1NoBarStat,
            H4SlRrGuard                 = H4SlRrGuard,
        };
    }

    BreakDepthStats BuildH4SlShortPoolFallbackStats(int sampleCount)
    {
        var minP = MinSlPipsForex > 0 ? MinSlPipsForex : 5.0;
        var maxP = Math.Max(minP, H4SlShortPoolFallbackMaxPips);
        return new BreakDepthStats(minP, (minP + maxP) / 2.0, maxP, minP, maxP, sampleCount);
    }

    /// <summary>
    /// Live collection: đọc <see cref="PivotStateStore.GetBreakDepthDist"/> đã latch Step A trên mỗi pivot
    /// (persist sau MAIN_BROKEN — không cần keybox). Dedup theo pivot bar index.
    /// </summary>
    void CollectBreakDepthStats()
    {
        var pivots = _host.State.Pivots;
        if (pivots is null)
            return;
        var pip = Symbol.PipSize > 0 ? Symbol.PipSize : Symbol.TickSize;
        if (pip <= 0)
            return;

        var newBuy = 0;
        var newSell = 0;
        for (var i = 0; i < pivots.Count; i++)
        {
            var barIdx = pivots.GetSnapshot(i).BarIndex;
            if (_breakDepthRecordedPivots.Contains(barIdx))
                continue;

            if (!BreakDepthPivotHelper.TryGetBreakDepthPips(pivots, i, pip, out var pips))
                continue;

            var type = pivots.GetTypeAt(i);
            if (type == 1)
            {
                _sellStopBreakDepthStats.Record(pips);
                newSell++;
                _breakDepthRecordedPivots.Add(barIdx);
            }
            else if (type == -1)
            {
                _buyStopBreakDepthStats.Record(pips);
                newBuy++;
                _breakDepthRecordedPivots.Add(barIdx);
            }
        }

        if ((newBuy > 0 || newSell > 0) && EnableLog)
            LogH4StatsSnapshot();
    }

    /// <summary>
    /// Seed 2 pool từ mọi pivot trong store có BreakDepthDist latched (warmup replay đã chạy qua Step A).
    /// Không phụ thuộc keybox hay flag broken — dist chỉ set khi break latch.
    /// </summary>
    void BackfillBreakDepthStatsFromHistory()
    {
        var pivots = _host.State.Pivots;
        if (pivots is null)
            return;
        var pip = Symbol.PipSize > 0 ? Symbol.PipSize : Symbol.TickSize;
        if (pip <= 0)
            return;

        int buyN = 0, sellN = 0, skipNoDist = 0;
        for (var i = 0; i < pivots.Count; i++)
        {
            if (!BreakDepthPivotHelper.TryGetBreakDepthPips(pivots, i, pip, out var pips))
            {
                skipNoDist++;
                continue;
            }

            var barIdx = pivots.GetSnapshot(i).BarIndex;
            var type = pivots.GetTypeAt(i);

            if (type == 1)
            {
                _sellStopBreakDepthStats.Record(pips);
                sellN++;
                _breakDepthRecordedPivots.Add(barIdx);
            }
            else if (type == -1)
            {
                _buyStopBreakDepthStats.Record(pips);
                buyN++;
                _breakDepthRecordedPivots.Add(barIdx);
            }
        }

        Print($"[H4SL-BACKFILL] symbol={SymbolName} scannedPivots={pivots.Count} buyRecorded={buyN} sellRecorded={sellN} skippedNoDist={skipNoDist}");
        LogH4StatsSnapshot();
    }

    /// <summary>Emit a <c>[H4SL-STATS]</c> line per side with current count + p25/mean/p75 (or insufficient).</summary>
    void LogH4StatsSnapshot()
    {
        LogH4StatsSnapshotForSide("BUY", _buyStopBreakDepthStats);
        LogH4StatsSnapshotForSide("SELL", _sellStopBreakDepthStats);
    }

    void LogH4StatsSnapshotForSide(string side, BreakDepthStatistics? pool)
    {
        if (pool is null)
            return;
        if (pool.TryGetStats(H4SlMinSampleCount, out var s))
            Print($"[H4SL-STATS] symbol={SymbolName} side={side} count={s.Count} p25={s.P25:0.##} mean={s.Mean:0.##} p75={s.P75:0.##} pmin={s.PMin:0.##} pmax={s.PMax:0.##}");
        else
        {
            var fb = BuildH4SlShortPoolFallbackStats(pool.Count);
            Print($"[H4SL-STATS] symbol={SymbolName} side={side} count={pool.Count} insufficient (min={H4SlMinSampleCount}) " +
                  $"→ fallbackBand=[{fb.P25:0.##},{fb.P75:0.##}]pip (MinSlPipsForex={MinSlPipsForex:0.##})");
        }
    }

    // ── H4 SL debug panel (on-chart) ────────────────────────────────────────

    void UpdateH4SlDebugPanel()
    {
        if (_h4SlDebugPanel == null)
            return;
        var bar = _host?.ChartShellLastBarIndex ?? (Bars.Count > 0 ? Bars.Count - 1 : 0);
        _h4SlPlanOverlay?.Tick(bar, IsPlanLegLive);
        _h4SlDebugPanel.Sync(Chart, BuildH4SlDebugLines());
    }

    bool IsPlanLegLive(string label) =>
        _exec.HasPendingByLabel(label) || _exec.TryGetPositionByLabel(label) != null;

    List<(string Text, Color Color)> BuildH4SlDebugLines()
    {
        var lines = new List<(string, Color)>(16);
        var headerColor = Color.FromArgb(255, 120, 200, 255);
        var okColor = Color.FromArgb(255, 100, 220, 120);
        var warnColor = Color.FromArgb(255, 255, 180, 60);
        var dimColor = Color.FromArgb(255, 160, 170, 190);
        var accentColor = Color.FromArgb(255, 180, 140, 255);

        var h4Ready = _host.TryGetSeriesBuffer("240", out _);
        lines.Add(($"=== H4 SL {SymbolName} ===", headerColor));
        var fbMin = MinSlPipsForex > 0 ? MinSlPipsForex : 5.0;
        lines.Add(($"H4 buf={(h4Ready ? "OK" : "MISSING")}  lookback={H4SlMaxLookbackH4Bars}H4  window={H4SlBreakDepthWindow}  minN={H4SlMinSampleCount}", dimColor));
        lines.Add(($"bandLo={H4SlBandLowStat}  bandHi={H4SlBandHighStat}  shortPoolFallback=[{fbMin:0.#},{H4SlShortPoolFallbackMaxPips:0.#}]pip", dimColor));
        lines.Add(("─ BUY pool (LOW swing broken ↓)", headerColor));
        AppendH4SlPoolDebugLines(lines, "BUY", _buyStopBreakDepthStats, okColor, warnColor, dimColor, accentColor);
        lines.Add(("─ SELL pool (HIGH swing broken ↑)", headerColor));
        AppendH4SlPoolDebugLines(lines, "SELL", _sellStopBreakDepthStats, okColor, warnColor, dimColor, accentColor);

        if (!string.IsNullOrEmpty(_lastH4SlResolveNote))
            lines.Add(($"last H4SL: {_lastH4SlResolveNote}", accentColor));

        AppendH4SlPlanDebugLines(lines, headerColor, dimColor, accentColor, okColor);

        return lines;
    }

    void AppendH4SlPlanDebugLines(
        List<(string Text, Color Color)> lines,
        Color headerColor,
        Color dimColor,
        Color accentColor,
        Color okColor)
    {
        lines.Add(("─ last plan levels ─", headerColor));
        if (!_lastH4SlPlanDebug.HasData)
        {
            lines.Add(("  (chưa có plan — chờ FIRE)", dimColor));
            return;
        }

        var s = _lastH4SlPlanDebug;
        lines.Add(($"  R{s.SlotIndex + 1} {s.DirectionLabel} bar={s.ChartBar}" + (s.IsSplit ? " split" : ""), accentColor));
        lines.Add(($"  entry={s.Entry:0.#####}  sl={s.StopLoss:0.#####} ({s.SlPips:0.##}p)", okColor));
        if (s.TpC > 0)
            lines.Add(($"  tpC={s.TpC:0.#####} ({s.TpCPips:0.##}p)", okColor));
        else
            lines.Add(("  tpC=—", dimColor));

        if (s.TpD > 0)
            lines.Add(($"  tpD={s.TpD:0.#####} ({s.TpDPips:0.##}p)", okColor));
        else
            lines.Add(("  tpD=—", dimColor));
    }

    void AppendH4SlPoolDebugLines(
        List<(string Text, Color Color)> lines,
        string side,
        BreakDepthStatistics? pool,
        Color okColor,
        Color warnColor,
        Color dimColor,
        Color accentColor)
    {
        if (pool is null)
        {
            lines.Add(("  (not initialized)", dimColor));
            return;
        }

        var count = pool.Count;
        var sufficient = pool.TryGetStats(H4SlMinSampleCount, out var gateStats);
        pool.TryGetPreviewStats(out var preview);
        if (preview.Count > 0)
            lines.Add(($"  {side} samples={count}/{H4SlMinSampleCount}  p25={preview.P25:0.##} mean={preview.Mean:0.##} p75={preview.P75:0.##} pmin={preview.PMin:0.##} pmax={preview.PMax:0.##}pip", dimColor));
        else
            lines.Add(($"  {side} samples={count}/{H4SlMinSampleCount}", warnColor));

        if (sufficient)
        {
            var (bandLo, bandHi) = BreakDepthStatistics.ResolveBandPips(in gateStats, H4SlBandLowStat, H4SlBandHighStat);
            lines.Add(($"  active: stats band=[{bandLo:0.##},{bandHi:0.##}] lo={H4SlBandLowStat} hi={H4SlBandHighStat}", okColor));
        }
        else
        {
            var fb = BuildH4SlShortPoolFallbackStats(count);
            var (bandLo, bandHi) = BreakDepthStatistics.ResolveBandPips(in fb, H4SlBandLowStat, H4SlBandHighStat);
            lines.Add(($"  active: FALLBACK band=[{bandLo:0.##},{bandHi:0.##}] lo={H4SlBandLowStat} hi={H4SlBandHighStat}", warnColor));
        }
    }

    void CaptureH4SlResolveNote(string planReason, SignalDirection direction)
    {
        var start = planReason.IndexOf("[H4SL=", StringComparison.Ordinal);
        if (start < 0)
            return;
        var end = planReason.IndexOf(']', start);
        if (end < 0)
            return;

        var note = planReason.Substring(start + 6, end - start - 6);
        var dir = direction == SignalDirection.Buy ? "BUY" : direction == SignalDirection.Sell ? "SELL" : "?";
        _lastH4SlResolveNote = $"{dir} {note}";
    }

    TimeSpan EstimateChartBarPeriod()
    {
        if (TimeFrame == TimeFrame.Minute5) return TimeSpan.FromMinutes(5);
        if (TimeFrame == TimeFrame.Minute15) return TimeSpan.FromMinutes(15);
        if (TimeFrame == TimeFrame.Minute30) return TimeSpan.FromMinutes(30);
        if (TimeFrame == TimeFrame.Hour) return TimeSpan.FromHours(1);
        if (TimeFrame == TimeFrame.Hour4) return TimeSpan.FromHours(4);
        return TimeSpan.FromMinutes(15);
    }

    /// <summary>
    /// Live lookup of USD conversion rate for the profile's quote currency.
    /// Priority: OverrideConvUsdPerQuote → USD (1.0) → {QUOTE}USD direct
    ///           → USD{QUOTE} inverse → cross-rate via chart symbol → 0.
    ///
    /// Cross-rate fallback: if the chart symbol is BASE/QUOTE and BASEUSD is available,
    /// then QUOTEUSD ≈ BASEUSD / (BASE/QUOTE bid).
    /// Example: EURGBP chart + EURUSD loaded → GBPUSD ≈ EURUSD.bid / EURGBP.bid.
    /// Conv lookups bypass chart-symbol guard (read-only bid for lot sizing).
    /// </summary>
    double ResolveConvUsdPerQuote(string quote)
    {
        if (quote is "USD" or "USDT" or "USDC")
            return 1.0;

        if (OverrideConvUsdPerQuote > 0)
            return OverrideConvUsdPerQuote;

        // JPY: pip value is in JPY — need USD/JPY rate (1/USDJPY bid).
        if (quote == "JPY")
        {
            if (SymbolName.Equals("USDJPY", StringComparison.OrdinalIgnoreCase) && Symbol.Bid > 0)
                return 1.0 / Symbol.Bid;

            var usdJpy = TryGetConvSymbol("USDJPY");
            if (usdJpy?.Bid > 0)
                return 1.0 / usdJpy.Bid;
        }

        try
        {
            var direct = TryGetConvSymbol($"{quote}USD");
            if (direct != null && direct.Bid > 0)
                return direct.Bid;
        }
        catch { /* pair not available in tester */ }

        try
        {
            var inverse = TryGetConvSymbol($"USD{quote}");
            if (inverse != null && inverse.Bid > 0)
                return 1.0 / inverse.Bid;
        }
        catch { /* pair not available in tester */ }

        // Cross-rate fallback: derive QUOTEUSD from chart symbol when chart is BASE/QUOTE.
        // Example: chart=EURGBP, quote=GBP → GBPUSD ≈ EURUSD.bid / EURGBP.bid
        // Works whenever the BASE of the chart symbol is directly paired with USD.
        var sym = SymbolName ?? "";
        if (sym.Length >= 6 && Symbol.Bid > 0)
        {
            var symUpper = sym.ToUpperInvariant();
            var chartBase = symUpper.Substring(0, 3);
            var chartQuote = symUpper.Substring(3, 3);
            if (string.Equals(chartQuote, quote, System.StringComparison.OrdinalIgnoreCase))
            {
                // Try BASEUSD direct
                try
                {
                    var baseUsd = TryGetConvSymbol($"{chartBase}USD");
                    if (baseUsd?.Bid > 0)
                    {
                        var derived = baseUsd.Bid / Symbol.Bid;
                        Log($"[L6BT] conv USD/{quote}: cross-rate via {chartBase}USD/chart = {derived:0.######}");
                        return derived;
                    }
                }
                catch { }

                // Try USDBASE inverse
                try
                {
                    var usdBase = TryGetConvSymbol($"USD{chartBase}");
                    if (usdBase?.Bid > 0)
                    {
                        var derived = 1.0 / (usdBase.Bid * Symbol.Bid);
                        Log($"[L6BT] conv USD/{quote}: cross-rate via 1/(USD{chartBase}*chart) = {derived:0.######}");
                        return derived;
                    }
                }
                catch { }
            }
        }

        Log($"[L6BT] WARN: cannot resolve USD/{quote} conv rate — set OverrideConvUsdPerQuote manually");
        return 0;
    }

    /// <summary>Symbol lookup for quote-currency conversion only (not gated by chart symbol).</summary>
    Symbol? TryGetConvSymbol(string symbolName)
    {
        try
        {
            return Symbols.GetSymbol(symbolName);
        }
        catch
        {
            return null;
        }
    }

    TradeMapperConfig BuildMapperConfig()
    {
        var quote = _sizingProfile.QuoteCurrency;
        var conv  = ResolveConvUsdPerQuote(quote);
        var spread = ResolveSpreadSnapshot();
        return new TradeMapperConfig
        {
            SymbolName        = SymbolName,
            TickSize          = Symbol.TickSize,
            PipSize           = Symbol.PipSize,
            SpreadPips        = spread.Pips,
            SpreadSource      = spread.Source,
            AccountBalanceFtmo = AccountBalanceFtmo,
            RiskPercent       = RiskPercent,
            RoundPrecision    = RoundPrecision,
            RewardRisk        = RewardRisk,
            SlWidthMult       = SlWidthMult,
            SlFromEntry       = SlFromEntry,
            AssetType         = _sizingProfile.AssetType,
            CustomContractSize = _sizingProfile.ContractSize,
            IsCryptoSymbol    = _sizingProfile.IsCrypto,
            QuoteCurrency     = quote,
            BaseAssetName     = _sizingProfile.BaseAssetName,
            ConvUsdPerQuote   = conv,
            LabelPrefix       = LabelPrefix,
            ChartTfToken      = Loop6ChartTimeframeToken.FromBarsTimeFrame(TimeFrame),
            M5FallbackState   = ResolveM5FallbackState(),
            M5FallbackBuffer  = ResolveM5FallbackBuffer(),
            EnableM5SwingBFallback = EnableM5SwingBFallback,
            UseSwingCEdgeTakeProfit = _swingCTpCfg.UseSwingCEdgeTakeProfit,
            SwingCEdgeTpFallbackToRR = _swingCTpCfg.SwingCEdgeTpFallbackToRR,
            SwingCEdgeTpRuleSlots = _swingCTpCfg.RuleSlotIndices,
            SwingCTpMode = _swingCTpCfg.TpMode,
            SkipIfSwingCRrBelowBase = _swingCTpCfg.SkipIfSwingCRrBelowBase,
            MoveEntryIfSwingCRrBelowBase = _swingCTpCfg.MoveEntryIfSwingCRrBelowBase,
            SplitDLegMinIncrementalRr = _swingCTpCfg.SplitDLegMinIncrementalRr,
            RecalcTpAfterSpread = RecalcTpAfterSpread,
            AtrSlWidthAdjust = _atrSlCfg,
            EnableMinSlConstraint   = EnableMinSlConstraint,
            MinSlSpreadMultiple     = MinSlSpreadMultiple,
            MinSlPipsForex          = MinSlPipsForex,
            SlConstraintBacktestMode = MinSlBacktestMode,
            DZoneStates             = BuildDZoneStates(),
            DPrimeZoneStates        = BuildDPrimeZoneStates(),
            EnableGongLoiTpD        = EnableGongLoiTpD,
            NearDZoneFallbackStates = BuildNearDZoneFallbackStates(),
            EnableSwingCZoneObstacleGate     = EnableSwingCZoneObstacleGate,
            SwingCZoneObstacleStates         = BuildSwingCZoneObstacleStates(),
            SwingCZoneObstacleFallbackStates = BuildSwingCZoneObstacleFallbackStates(),
            SwingCZoneObstacleTfBuffers      = BuildSwingCZoneObstacleTfBuffers(),
            ChartSeriesBuffer                = ResolveChartSeriesBuffer(),
        };
    }

    /// <summary>
    /// Collect M5/M15/H1/H4 PineStateEngine instances for D-zone lookup in SplitTpAtCAndD mode.
    /// Only TFs whose engines are already initialised are included.
    /// </summary>
    IReadOnlyDictionary<string, PineStateEngine>? BuildDZoneStates()
    {
        if (_swingCTpCfg.TpMode != SwingCTpMode.SplitTpAtCAndD)
            return null;

        var dict = new Dictionary<string, PineStateEngine>(4);
        foreach (var tf in s_dZoneTfTokens)
        {
            if (_host.TryGetState(tf, out var st))
                dict[tf] = st;
        }
        return dict.Count > 0 ? dict : null;
    }

    static readonly string[] s_dZoneTfTokens = { "5", "15", "60", "240" };
    static readonly string[] s_dPrimeZoneTfTokens = { "15", "60", "240" };

    /// <summary>
    /// Gồng lời: M15/H1/H4 only — higher-TF D' for TP leg D on R3–R6.
    /// </summary>
    IReadOnlyDictionary<string, PineStateEngine>? BuildDPrimeZoneStates()
    {
        if (_swingCTpCfg.TpMode != SwingCTpMode.SplitTpAtCAndD || !EnableGongLoiTpD)
            return null;

        var dict = new Dictionary<string, PineStateEngine>(3);
        foreach (var tf in s_dPrimeZoneTfTokens)
        {
            if (_host.TryGetState(tf, out var st))
                dict[tf] = st;
        }
        return dict.Count > 0 ? dict : null;
    }

    /// <summary>
    /// Collect Daily ("1D") PineStateEngine for the near-D pending-cancel gate fallback. Used ONLY
    /// when no D zone is found in M5/M15/H1/H4. Never used for D leg TP resolution.
    /// </summary>
    IReadOnlyDictionary<string, PineStateEngine>? BuildNearDZoneFallbackStates()
    {
        if (_swingCTpCfg.TpMode != SwingCTpMode.SplitTpAtCAndD || !EnableCancelOnNearDZone)
            return null;

        var dict = new Dictionary<string, PineStateEngine>(1);
        foreach (var tf in s_nearDFallbackTfTokens)
        {
            if (_host.TryGetState(tf, out var st))
                dict[tf] = st;
        }
        return dict.Count > 0 ? dict : null;
    }

    static readonly string[] s_nearDFallbackTfTokens = { "1440" };

    /// <summary>
    /// Collect PineStateEngine instances for the Swing-C zone obstacle gate (M5/M15/H1/H4).
    /// Only includes TFs whose engines have already been initialised.
    /// </summary>
    IReadOnlyDictionary<string, PineStateEngine>? BuildSwingCZoneObstacleStates()
    {
        if (!EnableSwingCZoneObstacleGate || !_swingCTpCfg.UseSwingCEdgeTakeProfit)
            return null;

        var tokens = SwingCZoneObstacleTfTokens
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var dict = new Dictionary<string, PineStateEngine>(tokens.Length);
        foreach (var tf in tokens)
        {
            if (_host.TryGetState(tf, out var st))
                dict[tf] = st;
        }
        return dict.Count > 0 ? dict : null;
    }

    /// <summary>Returns the SeriesBuffer for the chart TF, used for C-zone cross-TF self-identity and M5→M15 B anchoring.</summary>
    SeriesBuffer? ResolveChartSeriesBuffer()
    {
        if (_host is null)
            return null;
        var chartTf = Loop6ChartTimeframeToken.FromBarsTimeFrame(TimeFrame);
        _host.TryGetSeriesBuffer(chartTf, out var buf);
        return buf;
    }

    PineStateEngine? ResolveM5FallbackState()
    {
        if (_host is null)
            return null;
        if (Loop6ChartTimeframeToken.FromBarsTimeFrame(TimeFrame) != "15")
            return null;
        return _host.TryGetState("5", out var st) ? st : null;
    }

    SeriesBuffer? ResolveM5FallbackBuffer()
    {
        if (ResolveM5FallbackState() is null)
            return null;
        _host.TryGetSeriesBuffer("5", out var buf);
        return buf;
    }

    SwingCEdgeCrossTfContext? BuildSwingCCrossTfContext()
    {
        var m5 = ResolveM5FallbackState();
        if (m5 is null)
            return null;

        return new SwingCEdgeCrossTfContext
        {
            M5State = m5,
            M5Buffer = ResolveM5FallbackBuffer(),
            ChartBuffer = ResolveChartSeriesBuffer(),
            ChartTfToken = Loop6ChartTimeframeToken.FromBarsTimeFrame(TimeFrame),
        };
    }

    /// <summary>
    /// Collect per-TF SeriesBuffers for each TF in <see cref="SwingCZoneObstacleTfTokens"/>.
    /// These buffers are required so that <see cref="MultiTfZoneSnapshot.Collect"/> can populate
    /// <see cref="ZoneCandidate.PivotOpenTime"/>, enabling the cross-TF self-identity guard to
    /// exclude the C pivot's own zone representation on shorter TFs (e.g. M5).
    /// </summary>
    IReadOnlyDictionary<string, SeriesBuffer>? BuildSwingCZoneObstacleTfBuffers()
    {
        if (!EnableSwingCZoneObstacleGate || !_swingCTpCfg.UseSwingCEdgeTakeProfit)
            return null;

        var tokens = SwingCZoneObstacleTfTokens
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var dict = new Dictionary<string, SeriesBuffer>(tokens.Length);
        foreach (var tf in tokens)
        {
            if (_host.TryGetSeriesBuffer(tf, out var buf))
                dict[tf] = buf;
        }
        return dict.Count > 0 ? dict : null;
    }

    static readonly string[] s_swingCObstacleFallbackTfTokens = { "1440" };

    /// <summary>Collect Daily state for the Swing-C zone obstacle gate fallback.</summary>
    IReadOnlyDictionary<string, PineStateEngine>? BuildSwingCZoneObstacleFallbackStates()
    {
        if (!EnableSwingCZoneObstacleGate || !EnableSwingCZoneObstacleDailyFallback
            || !_swingCTpCfg.UseSwingCEdgeTakeProfit)
            return null;

        var dict = new Dictionary<string, PineStateEngine>(1);
        foreach (var tf in s_swingCObstacleFallbackTfTokens)
        {
            if (_host.TryGetState(tf, out var st))
                dict[tf] = st;
        }
        return dict.Count > 0 ? dict : null;
    }

    Loop6HostParameterSnapshot BuildSnapshot() => Loop6HostSnapshotBuilder.WithStyleDefaults(new Loop6HostParameterSnapshot
    {
        LookbackBars         = LookbackBars,
        EnableKeylevel       = EnableKeylevel,
        LockSwingCount       = LockSwingCount,

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

        BreakR3MaxK          = BreakR3MaxK,

        LtfBufferSize        = LtfBufferSize,
        ObScanBars           = ObScanBars,
        MaxObs               = MaxObs,
        MinObBodyRatio       = MinObBodyRatio,
        MaxDojiBodyRatio     = MaxDojiBodyRatio,

        KeylevelLookback     = KeylevelLookback,
        KeylevelAvgBodyLen   = KeylevelAvgBodyLen,
        KeylevelMinBodyMult  = KeylevelMinBodyMult,
        KeylevelAtrMult      = KeylevelAtrMult,
        KeylevelAtrLen       = KeylevelAtrLen,
        MaxKeylevelKeep      = MaxKeylevelKeep,
    });

    // ── News Filter ──────────────────────────────────────────────────────────

    void InitNewsFilter()
    {
        if (!EnableNewsFilter)
        {
            Print("[NEWS] News filter DISABLED");
            return;
        }

        var cfg = NewsFilterConfig.Build(
            enableNewsFilter:              EnableNewsFilter,
            newsProvider:                  NewsProvider,
            newsApiKey:                    NewsApiKey,
            newsLookaheadDays:             NewsLookaheadDays,
            newsRefreshHours:              NewsRefreshHours,
            useNewsCache:                  UseNewsCache,
            newsCacheFileName:             NewsCacheFileName,
            newsCacheMaxAgeHours:          NewsCacheMaxAgeHours,
            newsFailSafeMode:              NewsFailSafeMode,
            blockCurrencies:               BlockCurrencies,
            blockEventKeywords:            BlockEventKeywords,
            blockUsdNewsSymbols:           BlockUsdNewsSymbols,
            enableConservativeCrossBlock:  EnableConservativeCrossBlock,
            conservativeCrossBlockSymbols: ConservativeCrossBlockSymbols,
            blockBeforeHighImpactMin:      BlockBeforeHighImpactMin,
            blockAfterHighImpactMin:       BlockAfterHighImpactMin,
            enableNewsForceClose:          EnableNewsForceClose,
            forceCloseBeforeNewsMin:       ForceCloseBeforeNewsMin,
            cancelPendingBeforeNewsMin:    CancelPendingBeforeNewsMin,
            newsRefreshOnStart:            NewsRefreshOnStart,
            newsRefreshOnTimer:            NewsRefreshOnTimer,
            failSafeFridayBlockStartBangkok: FailSafeFridayBlockStartBangkok,
            failSafeFridayBlockEndBangkok:   FailSafeFridayBlockEndBangkok,
            newsBacktestMode:              NewsBacktestMode);

        var cache    = new NewsCacheStore(cfg.NewsCacheFileName);
        var provider = new TradingEconomicsNewsProvider();
        _newsFilter  = new NewsFilterService(cfg, provider, cache, Print);

        _newsFilter.LoadCacheOnStart();

        if (cfg.NewsRefreshOnStart)
            _newsFilter.ForceRefresh(Server.TimeInUtc);

        // 60-second timer for live force-close granularity (no-op in backtest).
        if (cfg.NewsRefreshOnTimer)
            Timer.Start(60);

        Print(_newsFilter.BuildTelemetryLine(Server.TimeInUtc));
    }

    // ── Friday Flat Guard ─────────────────────────────────────────────────────

    void InitFridayFlatGuard()
    {
        var cfg = FridayFlatGuardConfig.Build(
            enableFridayFlatGuard:           EnableFridayFlatGuard,
            fridayFlatTimeBangkok:           FridayFlatTimeBangkok,
            fridayBlockNewEntriesTimeBangkok: FridayBlockNewEntriesTimeBangkok,
            mondayResumeTimeBangkok:         MondayResumeTimeBangkok);

        _fridayFlat = new FridayFlatGuardService(cfg, _fridayFlatTracker, Print);
        Print($"[FRIDAY_FLAT] Guard {(EnableFridayFlatGuard ? "ENABLED" : "DISABLED")} " +
              $"flat={FridayFlatTimeBangkok} block={FridayBlockNewEntriesTimeBangkok} resume={MondayResumeTimeBangkok} BKK");

        // Start 60-second timer for live force-close if not already running from news filter.
        if (EnableFridayFlatGuard && !NewsRefreshOnTimer)
            Timer.Start(60);
    }

    /// <summary>
    /// Called from OnTimer / OnTick / OnBar.
    /// If Friday flat close is due (once per week), close all bot positions and cancel pending orders.
    /// Uses _exec.CloseAndCancelForSymbol() which is already scoped to LabelPrefix + SymbolName —
    /// manual trades and other robots' trades are never touched.
    /// </summary>
    void CheckAndExecuteFridayFlatClose()
    {
        if (_fridayFlat == null)
            return;

        var decision = _fridayFlat.Check(Server.TimeInUtc);
        if (!decision.ShouldForceCloseNow)
            return;

        var bkk = decision.BangkokTime;
        int closed = 0, cancelled = 0;
        string lastError = "";

        try
        {
            var result = _exec.CloseAndCancelForSymbol(SymbolName, Loop6SkipReasonCodes.FridayFlatClose);
            closed    = result.PositionsClosed;
            cancelled = result.PendingsCancelled;
        }
        catch (Exception ex)
        {
            lastError = ex.Message;
            Print($"[FRIDAY_FLAT] CloseAndCancel error: {ex.Message}");
        }

        _fridayFlatTracker.MarkFired(bkk);
        Print($"[FRIDAY_FLAT_CLOSE] bkk={bkk:yyyy-MM-dd HH:mm} closed={closed} cancelled={cancelled} err={lastError}");

        if (_csvLog == null)
            return;

        var ctx = BuildAnalyticsContext();
        _csvLog.LogFridayFlatAction(
            eventType:         Loop6SkipReasonCodes.FridayFlatClose,
            ctx:               ctx,
            bangkokTime:       bkk,
            action:            Loop6SkipReasonCodes.FridayFlatClose,
            positionsClosed:   closed,
            pendingsCancelled: cancelled,
            success:           string.IsNullOrEmpty(lastError),
            error:             lastError,
            reasonDetail:      decision.ReasonDetail);
    }

    protected override void OnTimer()
    {
        try
        {
            CheckAndExecuteFridayFlatClose();
        }
        catch (Exception ex)
        {
            Print($"[FRIDAY_FLAT] OnTimer error: {ex.GetType().Name}: {ex.Message}");
        }

        if (_newsFilter == null || !NewsRefreshOnTimer)
            return;
        try
        {
            _newsFilter.RefreshIfNeeded(Server.TimeInUtc);
            CheckAndExecuteNewsForceClose();
        }
        catch (Exception ex)
        {
            Print($"[NEWS] OnTimer error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // OnTick: only used for force-close granularity (Friday Flat + News).
    // Live: OnTimer (60s) is the primary path; OnTick provides sub-minute precision.
    // Backtest: OnTimer does not fire → OnTick is the fallback for force-close detection.
    protected override void OnTick()
    {
        try { CheckAndExecuteFridayFlatClose(); }
        catch (Exception ex) { Print($"[FRIDAY_FLAT] OnTick error: {ex.Message}"); }

        try { EvaluatePostFillCRollovers(); }
        catch (Exception ex) { Print($"[ROLLOVER] OnTick error: {ex.Message}"); }

        try { LogPositionSlProximityDiagnostics(); }
        catch (Exception ex) { Print($"[POS-DIAG] OnTick error: {ex.Message}"); }

        if (_newsFilter == null)
            return;
        try
        {
            CheckAndExecuteNewsForceClose();
        }
        catch (Exception ex)
        {
            Print($"[NEWS] OnTick error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Called both from OnBar (backtest compatibility) and OnTimer (live granularity).
    /// Uses NewsForceCloseTracker to prevent duplicate force-closes per event.
    /// </summary>
    void CheckAndExecuteNewsForceClose()
    {
        if (_newsFilter == null || !EnableTrading)
            return;

        var decision = _newsFilter.CheckForceCloseRequired(SymbolName, Server.TimeInUtc);
        if (!decision.ShouldForceCloseNow || decision.Event == null)
            return;

        if (!_newsForceCloseTracker.ShouldFire(decision.Event))
            return;

        var positions = _exec.OurPositions().ToArray();
        var pendings  = _exec.OurPendingOrders().ToArray();

        if (positions.Length == 0 && pendings.Length == 0)
        {
            _newsForceCloseTracker.MarkFired(decision.Event, Array.Empty<string>(), decision.ReasonCode);
            Print($"[NEWS] Force-close triggered but no open positions/orders. event={decision.Event.EventName}");
            return;
        }

        var reason = $"{decision.ReasonCode}: {decision.Event.EventName} in {decision.MinutesToEvent:0.#}m";
        var labels = positions.Select(p => p.Label).Where(l => l != null).Cast<string>().ToList();

        foreach (var label in labels)
        {
            _closeReasonAudit.RecordManualClose(label, decision.ReasonCode);
            _execAudit.PrepareClose(label, decision.ReasonCode);
        }

        _newsForceCloseTracker.MarkFired(decision.Event, labels, decision.ReasonCode);

        // CloseAndCancelForSymbol: only closes THIS bot's positions/pending orders
        // for the current chart symbol (scoped by label prefix + symbol name).
        // Manual trades and other robots are never touched.
        var (closedCount, cancelledCount) = _exec.CloseAndCancelForSymbol(SymbolName, reason);
        AuditManualCloses(labels, decision.ReasonCode);
        _book.Clear();

        Print($"[NEWS] FORCE_CLOSE executed: {reason} | positions_closed={closedCount} pendings_cancelled={cancelledCount}");

        _csvLog?.LogNewsAction(
            eventType:              decision.ReasonCode,
            ctx:                    BuildAnalyticsContext(),
            provider:               _newsFilter.UsedCache ? "cache" : NewsProvider,
            newsEvent:              decision.Event,
            minutesToEvent:         decision.MinutesToEvent,
            action:                 "CLOSE_AND_CANCEL_FOR_SYMBOL",
            positionsClosed:        closedCount,
            pendingOrdersCancelled: cancelledCount,
            success:                true,
            error:                  "",
            usedCache:              _newsFilter.UsedCache,
            cacheAgeHours:          _newsFilter.CacheAgeHours,
            reasonDetail:           reason);
    }

    /// <summary>Check Friday Flat Guard entry block for a fire event and return whether to skip.</summary>
    bool IsBlockedByFridayFlat(int ruleSlot, string direction, int? chartBar)
    {
        if (_fridayFlat == null)
            return false;

        if (!_fridayFlat.IsEntryBlocked(Server.TimeInUtc))
            return false;

        var bkk = Loop6BangkokSession.GetBangkokTime(Server.TimeInUtc);
        var detail = $"friday_flat_block bkk={bkk:yyyy-MM-dd HH:mm}";
        Print($"[FRIDAY_FLAT] SKIP {Loop6SkipReasonCodes.FridayFlatBlock}: {detail}");

        LogCsvSkip(ruleSlot, direction, Loop6SkipReasonCodes.FridayFlatBlock, detail,
            0, 0, 0, 0, chartBar);

        if (_csvLog != null)
        {
            var ctx = BuildAnalyticsContext();
            _csvLog.LogFridayFlatAction(
                eventType:         Loop6SkipReasonCodes.FridayFlatBlock,
                ctx:               ctx,
                bangkokTime:       bkk,
                action:            "SKIP_ENTRY",
                positionsClosed:   0,
                pendingsCancelled: 0,
                success:           true,
                error:             "",
                reasonDetail:      detail);
        }

        return true;
    }

    /// <summary>Check entry block for a fire event and return whether to skip.</summary>
    bool IsBlockedByNews(int ruleSlot, string direction, double rawEntry, double rawSl, double rawTp, double slStructurePips, int? chartBar)
    {
        if (_newsFilter == null)
            return false;

        var decision = _newsFilter.CheckEntryBlocked(SymbolName, Server.TimeInUtc);
        if (!decision.IsBlocked)
            return false;

        var detail = decision.BuildSkipDetail();
        Print($"[NEWS] SKIP {decision.ReasonCode}: {detail}");

        LogCsvSkip(ruleSlot, direction,
            decision.ReasonCode, detail,
            rawEntry, rawSl, rawTp, slStructurePips, chartBar);

        _csvLog?.LogNewsAction(
            eventType:              decision.ReasonCode,
            ctx:                    BuildAnalyticsContext(),
            provider:               _newsFilter.UsedCache ? "cache" : NewsProvider,
            newsEvent:              decision.Event,
            minutesToEvent:         decision.MinutesToEvent,
            action:                 "SKIP_ENTRY",
            positionsClosed:        0,
            pendingOrdersCancelled: 0,
            success:                true,
            error:                  "",
            usedCache:              _newsFilter.UsedCache,
            cacheAgeHours:          _newsFilter.CacheAgeHours,
            reasonDetail:           detail);

        return true;
    }
}
