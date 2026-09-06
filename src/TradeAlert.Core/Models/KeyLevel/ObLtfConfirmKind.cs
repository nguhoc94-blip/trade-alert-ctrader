namespace TradeAlert.Core.Models.KeyLevel;

/// <summary>Phân loại kết quả LTF confirm cho label OB (debug / parity Pine).</summary>
public enum ObLtfConfirmKind
{
    Pending = 0,
    Standard = 1,
    NoLtfData = 2,
    HasLtfNoTouch = 3,
}
