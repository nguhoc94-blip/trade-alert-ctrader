using System;
using System.Globalization;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.Alerts;
using TradeAlert.Core.Models.KeyLevel;

namespace TradeAlert.BacktestRobot.Execution;

/// <summary>Explains why a deferred split fire is still waiting for swing-C (chart debug).</summary>
public static class SwingCWaitDiagnostic
{
    public readonly struct Result
    {
        public string StatusCode { get; init; }
        public string ShortLine { get; init; }
        public string DetailLine { get; init; }
    }

    /// <summary>
    /// True when no ACTIVE/D_SWING pivot of the trade direction exists after swing B yet.
    /// False once a confirmed swing-C candidate exists — even if plan build later fails RR/obstacle gates.
    /// </summary>
    public static bool IsAwaitingSwingCConfirmation(
        PineStateEngine state,
        SwingBResult? pinnedB,
        SignalDirection direction,
        int slotIndex,
        in TradeMapperConfig cfg)
    {
        if (pinnedB is null || pinnedB.PivotBar <= 0)
            return true;

        var isBuy = direction == SignalDirection.Buy;
        var usesM5SwingC = R1R2M5Structure.UsesM5SwingC(slotIndex, in cfg);
        var swingState = usesM5SwingC && cfg.M5FallbackState is not null ? cfg.M5FallbackState : state;
        var swingBBar = usesM5SwingC
            ? R1R2M5Structure.ResolveM5SwingBBarForC(in pinnedB, in cfg)
            : pinnedB.PivotBar;

        var probe = ProbeNearestPivotAfter(swingState.Pivots, swingBBar, isBuy);
        return probe.ValidConfirmedBar < 0;
    }

    public static Result Describe(
        PineStateEngine state,
        SwingBResult? pinnedB,
        SignalDirection direction,
        int slotIndex,
        in TradeMapperConfig cfg,
        string? buildReason = null)
    {
        if (pinnedB is null || pinnedB.PivotBar <= 0)
        {
            return new Result
            {
                StatusCode = "NO_B",
                ShortLine = "WAIT: thiếu swing B",
                DetailLine = buildReason ?? "pinned B absent",
            };
        }

        var isBuy = direction == SignalDirection.Buy;
        var usesM5SwingC = R1R2M5Structure.UsesM5SwingC(slotIndex, in cfg);
        var swingState = usesM5SwingC && cfg.M5FallbackState is not null ? cfg.M5FallbackState : state;
        var swingBBar = usesM5SwingC
            ? R1R2M5Structure.ResolveM5SwingBBarForC(in pinnedB, in cfg)
            : pinnedB.PivotBar;

        var pivotProbe = ProbeNearestPivotAfter(swingState.Pivots, swingBBar, isBuy);
        if (pivotProbe.ValidConfirmedBar >= 0)
        {
            // Confirmed C exists — defer should not stay on "no C"; surface mapper/build reason.
            if (!string.IsNullOrWhiteSpace(buildReason))
            {
                var code = ClassifyBuildReason(buildReason);
                return new Result
                {
                    StatusCode = code,
                    ShortLine = FormatShortFromBuildReason(code, buildReason),
                    DetailLine = buildReason,
                };
            }

            return new Result
            {
                StatusCode = "C_CONFIRMED",
                ShortLine = $"C confirmed bar={pivotProbe.ValidConfirmedBar} — chờ build plan",
                DetailLine = $"B@{swingBBar} C@{pivotProbe.ValidConfirmedBar}",
            };
        }

        if (pivotProbe.NearestAnyBar >= 0)
        {
            return new Result
            {
                StatusCode = "C_UNCONFIRMED",
                ShortLine = $"C chưa confirm bar={pivotProbe.NearestAnyBar} ({pivotProbe.NearestAnyFlagLabel})",
                DetailLine = $"B@{swingBBar} pivot flag={pivotProbe.NearestAnyFlag} price={pivotProbe.NearestAnyPrice:0.#####}",
            };
        }

        return new Result
        {
            StatusCode = "NO_C",
            ShortLine = $"chưa có pivot {(isBuy ? "HIGH" : "LOW")} sau B@{swingBBar}",
            DetailLine = buildReason ?? "no confirmed swing C",
        };
    }

    /// <summary>Expose pivot flag labels for audit logs.</summary>
    public static string FormatPivotFlagPublic(int flag) => FormatPivotFlag(flag);

