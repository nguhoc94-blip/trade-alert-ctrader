using System.Collections.Generic;

namespace TradeAlert.Core.Engines.AlertEvaluators;

/// <summary>Per-shell cache — one raw detection + tracking labels per bar/TF/M15-mode.</summary>
public sealed class EventDetectionSessionCache
{
    sealed class ModeSlot
    {
        public int BarIndex = -1;
        public string TfToken = "";
        public EventRawCounts Counts;
        public int EmittedBarIndex = -1;
        public string EmittedTfToken = "";
        public readonly List<EventAlertTrackingLabel> TrackingLabels = new();
    }

    readonly ModeSlot _m5 = new();
    readonly ModeSlot _m15 = new();

    public IReadOnlyList<EventAlertTrackingLabel> TrackingLabels => _m15.TrackingLabels;

    ModeSlot Slot(bool isM15) => isM15 ? _m15 : _m5;

    public void Reset()
    {
        ResetSlot(_m5);
        ResetSlot(_m15);
    }

    static void ResetSlot(ModeSlot slot)
    {
        slot.BarIndex = -1;
        slot.TfToken = "";
        slot.TrackingLabels.Clear();
        slot.Counts = default;
        slot.EmittedBarIndex = -1;
        slot.EmittedTfToken = "";
    }

    public bool TryGet(int barIndex, string tfToken, bool isM15, out EventRawCounts counts)
    {
        var slot = Slot(isM15);
        if (slot.BarIndex == barIndex && slot.TfToken == tfToken)
        {
            counts = slot.Counts;
            return true;
        }

        counts = default;
        return false;
    }

    public bool HasTrackingLabels(int barIndex, string tfToken, bool isM15)
    {
        var slot = Slot(isM15);
        return slot.BarIndex == barIndex
               && slot.TfToken == tfToken
               && slot.TrackingLabels.Count > 0;
    }

    public void Store(
        int barIndex,
        string tfToken,
        bool isM15,
        in EventRawCounts counts,
        IList<EventAlertTrackingLabel>? trackingLabels)
    {
        var slot = Slot(isM15);
        slot.BarIndex = barIndex;
        slot.TfToken = tfToken;
        slot.Counts = counts;
        slot.TrackingLabels.Clear();
        if (trackingLabels != null)
            slot.TrackingLabels.AddRange(trackingLabels);

        // Allow re-draw when detection re-runs with debug labels on the same bar.
        slot.EmittedBarIndex = -1;
        slot.EmittedTfToken = "";
    }

    /// <summary>Draw tracking labels once per bar/TF/mode.</summary>
    public bool TryEmitTracking(
        int barIndex,
        string tfToken,
        bool isM15,
        System.Action<IReadOnlyList<EventAlertTrackingLabel>>? drawFn)
    {
        var slot = Slot(isM15);
        if (slot.EmittedBarIndex == barIndex && slot.EmittedTfToken == tfToken)
            return false;

        if (slot.BarIndex != barIndex || slot.TfToken != tfToken)
            return false;

        drawFn?.Invoke(slot.TrackingLabels);

        slot.EmittedBarIndex = barIndex;
        slot.EmittedTfToken = tfToken;
        return slot.TrackingLabels.Count > 0;
    }
}
