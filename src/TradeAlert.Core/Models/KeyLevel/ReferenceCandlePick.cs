namespace TradeAlert.Core.Models.KeyLevel;

public readonly record struct ReferenceCandlePick(
    int BestLag,
    double BodyHigh,
    double BodyLow,
    double BodyAbs);
