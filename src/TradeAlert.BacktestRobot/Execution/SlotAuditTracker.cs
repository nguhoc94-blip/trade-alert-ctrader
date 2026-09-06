namespace TradeAlert.BacktestRobot.Execution;

/// <summary>Per-slot (R1–R6) counters for backtest parity / execution audit on Stop.</summary>
public sealed class SlotAuditTracker
{
    public const int SlotCount = 6;

    readonly int[] _fire = new int[SlotCount];
    readonly int[] _plan = new int[SlotCount];
    readonly int[] _gatePass = new int[SlotCount];
    readonly int[] _gateSkip = new int[SlotCount];
    readonly int[] _orders = new int[SlotCount];
    readonly int[] _pendingCancelPostBPushObstacle = new int[SlotCount];
    readonly int[] _pendingCancelPostBPushObstacleSameTf = new int[SlotCount];
    readonly int[] _pendingCancelPostBPushObstacleCrossTf = new int[SlotCount];
    readonly int[] _pendingCancelPostBPushObstacleCrossTfSelfSkipped = new int[SlotCount];
    readonly int[] _pendingCancelHlM15BcAbRatio = new int[SlotCount];

    public void RecordFire(int slotIndex)
    {
        if ((uint)slotIndex < SlotCount)
            _fire[slotIndex]++;
    }

    public void RecordPlan(int slotIndex)
    {
        if ((uint)slotIndex < SlotCount)
            _plan[slotIndex]++;
    }

    public void RecordGatePass(int slotIndex)
    {
        if ((uint)slotIndex < SlotCount)
            _gatePass[slotIndex]++;
    }

    public void RecordGateSkip(int slotIndex)
    {
        if ((uint)slotIndex < SlotCount)
            _gateSkip[slotIndex]++;
    }

    public void RecordOrder(int slotIndex)
    {
        if ((uint)slotIndex < SlotCount)
            _orders[slotIndex]++;
    }

    public void RecordPendingCancelPostBPushObstacle(int slotIndex)
    {
        if ((uint)slotIndex < SlotCount)
            _pendingCancelPostBPushObstacle[slotIndex]++;
    }

    /// <summary>Record the per-TF match-kind for the cancel: <c>sameTf</c> when the obstacle zone shared the
    /// chart TF; <c>crossTf</c> when it came from a different TF (after the new cross-TF self-identity filter).
    /// Helps verify whether <see cref="PostBPushObstacleCancelRule"/> still over-fires across TFs.</summary>
    public void RecordPendingCancelPostBPushObstacleMatchKind(int slotIndex, bool sameTf)
    {
        if ((uint)slotIndex >= SlotCount) return;
        if (sameTf) _pendingCancelPostBPushObstacleSameTf[slotIndex]++;
        else        _pendingCancelPostBPushObstacleCrossTf[slotIndex]++;
    }

    /// <summary>Accumulate the count of zones filtered out by the cross-TF self-identity guard. Non-zero means
    /// the new check actually blocked self-overlap cancels that the legacy bar_index match would have missed.</summary>
    public void AddPendingCancelPostBPushObstacleCrossTfSelfSkipped(int slotIndex, int n)
    {
        if ((uint)slotIndex >= SlotCount || n <= 0) return;
        _pendingCancelPostBPushObstacleCrossTfSelfSkipped[slotIndex] += n;
    }

    public void RecordPendingCancelHlM15BcAbRatio(int slotIndex)
    {
        if ((uint)slotIndex < SlotCount)
            _pendingCancelHlM15BcAbRatio[slotIndex]++;
    }

    public (int fire, int plan, int gatePass, int gateSkip, int orders) Get(int slotIndex) =>
        ((uint)slotIndex < SlotCount)
            ? (_fire[slotIndex], _plan[slotIndex], _gatePass[slotIndex], _gateSkip[slotIndex], _orders[slotIndex])
            : (0, 0, 0, 0, 0);

    public int GetPendingCancelPostBPushObstacle(int slotIndex) =>
        (uint)slotIndex < SlotCount ? _pendingCancelPostBPushObstacle[slotIndex] : 0;

    public int GetPendingCancelPostBPushObstacleSameTf(int slotIndex) =>
        (uint)slotIndex < SlotCount ? _pendingCancelPostBPushObstacleSameTf[slotIndex] : 0;

    public int GetPendingCancelPostBPushObstacleCrossTf(int slotIndex) =>
        (uint)slotIndex < SlotCount ? _pendingCancelPostBPushObstacleCrossTf[slotIndex] : 0;

    public int GetPendingCancelPostBPushObstacleCrossTfSelfSkipped(int slotIndex) =>
        (uint)slotIndex < SlotCount ? _pendingCancelPostBPushObstacleCrossTfSelfSkipped[slotIndex] : 0;

    public int GetPendingCancelHlM15BcAbRatio(int slotIndex) =>
        (uint)slotIndex < SlotCount ? _pendingCancelHlM15BcAbRatio[slotIndex] : 0;
}
