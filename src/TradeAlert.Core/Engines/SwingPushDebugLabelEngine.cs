using TradeAlert.Core.Drawing;
using TradeAlert.Core.Models.KeyLevel;

namespace TradeAlert.Core.Engines;

/// <summary>
/// Debug labels for swing candidates and confirmed swings.
/// <list type="bullet">
///   <item><b>Confirmed/pushed:</b> fuchsia label at bar A showing rule (A1/A2/A3/A4/MICRO).</item>
///   <item><b>Expired no-B:</b> gray "×" at bar A — candidate never found B in time.</item>
///   <item><b>Expired no-CD:</b> orange "×B" at bar A — found B but no rule confirmed C/D.</item>
/// </list>
/// Objects use prefix <c>dbg_sw_*</c> / <c>dbg_cnd_*</c> so <see cref="PivotLabelEngine"/> never touches them.
/// </summary>
public static class SwingPushDebugLabelEngine
{
    /// <summary>Fuchsia ~70% opacity — matches Pine micro debug label style.</summary>
    public const uint ConfirmedColorArgb = 0xB3FF00FFu;

    /// <summary>Gray — candidate expired before finding B.</summary>
    public const uint ExpiredNoBColorArgb = 0xFF808080u;

    /// <summary>Orange — candidate expired: B found but C/D never confirmed.</summary>
    public const uint ExpiredNoCdColorArgb = 0xFFFF8C00u;

    /// <summary>Dark-gray — candidate forcibly removed from overflow queue.</summary>
    public const uint ExpiredOvfColorArgb = 0xFF505050u;

    /// <summary>Purple — survivor in an 8-entry FIFO overflow queue (before eviction).</summary>
    public const uint FifoMemColorArgb = 0xFFAB47BCu;

    /// <summary>Yellow — confirmed by A1/A2/A3/A4 but overridden by EXTREME rule (another bar had a more extreme cleanHigh/Low).</summary>
    public const uint ConfNotPickedColorArgb = 0xFFFFCC00u;

    /// <summary>Light gray — in pending queue but never confirmed by any rule; silently cleared when another entry triggered the batch.</summary>
    public const uint ClearedUnconfColorArgb = 0xFF909090u;

    /// <summary>Teal — entry was in the OPPOSITE queue (e.g. penL entry cleared by HIGH batch); shows that bar entered both queues.</summary>
    public const uint ClearedOppBatchColorArgb = 0xFF00B8B8u;

    /// <summary>How many recent bars to emit expired-candidate labels for (independent of LookbackBars).</summary>
    public const int CandidateDebugWindowBars = 500;

    public static string ConfirmedChartObjectName(int typ, int highId, int lowId) =>
        typ == 1 ? $"dbg_sw_H{highId}" : $"dbg_sw_L{lowId}";

    public static string CandidateChartObjectName(int type, int candBar) =>
        type == 1 ? $"dbg_cnd_H{candBar}" : $"dbg_cnd_L{candBar}";

    // ── Confirmed/pushed swing ────────────────────────────────────────────────

    public static void EmitCreate(
        PivotStateStore pivots,
        int pivotIdx,
        string pushReason,
        int evalBarIndex,
        int lookbackBars,
        IDrawingCommandSink? sink)
    {
        if (sink == null || string.IsNullOrWhiteSpace(pushReason))
            return;
        if (pivotIdx < 0 || pivotIdx >= pivots.Count)
            return;
        if (!PivotLabelEngine.IsPivotVisible(pivots, pivotIdx, evalBarIndex, lookbackBars))
            return;

        var typ   = pivots.GetTypeAt(pivotIdx);
        var hid   = pivots.GetHighId(pivotIdx);
        var lid   = pivots.GetLowId(pivotIdx);
        var bar   = pivots.GetBarIndexInternal(pivotIdx);
        var price = pivots.GetPivotPrice(pivotIdx);
        var sid   = typ == 1 ? $"H{hid:D3}" : $"L{lid:D3}";
        var dir   = typ == 1 ? "HIGH" : "LOW";
        var rule  = FormatRule(pushReason);

        sink.Enqueue(new DrawingCommand
        {
            Kind        = DrawingCommandKind.AddLabel,
            LabelKeyStr = ConfirmedChartObjectName(typ, hid, lid),
            BarIndex    = bar,
            Price       = price,
            IsHigh      = typ == 1,
            Text        = $"✓{rule}\n{dir} {sid}\n#{bar}",
            ColorArgb   = ConfirmedColorArgb,
        });
    }

