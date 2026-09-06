namespace TradeAlert.Core.Models.KeyLevel;

/// <summary>Why an OB ZIN/pending row has no chart rectangle (debug only).</summary>
public enum ObBoxMissingReason
{
    None = 0,
    Pending,
    ShowObBoxOff,
    LookbackExceeded,
    OhlcUnavailable,
    OverlapTrimmed,
    LtfReject,
    HtfLose,
}
