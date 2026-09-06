using TradeAlert.BacktestRobot.Execution.FridayFlat;
using Xunit;

namespace TradeAlert.BacktestRobot.Tests.FridayFlat;

/// <summary>
/// Unit tests for FridayFlatGuardService.
///
/// Config: flat=18:00, blockEntries=18:00, resumeMonday=06:00 Bangkok (UTC+7).
///
/// Times below are UTC; +7 hours = Bangkok time shown in comments.
/// </summary>
public class FridayFlatGuardTests
{
    static FridayFlatGuardService Build(bool enabled = true) =>
        new FridayFlatGuardService(
            FridayFlatGuardConfig.Build(
                enableFridayFlatGuard:           enabled,
                fridayFlatTimeBangkok:           "18:00",
                fridayBlockNewEntriesTimeBangkok: "18:00",
                mondayResumeTimeBangkok:         "06:00"),
            new FridayFlatCloseTracker(),
            _ => { });

    // UTC helpers: Bangkok = UTC + 7h
    static DateTime Utc(int year, int month, int day, int utcHour, int utcMin = 0) =>
        new DateTime(year, month, day, utcHour, utcMin, 0, DateTimeKind.Utc);

    // ── Test 1: Friday 17:59 Bangkok = 10:59 UTC → no block ─────────────────

    [Fact]
    public void Friday_Before_FlatTime_Not_Blocked()
    {
        // Friday 2026-06-12 10:59 UTC = 17:59 Bangkok
        var svc = Build();
        var utc = Utc(2026, 6, 12, 10, 59);

        var decision = svc.Check(utc);

        Assert.False(decision.ShouldForceCloseNow, "Should NOT force-close before 18:00 BKK");
        Assert.False(decision.IsEntryBlocked,       "Should NOT block entries before 18:00 BKK");
        Assert.False(svc.IsEntryBlocked(utc),       "IsEntryBlocked must be false");
    }

    // ── Test 2: Friday 18:00 Bangkok = 11:00 UTC → close + block ────────────

    [Fact]
    public void Friday_At_FlatTime_CloseAndBlock()
    {
        // Friday 2026-06-12 11:00 UTC = 18:00 Bangkok
        var svc = Build();
        var utc = Utc(2026, 6, 12, 11, 0);

        var decision = svc.Check(utc);

        Assert.True(decision.ShouldForceCloseNow, "Should force-close AT 18:00 BKK");
        Assert.True(decision.IsEntryBlocked,       "Should block entries AT 18:00 BKK");
        Assert.True(svc.IsEntryBlocked(utc),       "IsEntryBlocked must be true");
    }

    // ── Test 3: Friday 18:00 BKK – force-close fires only once (tracker) ────

    [Fact]
    public void Friday_FlatClose_Fires_Only_Once_Per_Week()
    {
        // Same Friday, two consecutive checks
        var svc = Build();
        var utc = Utc(2026, 6, 12, 11, 0); // Friday 18:00 BKK

        var d1 = svc.Check(utc);
        Assert.True(d1.ShouldForceCloseNow, "First check must want to close");

        // Simulate the bot calling MarkFired
        var bkk = d1.BangkokTime;
        // Access tracker indirectly: re-build service with the same tracker instance
        var tracker = new FridayFlatCloseTracker();
        var cfg = FridayFlatGuardConfig.Build(true, "18:00", "18:00", "06:00");
        var svc2 = new FridayFlatGuardService(cfg, tracker, _ => { });

        var d2a = svc2.Check(utc);
        Assert.True(d2a.ShouldForceCloseNow, "Before MarkFired: should still fire");

        tracker.MarkFired(bkk);

        var d2b = svc2.Check(utc);
        Assert.False(d2b.ShouldForceCloseNow, "After MarkFired: must NOT fire again same week");
        Assert.True(d2b.IsEntryBlocked,        "Entry must still be blocked even after close fires");
    }

    // ── Test 4: Saturday → entry blocked ─────────────────────────────────────

    [Fact]
    public void Saturday_EntryBlocked()
    {
        // Saturday 2026-06-13 any time
        var svc = Build();
        var utc = Utc(2026, 6, 13, 5, 0); // 12:00 BKK

        var decision = svc.Check(utc);

        Assert.False(decision.ShouldForceCloseNow, "No force-close on Saturday");
        Assert.True(decision.IsEntryBlocked,        "Saturday is fully blocked");
        Assert.True(svc.IsEntryBlocked(utc));
    }

    // ── Test 5: Sunday → entry blocked ───────────────────────────────────────

    [Fact]
    public void Sunday_EntryBlocked()
    {
        var svc = Build();
        var utc = Utc(2026, 6, 14, 10, 0); // Sunday 17:00 BKK

        Assert.True(svc.Check(utc).IsEntryBlocked);
        Assert.True(svc.IsEntryBlocked(utc));
    }

