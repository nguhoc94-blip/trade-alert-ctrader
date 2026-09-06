namespace TradeAlert.Core.Models.Alerts;

public enum DuplicateRejectReason
{
    None = 0,
    AlreadyRegisteredInSession,
}

public interface IAlertDuplicateStore
{
    bool TryRegister(in DuplicateKey key, out DuplicateRejectReason reason);
    void ResetSession();
    void RemoveOlderThanBarIndex(long minBarIndexExclusive);
}
