namespace TradeAlert.Core.Models;

/// <summary>Pine barstate.* tối thiểu cho Loop 2.</summary>
public readonly record struct BarRuntimeFlags(
    bool IsFirst,
    bool IsLast,
    bool IsConfirmed,
    bool IsRealtime,
    bool IsNew,
    bool IsHistory);
