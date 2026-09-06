using cAlgo.API;

namespace TradeAlert.Indicator;

/// <summary>
/// Token timeframe đưa vào DuplicateKey/context (vd. khớp M5 và test "5").
/// Giả định hội nhập: số phút cho TF phút hệ thống; không khớp thì <see cref="TimeFrame.ShortName"/>.
/// </summary>
public static class Loop6ChartTimeframeToken
{
    public static string FromBarsTimeFrame(TimeFrame tf)
    {
        if (tf.Equals(TimeFrame.Minute))
            return "1";
        if (tf.Equals(TimeFrame.Minute2))
            return "2";
        if (tf.Equals(TimeFrame.Minute3))
            return "3";
        if (tf.Equals(TimeFrame.Minute4))
            return "4";
        if (tf.Equals(TimeFrame.Minute5))
            return "5";
        if (tf.Equals(TimeFrame.Minute6))
            return "6";
        if (tf.Equals(TimeFrame.Minute7))
            return "7";
        if (tf.Equals(TimeFrame.Minute8))
            return "8";
        if (tf.Equals(TimeFrame.Minute9))
            return "9";
        if (tf.Equals(TimeFrame.Minute10))
            return "10";
        if (tf.Equals(TimeFrame.Minute15))
            return "15";
        if (tf.Equals(TimeFrame.Minute20))
            return "20";
        if (tf.Equals(TimeFrame.Minute30))
            return "30";
        if (tf.Equals(TimeFrame.Minute45))
            return "45";
        if (tf.Equals(TimeFrame.Hour))
            return "60";
        if (tf.Equals(TimeFrame.Hour4))
            return "240";
        if (tf.Equals(TimeFrame.Daily))
            return "D";
        if (tf.Equals(TimeFrame.Weekly))
            return "W";

        var sn = tf.ShortName;
        return string.IsNullOrEmpty(sn) ? tf.Name ?? "" : sn;
    }
}
