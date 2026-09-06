using System.Collections.Generic;
using TradeAlert.Core.Models.KeyLevel;

namespace TradeAlert.Core.Engines;

/// <summary>
/// Rolling store of why recent bars were not pushed as swing HIGH (đỉnh).
/// Updated by <see cref="PullbackFilterEngine"/> and flushed by
/// <see cref="SwingPeakMissDebugLabelEngine"/>.
/// </summary>
public sealed class SwingPeakMissTracker
{
    public const int WindowBars = 10;

    readonly Dictionary<int, Entry> _entries = new();

    public readonly record struct Entry(
        int    Bar,
        double Price,
        string Code,
        string Detail);

    public void Clear() => _entries.Clear();

    public void Prune(int evalBarIndex)
    {
        var minBar = evalBarIndex - WindowBars;
        var remove = new List<int>();
        foreach (var kv in _entries)
            if (kv.Key < minBar)
                remove.Add(kv.Key);
        foreach (var b in remove)
            _entries.Remove(b);
    }

    public void NoteNoCandidate(int bar, double price, string reason) =>
        Upsert(bar, price, "NO_CAND", reason);

    public void NoteQueued(int bar, double price, int initBBar)
    {
        if (initBBar >= 0)
            Upsert(bar, price, "WAIT_CD", $"B@{initBBar}");
        else
            Upsert(bar, price, "WAIT_B", "");
    }

    public void NotePicked(int bar, double price, string rule) =>
        Upsert(bar, price, "PICKED", rule);

    public void NoteExtremeLoss(int bar, double price, string rule, int winBar, double winPrice, bool winConfirmed)
    {
        var cmp = price > winPrice ? "↑here" : "↓here";
        var winTag = winConfirmed ? "conf" : "unconf";
        Upsert(bar, price, "EXT_LOSS", $"{rule}→#{winBar} {winPrice:0.#####} {winTag} {cmp}");
    }

    public void NoteUnconfirmed(int bar, double price, int bBar, string? reason = null)
    {
        var bPart = bBar >= 0 ? $" B@{bBar}" : "";
        var detail = string.IsNullOrEmpty(reason) ? bPart.TrimStart() : $"{reason}{bPart}";
        Upsert(bar, price, "UNCONF", detail);
    }

    public void NoteClearedOpp(int bar, double price, int oppWinBar, string oppDir = "LOW") =>
        Upsert(bar, price, "OPP_CLR", $"{oppDir} batch win #{oppWinBar}");

    public void NoteExpired(int bar, double price, string status, int bBar, string? detail = null)
    {
        var code = status switch
        {
            "EXP_NO_B"  => "EXP_NO_B",
            "EXP_NO_CD" => "EXP_NO_CD",
            "EXP_OVF"   => "EXP_OVF",
            _           => "EXP",
        };
        if (string.IsNullOrEmpty(detail))
            detail = bBar >= 0 ? $"B@{bBar}" : "";
        Upsert(bar, price, code, detail);
    }

    /// <summary>penH entry cleared by micro-swing rescue (ClearRange between N and A).</summary>
    public void NoteMicroQueueClear(int bar, double price, int nBar, int aBar, int microBar) =>
        Upsert(bar, price, "MICRO_CLR", $"μ clr ({nBar},{aBar}) M#{microBar}");

    public void NoteAltBlocked(int bar, double price) =>
        Upsert(bar, price, "ALT_BLK", "last=HIGH");

    /// <summary>
    /// Marks every entry in an overflow queue (e.g. 8&gt;7) before FIFO eviction.
    /// q1 is tagged <c>FIFO_DROP</c>; survivors q2..qN get <c>FIFO_MEM</c>.
    /// </summary>
    public void NoteFifoSnapshot(PendingSwingQueue queue, int cdMaxLag)
    {
        var count = queue.Count;
        if (count <= cdMaxLag) return;

        for (var i = 0; i < count; i++)
        {
            var e = queue[i];
            var pos = $"q{i + 1}/{count}";
            var tag = $"{pos} {count}>{cdMaxLag}";
            if (i == 0)
                Upsert(e.Bar, e.Price, "FIFO_DROP", $"{tag} →evict");
            else
                Upsert(e.Bar, e.Price, "FIFO_MEM", tag);
        }
    }

    public void RefreshLivePenH(PendingSwingQueue penH, int evalBarIndex, int bMaxLag, int cdMaxLag)
    {
        var minBar = evalBarIndex - WindowBars;
        var qSize = penH.Count;
        for (var qi = 0; qi < qSize; qi++)
        {
            var e = penH[qi];
            if (e.Bar < minBar) continue;
            if (_entries.TryGetValue(e.Bar, out var cur) && cur.Code is "PICKED" or "FIFO_MEM" or "EXP_OVF")
                continue;

            var qPos = $"q{qi + 1}/{qSize}";
            var age = (evalBarIndex - 1) - e.Bar;
            if (e.BBar == -1)
            {
                var left = bMaxLag - age;
                var agePart = left >= 0 ? $"age={age} BxLeft={left}" : $"age={age} BxOver";
                Upsert(e.Bar, e.Price, "WAIT_B", $"{qPos} {agePart}");
            }
            else
            {
                var cdLeft = cdMaxLag - age;
                Upsert(e.Bar, e.Price, "WAIT_RULE", $"{qPos} B@{e.BBar} t{e.BTier} CDxLeft={cdLeft}");
            }
        }
    }

    public IEnumerable<Entry> WindowEntries(int evalBarIndex)
    {
        for (var lag = 1; lag <= WindowBars; lag++)
        {
            var bar = evalBarIndex - lag;
            if (bar < 0) continue;
            if (_entries.TryGetValue(bar, out var e))
                yield return e;
        }
    }

    public bool TryGet(int bar, out Entry entry) => _entries.TryGetValue(bar, out entry);

    void Upsert(int bar, double price, string code, string detail)
    {
        if (code != "PICKED" && _entries.TryGetValue(bar, out var cur) && cur.Code == "PICKED")
            return;

        _entries[bar] = new Entry(bar, price, code, detail);
    }
}
