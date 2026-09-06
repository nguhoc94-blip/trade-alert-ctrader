using System.Collections.Generic;

namespace TradeAlert.Core.Models.Alerts;

/// <summary>
/// Per-session dedup state shared across bar evaluations — mirrors Pine triggered arrays.
/// Same instance must be reused across all bars of a session to preserve "already triggered" state.
/// Call ResetSession() when a new cTrader session/session-reload starts.
/// </summary>
public sealed class EventDedupSets
{
    // M5 sets
    public HashSet<string> TriggeredA_Buy_M5  { get; } = new(StringComparer.Ordinal);
    public HashSet<string> TriggeredB_Buy_M5  { get; } = new(StringComparer.Ordinal);
    public HashSet<string> TriggeredC_Buy_M5  { get; } = new(StringComparer.Ordinal);
    public HashSet<string> TriggeredA_Sell_M5 { get; } = new(StringComparer.Ordinal);
    public HashSet<string> TriggeredB_Sell_M5 { get; } = new(StringComparer.Ordinal);
    public HashSet<string> TriggeredC_Sell_M5 { get; } = new(StringComparer.Ordinal);

    // M15 sets
    public HashSet<string> TriggeredA_Buy_M15  { get; } = new(StringComparer.Ordinal);
    public HashSet<string> TriggeredB_Buy_M15  { get; } = new(StringComparer.Ordinal);
    public HashSet<string> TriggeredC_Buy_M15  { get; } = new(StringComparer.Ordinal);
    public HashSet<string> TriggeredA_Sell_M15 { get; } = new(StringComparer.Ordinal);
    public HashSet<string> TriggeredB_Sell_M15 { get; } = new(StringComparer.Ordinal);
    public HashSet<string> TriggeredC_Sell_M15 { get; } = new(StringComparer.Ordinal);

    // PKL (Phá Khung Lớn) sets — mọi TF, mọi bar confirmed (không tách buy/sell, không tách M5/M15).
    // Pine: triggeredA_PKL / triggeredB_PKL / triggeredC_PKL (pine code alert debug.pine ~1100-1130).
    public HashSet<string> TriggeredA_PKL { get; } = new(StringComparer.Ordinal);
    public HashSet<string> TriggeredB_PKL { get; } = new(StringComparer.Ordinal);
    public HashSet<string> TriggeredC_PKL { get; } = new(StringComparer.Ordinal);

    public void ResetSession()
    {
        TriggeredA_Buy_M5.Clear();  TriggeredB_Buy_M5.Clear();  TriggeredC_Buy_M5.Clear();
        TriggeredA_Sell_M5.Clear(); TriggeredB_Sell_M5.Clear(); TriggeredC_Sell_M5.Clear();
        TriggeredA_Buy_M15.Clear();  TriggeredB_Buy_M15.Clear();  TriggeredC_Buy_M15.Clear();
        TriggeredA_Sell_M15.Clear(); TriggeredB_Sell_M15.Clear(); TriggeredC_Sell_M15.Clear();
        TriggeredA_PKL.Clear(); TriggeredB_PKL.Clear(); TriggeredC_PKL.Clear();
    }
}