    // ── Test 6: Monday 05:59 Bangkok = 22:59 UTC Sunday → still blocked ─────

    [Fact]
    public void Monday_Before_ResumeTime_StillBlocked()
    {
        // Monday 2026-06-15 22:59 UTC (Sunday evening UTC!) = Monday 05:59 BKK
        var svc = Build();
        var utc = Utc(2026, 6, 14, 22, 59); // Sunday 22:59 UTC = Monday 05:59 BKK

        var decision = svc.Check(utc);

        Assert.False(decision.ShouldForceCloseNow, "No force-close on Monday");
        Assert.True(decision.IsEntryBlocked,        "Still blocked before 06:00 BKK Monday");
        Assert.True(svc.IsEntryBlocked(utc));
    }

    // ── Test 7: Monday 06:00 Bangkok = 23:00 UTC Sunday → RESUMED ────────────

    [Fact]
    public void Monday_At_ResumeTime_Resumed()
    {
        // Monday 2026-06-15 23:00 UTC (Sunday 23:00 UTC) = Monday 06:00 BKK
        var svc = Build();
        var utc = Utc(2026, 6, 14, 23, 0); // Sunday 23:00 UTC = Monday 06:00 BKK

        var decision = svc.Check(utc);

        Assert.False(decision.ShouldForceCloseNow, "No force-close after resume");
        Assert.False(decision.IsEntryBlocked,       "Must be UNBLOCKED at 06:00 BKK Monday");
        Assert.False(svc.IsEntryBlocked(utc));
    }

    // ── Test 8: EnableFridayFlatGuard=false → complete no-op ─────────────────

    [Fact]
    public void Disabled_Guard_IsNoOp()
    {
        var svc = Build(enabled: false);

        // Friday 18:00 BKK
        var friday = Utc(2026, 6, 12, 11, 0);
        var d1 = svc.Check(friday);
        Assert.False(d1.ShouldForceCloseNow);
        Assert.False(d1.IsEntryBlocked);
        Assert.False(svc.IsEntryBlocked(friday));

        // Saturday
        var saturday = Utc(2026, 6, 13, 5, 0);
        var d2 = svc.Check(saturday);
        Assert.False(d2.ShouldForceCloseNow);
        Assert.False(d2.IsEntryBlocked);
        Assert.False(svc.IsEntryBlocked(saturday));

        // Monday before resume
        var mondayEarly = Utc(2026, 6, 14, 22, 59);
        var d3 = svc.Check(mondayEarly);
        Assert.False(d3.ShouldForceCloseNow);
        Assert.False(d3.IsEntryBlocked);
        Assert.False(svc.IsEntryBlocked(mondayEarly));
    }

    // ── Bonus: SkipReasonCodes constants exist ────────────────────────────────

    [Fact]
    public void SkipReasonCodes_ContainFridayFlatConstants()
    {
        Assert.Equal("FRIDAY_FLAT_BLOCK",
            TradeAlert.BacktestRobot.Execution.Analytics.Loop6SkipReasonCodes.FridayFlatBlock);
        Assert.Equal("FRIDAY_FLAT_CLOSE",
            TradeAlert.BacktestRobot.Execution.Analytics.Loop6SkipReasonCodes.FridayFlatClose);
    }

    // ── Bonus: FridayFlatCloseTracker week-key logic ─────────────────────────

    [Fact]
    public void FridayFlatCloseTracker_SameFridayDoesNotFireTwice()
    {
        var tracker = new FridayFlatCloseTracker();
        var bkk = new DateTime(2026, 6, 12, 18, 0, 0); // Friday 18:00 BKK

        Assert.True(tracker.ShouldFire(bkk), "First check should fire");
        tracker.MarkFired(bkk);
        Assert.False(tracker.ShouldFire(bkk), "Same week must not fire again");

        // Different week
        var nextFriday = new DateTime(2026, 6, 19, 18, 0, 0);
        Assert.True(tracker.ShouldFire(nextFriday), "Different week should fire again");
    }

    // ── Bonus: FridayFlatGuardConfig.ParseTime ───────────────────────────────

    [Theory]
    [InlineData("18:00", 18, 0)]
    [InlineData("06:00",  6, 0)]
    [InlineData("00:00",  0, 0)]
    public void FridayFlatGuardConfig_ParseTime_Valid(string s, int h, int m)
    {
        var ts = FridayFlatGuardConfig.ParseTime(s);
        Assert.Equal(h, ts.Hours);
        Assert.Equal(m, ts.Minutes);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("bad")]
    public void FridayFlatGuardConfig_ParseTime_Invalid_ReturnsZero(string? s)
    {
        var ts = FridayFlatGuardConfig.ParseTime(s!);
        Assert.Equal(TimeSpan.Zero, ts);
    }
}
