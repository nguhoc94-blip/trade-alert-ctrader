using System.Collections.Generic;

namespace TradeAlert.Core.Models.Alerts;

/// <summary>Port thuần <c>f_check_already_triggered</c> từ <c>lib_alerts new</c> — không đủ để fire EVENT alerts.</summary>
public static class AlertDedupHelpers
{
    public static bool CheckAlreadyTriggered(IReadOnlyList<string> triggeredList, string swingKey)
    {
        if (triggeredList.Count == 0 || string.IsNullOrEmpty(swingKey))
            return false;

        for (var i = 0; i < triggeredList.Count; i++)
        {
            if (triggeredList[i] == swingKey)
                return true;
        }

        return false;
    }
}
