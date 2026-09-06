using cAlgo.API;

namespace TradeAlert.Indicator.Host;

public static class Loop6ColorUtil
{
    public static uint ToOpaqueArgb(Color c, uint pineDefaultOpaque)
    {
        if (c.A == 0 && c.R == 0 && c.G == 0 && c.B == 0)
            return pineDefaultOpaque;
        return 0xFF000000u | ((uint)c.R << 16) | ((uint)c.G << 8) | (uint)c.B;
    }

    public static void SetRgbFromColor(Color c, uint pineDefaultOpaque, out byte r, out byte g, out byte b)
    {
        var argb = ToOpaqueArgb(c, pineDefaultOpaque);
        r = (byte)((argb >> 16) & 0xFF);
        g = (byte)((argb >> 8) & 0xFF);
        b = (byte)(argb & 0xFF);
    }
}
