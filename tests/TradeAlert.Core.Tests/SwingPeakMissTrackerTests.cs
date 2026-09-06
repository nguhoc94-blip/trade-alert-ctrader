using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.KeyLevel;
using Xunit;

namespace TradeAlert.Core.Tests;

public sealed class SwingPeakMissTrackerTests
{
    [Fact]
    public void NoteExtremeLoss_keeps_higher_price_hint_in_detail()
    {
        var t = new SwingPeakMissTracker();
        t.NoteExtremeLoss(100, 158.60, "A3", 98, 158.55, winConfirmed: true);

        Assert.True(t.TryGet(100, out var e));
        Assert.Equal("EXT_LOSS", e.Code);
        Assert.Contains("↑here", e.Detail);
        Assert.Contains("A3", e.Detail);
    }

    [Fact]
    public void RefreshLivePenH_marks_waitB_for_pending_candidate()
    {
        var t = new SwingPeakMissTracker();
        var q = new PendingSwingQueue();
        q.Push(new PendingSwingEntry { Bar = 50, Price = 1.25, BBar = -1, BTier = 0 });

        t.RefreshLivePenH(q, evalBarIndex: 55, bMaxLag: 6, cdMaxLag: 7);

        Assert.True(t.TryGet(50, out var e));
        Assert.Equal("WAIT_B", e.Code);
        Assert.Contains("q1/1", e.Detail);
        Assert.Contains("age=", e.Detail);
    }

    [Fact]
    public void NoteExpired_exp_ovf_includes_fifo_detail()
    {
        var t = new SwingPeakMissTracker();
        t.NoteExpired(100, 1.30, "EXP_OVF", bBar: 98, detail: "FIFO 8>7 oldest q1/8");

        Assert.True(t.TryGet(100, out var e));
        Assert.Equal("EXP_OVF", e.Code);
        Assert.Contains("FIFO", e.Detail);
        Assert.Contains("8>7", e.Detail);
    }

    [Fact]
    public void Picked_entry_is_not_overwritten_by_wait_state()
    {
        var t = new SwingPeakMissTracker();
        t.NotePicked(50, 1.25, "A1");

        var q = new PendingSwingQueue();
        q.Push(new PendingSwingEntry { Bar = 50, Price = 1.25, BBar = 48, BTier = 2 });
        t.RefreshLivePenH(q, evalBarIndex: 55, bMaxLag: 6, cdMaxLag: 7);

        Assert.True(t.TryGet(50, out var e));
        Assert.Equal("PICKED", e.Code);
    }

    [Fact]
    public void NoteFifoSnapshot_marks_all_eight_entries_before_evict()
    {
        var t = new SwingPeakMissTracker();
        var q = new PendingSwingQueue();
        for (var b = 100; b <= 107; b++)
            q.Push(new PendingSwingEntry { Bar = b, Price = 1.20 + b * 0.001, BBar = b - 1, BTier = 1 });

        t.NoteFifoSnapshot(q, cdMaxLag: 7);

        Assert.True(t.TryGet(100, out var drop));
        Assert.Equal("FIFO_DROP", drop.Code);
        Assert.Contains("q1/8", drop.Detail);
        Assert.Contains("→evict", drop.Detail);

        for (var b = 101; b <= 107; b++)
        {
            Assert.True(t.TryGet(b, out var mem));
            Assert.Equal("FIFO_MEM", mem.Code);
            Assert.Contains("8>7", mem.Detail);
            Assert.Contains($"q{b - 99}/8", mem.Detail);
        }
    }

    [Fact]
    public void RefreshLivePenH_does_not_overwrite_fifo_mem_snapshot()
    {
        var t = new SwingPeakMissTracker();
        var q = new PendingSwingQueue();
        for (var b = 100; b <= 107; b++)
            q.Push(new PendingSwingEntry { Bar = b, Price = 1.25, BBar = 98, BTier = 1 });
        t.NoteFifoSnapshot(q, cdMaxLag: 7);

        t.RefreshLivePenH(q, evalBarIndex: 110, bMaxLag: 6, cdMaxLag: 7);

        Assert.True(t.TryGet(102, out var e));
        Assert.Equal("FIFO_MEM", e.Code);
        Assert.Contains("q3/8", e.Detail);
    }
}
