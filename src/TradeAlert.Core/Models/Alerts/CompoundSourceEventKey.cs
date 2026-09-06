using System;

namespace TradeAlert.Core.Models.Alerts;

/// <summary>Canonical source-event identity for compound dedup and stage ledger.</summary>
public static class CompoundSourceEventKey
{
    public static string FormatDirection(SignalDirection direction) =>
        direction switch
        {
            SignalDirection.Buy => "BUY",
            SignalDirection.Sell => "SELL",
            _ => direction.ToString().ToUpperInvariant(),
        };

    public static string FormatSourceTf(string tfToken) =>
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

    public static string Build(
        string symbol,
        string ruleId,
        SignalDirection direction,
        string sourceTfToken,
        int sourceBarIndex) =>
        $"{symbol}|{ruleId}|{FormatDirection(direction)}|{FormatSourceTf(sourceTfToken)}|{sourceBarIndex}";

    public static string Build(in CompoundSourceEventCandidate candidate, string ruleId, SignalDirection direction) =>
        Build(candidate.Symbol, ruleId, direction, candidate.SourceTfToken, candidate.SourceBarIndex);

    public static string CandidateBarDedupeKey(in CompoundSourceEventCandidate candidate) =>
        $"{candidate.Symbol}|{candidate.SourceTfToken}|{candidate.SourceBarIndex}";
}
