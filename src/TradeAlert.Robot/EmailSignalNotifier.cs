using System;
using System.Collections.Generic;
using TradeAlert.Core.Models.Alerts;

namespace TradeAlert.Robot;

/// <summary>
/// Handles email alert deduplication and throttling for
/// <see cref="Loop6MultiSymbolEmailScanner"/>.
///
/// Two modes:
/// <list type="bullet">
///   <item><see cref="AlertMode.OncePerEventPerBar"/> — emit once per unique compound event
///   instance (keyed by symbol|tf|direction|rule|eventBarIndex). Re-fires on subsequent
///   chart bars from the same underlying HTF event are suppressed for the life of the
///   session.</item>
///   <item><see cref="AlertMode.EveryTime"/> — re-emit is allowed but only if
///   <c>EmailMinSecondsPerKey</c> seconds have elapsed since the last send for that key.</item>
/// </list>
/// </summary>
public sealed class EmailSignalNotifier
{
    // ── Config ────────────────────────────────────────────────────────────────
    public AlertMode Mode               { get; set; } = AlertMode.OncePerEventPerBar;
    public string    EmailFrom          { get; set; } = "";
    public string    EmailTo            { get; set; } = "";
    public string    EmailSubjectPrefix { get; set; } = "[LOOP6]";
    public int       EmailMinSeconds    { get; set; } = 30;

    // ── Dedup / throttle state ────────────────────────────────────────────────
    readonly HashSet<string>             _seenKeys   = new(StringComparer.Ordinal);
    readonly Dictionary<string, DateTime> _lastSent  = new(StringComparer.Ordinal);

    // ── Public API ────────────────────────────────────────────────────────────
    /// <summary>
    /// Evaluate a fired event. Returns a ready-to-send <see cref="EmailPayload"/> if the
    /// event should be emitted, or <c>null</c> if deduplicated / throttled.
    /// </summary>
    public EmailPayload? Evaluate(string symbol, in CompoundFireEvent ev, DateTime now)
    {
        var key = MakeKey(symbol, ev);

        if (Mode == AlertMode.OncePerEventPerBar)
        {
            if (_seenKeys.Contains(key))
                return null;
            _seenKeys.Add(key);
        }
        else // EveryTime
        {
            if (_lastSent.TryGetValue(key, out var lastSentTime)
                && (now - lastSentTime).TotalSeconds < EmailMinSeconds)
                return null;

            _lastSent[key] = now;
        }

        return BuildPayload(symbol, in ev);
    }

    // ── Key & payload helpers ─────────────────────────────────────────────────
    static string MakeKey(string symbol, in CompoundFireEvent ev) =>
        $"{symbol}|{ev.ChartTfToken}|{ev.Direction}|R{ev.SlotIndex + 1}|ev{ev.EventBarIndex}";

    EmailPayload BuildPayload(string symbol, in CompoundFireEvent ev)
    {
        var dirLabel = ev.Direction switch
        {
            SignalDirection.Buy  => "BUY",
            SignalDirection.Sell => "SELL",
            _                   => ev.Direction.ToString().ToUpperInvariant(),
        };

        var tfLabel = ev.ChartTfToken switch
        {
            "1"    => "M1",
            "5"    => "M5",
            "15"   => "M15",
            "30"   => "M30",
            "60"   => "H1",
            "240"  => "H4",
            "1440" => "D1",
            _      => ev.ChartTfToken,
        };

        var subject = $"{EmailSubjectPrefix} {symbol} {dirLabel} R{ev.SlotIndex + 1} {tfLabel}";
        var body    =
            $"Symbol   : {symbol}\n" +
            $"Direction: {dirLabel}\n" +
            $"Rule     : R{ev.SlotIndex + 1} [{ev.RuleName}]\n" +
            $"Timeframe: {tfLabel}\n" +
            $"Bar Open : {ev.BarOpenTime:yyyy-MM-dd HH:mm}\n" +
            $"Price    : {ev.Price}\n" +
            $"Bar Index: {ev.ChartBarIndex}\n" +
            $"EventBar : {ev.EventBarIndex}\n";

        return new EmailPayload(EmailFrom, EmailTo, subject, body);
    }
}

/// <summary>Deduplication mode for <see cref="EmailSignalNotifier"/>.</summary>
public enum AlertMode
{
    /// <summary>
    /// Send once per unique compound event instance within a session.
    /// Key = symbol|tf|direction|rule|eventBarIndex.
    /// A new HTF event (different EventBarIndex) is treated as a distinct alert.
    /// Re-fires of the same event on subsequent chart bars are suppressed.
    /// </summary>
    OncePerEventPerBar,

    /// <summary>
    /// Re-send is allowed, but at least <c>EmailMinSecondsPerKey</c> seconds must have
    /// elapsed since the last send for the same key.
    /// </summary>
    EveryTime,
}

/// <summary>Ready-to-send email envelope.</summary>
public readonly struct EmailPayload
{
    public string From    { get; }
    public string To      { get; }
    public string Subject { get; }
    public string Body    { get; }

    public EmailPayload(string from, string to, string subject, string body)
    {
        From    = from;
        To      = to;
        Subject = subject;
        Body    = body;
    }
}
