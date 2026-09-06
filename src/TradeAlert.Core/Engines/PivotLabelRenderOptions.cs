using TradeAlert.Core.Models.KeyLevel;

namespace TradeAlert.Core.Engines;

/// <summary>Pine Visual Display toggles — <c>showLockedSwings</c>, <c>showFakeSwings</c>, <c>showActiveSwings</c>.</summary>
public sealed class PivotLabelRenderOptions
{
    public bool ShowLockedSwings { get; init; }
    public bool ShowFakeSwings   { get; init; }
    public bool ShowActiveSwings { get; init; }
    public int  BarIndex         { get; init; }
    public int  LookbackBars     { get; init; } = 200;
    /// <summary>Pine <c>update_label</c> line 2994 — delete OB when rendering locked pivot.</summary>
    public bool DeleteObsOnLockLabelRefresh { get; init; } = true;
    public ObPoolStore? ObPool { get; init; }

    /// <summary>Pine input defaults (all false — chỉ hiện label mặc định, lock ẩn).</summary>
    public static PivotLabelRenderOptions PineDefaults(int barIndex, int lookbackBars = 200) => new()
    {
        ShowLockedSwings = false,
        ShowFakeSwings   = false,
        ShowActiveSwings = false,
        BarIndex         = barIndex,
        LookbackBars     = lookbackBars,
    };
}
