using TradeAlert.BacktestRobot.Execution.Analytics;

namespace TradeAlert.BacktestRobot.Execution.FridayFlat;

/// <summary>
/// Friday Flat Guard — sole responsibility:
/// 1. On Friday at/after FridayFlatTime (Bangkok): signal one-shot close of bot positions/orders.
/// 2. From Friday FridayBlockNewEntriesTime through Monday MondayResumeTime (Bangkok): block new entries.
/// 3. All other times: no-op.
///
/// Does NOT execute closes itself — returns a <see cref="FridayFlatDecision"/> that the bot acts on.
/// The bot calls <see cref="FridayFlatCloseTracker"/> and <see cref="TradeExecutionService"/> for the actual close.
/// </summary>
public sealed class FridayFlatGuardService
{
    readonly FridayFlatGuardConfig    _cfg;
    readonly FridayFlatCloseTracker   _tracker;
    readonly Action<string>           _log;

    public FridayFlatGuardService(
        FridayFlatGuardConfig  cfg,
        FridayFlatCloseTracker tracker,
        Action<string>         log)
    {
        _cfg     = cfg;
        _tracker = tracker;
        _log     = log;
    }

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Call from OnTimer / OnTick / OnBar.
    /// Returns a decision: whether to force-close now and/or block entry.
    /// </summary>
    public FridayFlatDecision Check(DateTime utcNow)
    {
        if (!_cfg.EnableFridayFlatGuard)
            return FridayFlatDecision.None;

        var bkk = Loop6BangkokSession.GetBangkokTime(utcNow);
        return Evaluate(bkk);
    }

    /// <summary>
    /// Check entry block only (called before submitting a new order).
    /// Returns true if the entry should be blocked.
    /// </summary>
    public bool IsEntryBlocked(DateTime utcNow)
    {
        if (!_cfg.EnableFridayFlatGuard)
            return false;

        var bkk = Loop6BangkokSession.GetBangkokTime(utcNow);
        return IsInFlatWindow(bkk);
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    FridayFlatDecision Evaluate(DateTime bkk)
    {
        bool inFlatWindow  = IsInFlatWindow(bkk);
        bool shouldClose   = IsFridayCloseTime(bkk) && _tracker.ShouldFire(bkk);

        if (!inFlatWindow && !shouldClose)
            return FridayFlatDecision.None;

        return new FridayFlatDecision
        {
            ShouldForceCloseNow = shouldClose,
            IsEntryBlocked      = inFlatWindow,
            BangkokTime         = bkk,
            ReasonDetail        = BuildDetail(bkk, shouldClose, inFlatWindow),
        };
    }

    /// <summary>
    /// "Flat window" = Friday >= FridayBlockNewEntriesTime  OR  Saturday/Sunday all day
    ///             OR  Monday 00:00 up to (not including) MondayResumeTime.
    /// </summary>
    bool IsInFlatWindow(DateTime bkk)
    {
        var tod = bkk.TimeOfDay;
        return bkk.DayOfWeek switch
        {
            DayOfWeek.Friday   => tod >= _cfg.FridayBlockNewEntriesTime,
            DayOfWeek.Saturday => true,
            DayOfWeek.Sunday   => true,
            DayOfWeek.Monday   => tod < _cfg.MondayResumeTime,
            _                  => false,
        };
    }

    /// <summary>
    /// Force-close fires exactly once per Friday at/after FridayFlatTime.
    /// </summary>
    bool IsFridayCloseTime(DateTime bkk) =>
        bkk.DayOfWeek == DayOfWeek.Friday && bkk.TimeOfDay >= _cfg.FridayFlatTime;

    static string BuildDetail(DateTime bkk, bool close, bool block) =>
        $"friday_flat: bkk={bkk:yyyy-MM-dd HH:mm} close={close} block={block}";
}

/// <summary>Decision returned by <see cref="FridayFlatGuardService.Check"/>.</summary>
public sealed class FridayFlatDecision
{
    public static readonly FridayFlatDecision None = new();

    public bool     ShouldForceCloseNow { get; init; }
    public bool     IsEntryBlocked      { get; init; }
    public DateTime BangkokTime         { get; init; }
    public string   ReasonDetail        { get; init; } = "";
}