    // ── Expired/failed candidates ─────────────────────────────────────────────

    /// <summary>
    /// Emit a "failed" debug label for each expired candidate in <paramref name="diags"/>.
    /// Only emits when candidate bar is within <see cref="CandidateDebugWindowBars"/> of <paramref name="evalBarIndex"/>.
    /// </summary>
    public static void EmitCandidateDiags(
        IReadOnlyList<PullbackFilterEngine.SwingCandidateDiag> diags,
        int evalBarIndex,
        IDrawingCommandSink? sink)
    {
        if (sink == null || diags.Count == 0) return;

        foreach (var d in diags)
        {
            if (evalBarIndex - d.CandBar > CandidateDebugWindowBars) continue;

            string text;
            uint   color;
            var typeLabel = d.Type == 1 ? "H" : "L";

            if (d.Status.StartsWith("CONF_NOT_PICKED:", StringComparison.Ordinal))
            {
                // Confirmed by a rule but overridden by EXTREME (another bar was more extreme)
                var rsn = d.Status.Substring("CONF_NOT_PICKED:".Length);
                text  = $"⊗{FormatRule(rsn)}{typeLabel}";
                color = ConfNotPickedColorArgb;
            }
            else if (d.Status == "CLEARED_UNCONF")
            {
                var bLabel = d.BBar >= 0 ? $"\nB@{d.BBar}" : string.Empty;
                var detail = string.IsNullOrEmpty(d.Detail) ? bLabel : $"\n{d.Detail}{bLabel}";
                text  = $"—{typeLabel}{detail}";
                color = ClearedUnconfColorArgb;
            }
            else if (d.Status == "CLEARED_OPP_BATCH")
            {
                var bLabel = d.BBar >= 0 ? $"\nB@{d.BBar}" : string.Empty;
                var detail = string.IsNullOrEmpty(d.Detail) ? bLabel : $"\n{d.Detail}{bLabel}";
                text  = $"~{typeLabel}{detail}";
                color = ClearedOppBatchColorArgb;
            }
            else if (d.Status == "FIFO_MEM")
            {
                var detail = string.IsNullOrEmpty(d.Detail) ? string.Empty : $"\n{d.Detail}";
                text  = $"◆{typeLabel}{detail}";
                color = FifoMemColorArgb;
            }
            else
            {
                var bLabel = d.BBar >= 0 && string.IsNullOrEmpty(d.Detail) ? $"\nB@{d.BBar}" : string.Empty;
                var detail = string.IsNullOrEmpty(d.Detail) ? bLabel : $"\n{d.Detail}";
                (text, color) = d.Status switch
                {
                    "EXP_NO_B"  => ($"×{typeLabel}{detail}",  ExpiredNoBColorArgb),
                    "EXP_NO_CD" => ($"×B{typeLabel}{detail}", ExpiredNoCdColorArgb),
                    "EXP_OVF"   => ($"×Q{typeLabel}{detail}", ExpiredOvfColorArgb),
                    _           => ($"?{typeLabel}{detail}",  ExpiredNoBColorArgb),
                };
            }

            sink.Enqueue(new DrawingCommand
            {
                Kind        = DrawingCommandKind.AddLabel,
                LabelKeyStr = CandidateChartObjectName(d.Type, d.CandBar),
                BarIndex    = d.CandBar,
                Price       = d.CandPrice,
                IsHigh      = d.Type == 1,
                Text        = text,
                ColorArgb   = color,
            });
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static string FormatRule(string reason) =>
        reason switch
        {
            "MICRO"           => "μ",
            "A4_DOJI_SELL"    => "A4↓",
            "A4_DOJI_BUY"     => "A4↑",
            "BYPASS_H"        => "BYPASS↑",
            "BYPASS_L"        => "BYPASS↓",
            _                 => reason
        };
}
