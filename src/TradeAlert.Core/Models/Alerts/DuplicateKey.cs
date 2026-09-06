using System;
using System.Globalization;
using System.Text;
using TradeAlert.Core.Models;

namespace TradeAlert.Core.Models.Alerts;

/// <summary>
/// Khóa dedup — thời gian bar nguồn chart-local <see cref="DateTimeKind.Unspecified"/>.
/// Log/persist dùng <see cref="ToCanonicalString"/>; không dùng <see cref="GetHashCode"/> làm khóa log.
/// </summary>
public readonly struct DuplicateKey : IEquatable<DuplicateKey>
{
    public string Symbol { get; }
    public string ChartTimeframe { get; }
    public string AlertName { get; }
    public string PineConditionSymbol { get; }
    public DateTime SourceBarOpenTimeChartLocal { get; }
    public long SourceBarIndex { get; }
    public string SourceTimeframeToken { get; }
    public int? PivotId { get; }
    public int? ObPoolIndex { get; }
    public long? StructId { get; }
    public string? EventFamilyKey { get; }

    public DuplicateKey(
        string symbol,
        string chartTimeframe,
        string alertName,
        string pineConditionSymbol,
        DateTime sourceBarOpenTimeChartLocal,
        long sourceBarIndex,
        string sourceTimeframeToken,
        int? pivotId = null,
        int? obPoolIndex = null,
        long? structId = null,
        string? eventFamilyKey = null)
    {
        Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
        ChartTimeframe = chartTimeframe ?? throw new ArgumentNullException(nameof(chartTimeframe));
        AlertName = alertName ?? throw new ArgumentNullException(nameof(alertName));
        PineConditionSymbol = pineConditionSymbol ?? throw new ArgumentNullException(nameof(pineConditionSymbol));
        SourceBarOpenTimeChartLocal = ChartTimePolicy.EnsureChartLocalUnspecified(sourceBarOpenTimeChartLocal);
        SourceBarIndex = sourceBarIndex;
        SourceTimeframeToken = sourceTimeframeToken ?? throw new ArgumentNullException(nameof(sourceTimeframeToken));
        PivotId = pivotId;
        ObPoolIndex = obPoolIndex;
        StructId = structId;
        EventFamilyKey = eventFamilyKey;
    }

    /// <summary>Chuỗi ổn định cho log và duplicate store — invariant delimiter.</summary>
    public string ToCanonicalString()
    {
        var t = SourceBarOpenTimeChartLocal;
        var ts = t.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);
        var sb = new StringBuilder(128);
        sb.Append("sym=").Append(Symbol).Append('|');
        sb.Append("tf=").Append(ChartTimeframe).Append('|');
        sb.Append("alert=").Append(AlertName).Append('|');
        sb.Append("pine=").Append(PineConditionSymbol).Append('|');
        sb.Append("barT=").Append(ts).Append('|');
        sb.Append("barI=").Append(SourceBarIndex.ToString(CultureInfo.InvariantCulture)).Append('|');
        sb.Append("srcTf=").Append(SourceTimeframeToken).Append('|');
        sb.Append("piv=").Append(PivotId?.ToString(CultureInfo.InvariantCulture) ?? "").Append('|');
        sb.Append("ob=").Append(ObPoolIndex?.ToString(CultureInfo.InvariantCulture) ?? "").Append('|');
        sb.Append("struct=").Append(StructId?.ToString(CultureInfo.InvariantCulture) ?? "").Append('|');
        sb.Append("ev=").Append(EventFamilyKey ?? "");
        return sb.ToString();
    }

    public bool Equals(DuplicateKey other) =>
        string.Equals(ToCanonicalString(), other.ToCanonicalString(), StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is DuplicateKey o && Equals(o);

    /// <summary>Chỉ dùng collection nội bộ — không ghi log.</summary>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(ToCanonicalString());

    public static bool operator ==(DuplicateKey a, DuplicateKey b) => a.Equals(b);
    public static bool operator !=(DuplicateKey a, DuplicateKey b) => !a.Equals(b);
}