    /// <summary>Expose pivot probe for audit logs (same logic as WAIT-C diagnostic).</summary>
    public static PivotProbeResult ProbeAfterBPublic(PivotStateStore pivots, int bBar, bool isBuy)
    {
        var probe = ProbeNearestPivotAfter(pivots, bBar, isBuy);
        return new PivotProbeResult
        {
            NearestAnyBar = probe.NearestAnyBar,
            NearestAnyFlag = probe.NearestAnyFlag,
            NearestAnyFlagLabel = probe.NearestAnyFlagLabel,
            NearestAnyPrice = probe.NearestAnyPrice,
            ValidConfirmedBar = probe.ValidConfirmedBar,
        };
    }

    public readonly struct PivotProbeResult
    {
        public int NearestAnyBar { get; init; }
        public int NearestAnyFlag { get; init; }
        public string NearestAnyFlagLabel { get; init; }
        public double NearestAnyPrice { get; init; }
        public int ValidConfirmedBar { get; init; }
    }

    readonly struct PivotProbe
    {
        public int NearestAnyBar { get; init; }
        public int NearestAnyFlag { get; init; }
        public string NearestAnyFlagLabel { get; init; }
        public double NearestAnyPrice { get; init; }
        public int ValidConfirmedBar { get; init; }
    }

    static PivotProbe ProbeNearestPivotAfter(PivotStateStore pivots, int bBar, bool isBuy)
    {
        var wantType = isBuy ? SwingBResolver.TypeHigh : SwingBResolver.TypeLow;
        var nearestAnyBar = -1;
        var nearestAnyIdx = -1;
        var validConfirmedBar = -1;

        for (var i = 0; i < pivots.Count; i++)
        {
            var snap = pivots.GetSnapshot(i);
            if (snap.BarIndex <= bBar || snap.Type != wantType)
                continue;

            if (nearestAnyBar < 0 || snap.BarIndex < nearestAnyBar)
            {
                nearestAnyBar = snap.BarIndex;
                nearestAnyIdx = i;
            }

            if (SwingCEdgeTakeProfitResolver.IsValidCEntryCandidate(pivots, i)
                && (validConfirmedBar < 0 || snap.BarIndex < validConfirmedBar))
                validConfirmedBar = snap.BarIndex;
        }

        if (nearestAnyIdx < 0)
            return new PivotProbe { ValidConfirmedBar = validConfirmedBar };

        var flag = pivots.GetFlag(nearestAnyIdx);
        var snapAny = pivots.GetSnapshot(nearestAnyIdx);
        return new PivotProbe
        {
            NearestAnyBar = nearestAnyBar,
            NearestAnyFlag = flag,
            NearestAnyFlagLabel = FormatPivotFlag(flag),
            NearestAnyPrice = snapAny.Price,
            ValidConfirmedBar = validConfirmedBar,
        };
    }

    static string ClassifyBuildReason(string reason)
    {
        var r = reason;
        if (r.Contains("no confirmed swing C", StringComparison.OrdinalIgnoreCase))
            return "NO_C";
        if (r.Contains("swing-C RR", StringComparison.OrdinalIgnoreCase)
            || r.Contains("RR below", StringComparison.OrdinalIgnoreCase))
            return "C_RR_LOW";
        if (r.Contains("not above entry", StringComparison.OrdinalIgnoreCase)
            || r.Contains("not below entry", StringComparison.OrdinalIgnoreCase))
            return "C_TP_SIDE";
        if (r.Contains("zone obstacle", StringComparison.OrdinalIgnoreCase))
            return "C_OBSTACLE";
        if (r.Contains("entry move not possible", StringComparison.OrdinalIgnoreCase))
            return "C_ENTRY_MOVE";
        if (r.Contains("SL too tight", StringComparison.OrdinalIgnoreCase))
            return "SL_TIGHT";
        return "BUILD_FAIL";
    }

    static string FormatShortFromBuildReason(string code, string reason) => code switch
    {
        "C_RR_LOW"      => "C có nhưng RR < base",
        "C_TP_SIDE"     => "C có nhưng TP sai phía entry",
        "C_OBSTACLE"    => "C bị chặn bởi zone obstacle",
        "C_ENTRY_MOVE"  => "C có — không move entry được",
        "SL_TIGHT"      => "SL quá hẹp sau move entry",
        "NO_C"          => "chưa có C confirmed",
        _               => Truncate(reason, 48),
    };

    static string FormatPivotFlag(int flag) => flag switch
    {
        SwingBResolver.FlagActive  => "ACTIVE",
        SwingBResolver.FlagDSwing  => "D_SWING",
        2                          => "BROKEN",
        0                          => "INIT",
        3                          => "MAIN_C",
        5                          => "MAIN_FAKE",
        6                          => "MAIN_OLD",
        7                          => "LOCK",
        _                          => flag.ToString(CultureInfo.InvariantCulture),
    };

    static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..(maxLen - 1)] + "…";
}
