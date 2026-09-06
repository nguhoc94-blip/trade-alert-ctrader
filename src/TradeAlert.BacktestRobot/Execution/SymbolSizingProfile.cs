using System;
using TradeAlert.BacktestRobot.Execution.Kl;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>
/// Auto-detected sizing profile for a symbol: asset type, contract size, quote currency,
/// crypto flag, and base asset name.
///
/// Call <see cref="Detect"/> once at OnStart (uses <c>Symbol.Name</c>).
/// The user can still override every field via the [Parameter] overrides in the cBot
/// (any override value != default means "use mine instead of auto").
/// </summary>
public sealed class SymbolSizingProfile
{
    public KlAssetType AssetType       { get; private set; }
    public int         ContractSize    { get; private set; }
    public string      QuoteCurrency   { get; private set; } = "USD";
    public bool        IsCrypto        { get; private set; }
    public string      BaseAssetName   { get; private set; } = "";

    // ── Apply user overrides on top of auto-detected profile ────────────────
    public SymbolSizingProfile WithOverrides(
        KlAssetType? assetType,
        int?         contractSize,
        string?      quoteCurrency,
        bool?        isCrypto,
        string?      baseAssetName)
    {
        var p = new SymbolSizingProfile
        {
            AssetType     = assetType     ?? AssetType,
            ContractSize  = contractSize  ?? ContractSize,
            QuoteCurrency = quoteCurrency ?? QuoteCurrency,
            IsCrypto      = isCrypto      ?? IsCrypto,
            BaseAssetName = baseAssetName ?? BaseAssetName,
        };
        return p;
    }

    // ── Auto-detect from symbol name ────────────────────────────────────────
    public static SymbolSizingProfile Detect(string symbolName)
    {
        var s = (symbolName ?? "").ToUpperInvariant().Trim();
        var p = new SymbolSizingProfile();

        // --- Gold / Silver -------------------------------------------------------
        if (s.Contains("XAU") || s.Contains("GOLD"))
        {
            p.AssetType     = KlAssetType.XAUUSD;
            p.ContractSize  = 100;
            p.QuoteCurrency = "USD";
            return p;
        }
        if (s.Contains("XAG") || s.Contains("SILVER"))
        {
            p.AssetType     = KlAssetType.XAUUSD; // same contract logic (100 oz)
            p.ContractSize  = 100;
            p.QuoteCurrency = "USD";
            return p;
        }

        // --- Crypto --------------------------------------------------------------
        if (s.StartsWith("BTC") || s.Contains("BITCOIN"))
        {
            p.AssetType     = KlAssetType.BTCUSD;
            p.ContractSize  = 1;
            p.IsCrypto      = true;
            p.BaseAssetName = "BTC";
            p.QuoteCurrency = s.EndsWith("USDT") || s.EndsWith("USDC") ? s[3..] : "USD";
            return p;
        }
        if (s.StartsWith("ETH"))
        {
            p.AssetType     = KlAssetType.Custom;
            p.ContractSize  = 1;
            p.IsCrypto      = true;
            p.BaseAssetName = "ETH";
            p.QuoteCurrency = "USD";
            return p;
        }
        if (IsCryptoSymbol(s))
        {
            p.AssetType     = KlAssetType.Custom;
            p.ContractSize  = 1;
            p.IsCrypto      = true;
            p.BaseAssetName = s.Length >= 3 ? s[..3] : s;
            p.QuoteCurrency = "USD";
            return p;
        }

        // --- Equity indices (Custom lot; 1 contract = 1 index unit) --------------
        if (IsIndex(s, out var indexQuote))
        {
            p.AssetType     = KlAssetType.Custom;
            p.ContractSize  = 1;
            p.QuoteCurrency = indexQuote;
            return p;
        }

        // --- Standard 6-letter Forex pair ----------------------------------------
        if (s.Length == 6 && IsAllAlpha(s))
        {
            p.AssetType     = KlAssetType.Forex;
            p.ContractSize  = 100_000;
            p.QuoteCurrency = s[3..]; // last 3 chars = quote currency
            return p;
        }

        // --- Forex with separator (EUR/USD, EUR-USD) or suffix digits (EURUSD.i) -
        var cleaned = System.Text.RegularExpressions.Regex.Replace(s, @"[^A-Z]", "");
        if (cleaned.Length == 6 && IsAllAlpha(cleaned))
        {
            p.AssetType     = KlAssetType.Forex;
            p.ContractSize  = 100_000;
            p.QuoteCurrency = cleaned[3..];
            return p;
        }

        // --- Fallback: treat as Forex USD-quoted --------------------------------
        p.AssetType     = KlAssetType.Auto;
        p.ContractSize  = 100_000;
        p.QuoteCurrency = "USD";
        return p;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────
    static readonly string[] IndexKeywords =
    {
        "US30","DJ30","DOW","US500","SP500","NAS","US100","USTEC","NDX","NAS100",
        "DAX","GERMANY40","GER40","GER30","DE30","DE40",
        "UK100","FTSE","UKX",
        "JPN225","JP225","JAPAN","NIKKEI","NI225",
        "AUS200","ASX200",
        "HK50","HSI","HONGKONG",
        "STOXX","EU50","EUSTX50",
        "FRANCE40","CAC40","FRA40",
        "CHINA50","CN50",
        "VIX","VOLATILITY",
    };

    static readonly string[] CryptoSuffixes = { "USDT", "USDC", "USD", "BTC", "ETH", "BNB", "EUR" };
    static readonly string[] CryptoBases    = { "ETH","BNB","SOL","XRP","ADA","DOT","LINK","MATIC",
                                                 "AVAX","LTC","UNI","DOGE","SHIB","ATOM","TRX" };

    static bool IsCryptoSymbol(string s)
    {
        foreach (var b in CryptoBases)
            if (s.StartsWith(b)) return true;
        return false;
    }

    static bool IsIndex(string s, out string quote)
    {
        quote = "USD";
        foreach (var kw in IndexKeywords)
        {
            if (!s.Contains(kw)) continue;

            // Pick quote currency from common index countries
            if (kw.Contains("GER") || kw.Contains("DAX") || kw.Contains("EU"))
                quote = "EUR";
            else if (kw.Contains("UK") || kw.Contains("FTSE"))
                quote = "GBP";
            else if (kw.Contains("JPN") || kw.Contains("JP2") || kw.Contains("NIKKEI"))
                quote = "JPY";
            else if (kw.Contains("AUS") || kw.Contains("ASX"))
                quote = "AUD";
            else if (kw.Contains("CAC") || kw.Contains("FRA") || kw.Contains("FRANCE"))
                quote = "EUR";

            return true;
        }
        return false;
    }

    static bool IsAllAlpha(string s)
    {
        foreach (var c in s)
            if (!char.IsLetter(c)) return false;
        return true;
    }
}
