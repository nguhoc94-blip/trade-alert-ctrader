using System;
using TradeAlert.Core.Engines;
using TradeAlert.Core.Models.Alerts;

namespace TradeAlert.Indicator;

/// <summary>
/// Duplicate store per session; nhật ký chẩn đoán <see cref="LogEntries"/>.
/// Tham số <c>appendLog</c> của <see cref="EvaluateRecordAndMaybeFire"/> chỉ điều khiển gắng thêm hàng vào <see cref="LogEntries"/>.
/// Không đổi Evaluate, DuplicateKey, duplicate store, phiên hay fire sink.
/// </summary>
public sealed class AlertPipelineHost
{
    readonly AlertEngine _engine = new();
    readonly IAlertDuplicateStore _duplicateStore;
    readonly IAlertFireSink? _fireSink;

    public AlertPipelineHost(IAlertDuplicateStore? duplicateStore = null, IAlertFireSink? fireSink = null)
    {
        _duplicateStore = duplicateStore ?? new AlertDuplicateMemoryStore();
        _fireSink = fireSink;
    }

    public IAlertDuplicateStore DuplicateStore => _duplicateStore;

    public AlertEngine Engine => _engine;

    public List<AlertLogEntry> LogEntries { get; } = new();

    /// <summary>Giữ log chẩn đoán gọn — xóa mục cũ nhất; không đụng duplicate store hay session sink.</summary>
    public int MaxDebugLogEntries { get; set; } = 1000;

    public void ResetSession()
    {
        _duplicateStore.ResetSession();
        LogEntries.Clear();
    }

    public void EvaluateRecordAndMaybeFire(
        AlertConditionId id,
        in AlertEvaluationContext ctx,
        DateTime fireTimestampChartLocal,
        in DuplicateKey duplicateKey,
        bool appendLog = true)
    {
        var def = _engine.Registry[id];
        var result = _engine.Evaluate(id, in ctx);
        var log = AlertEngine.ToLogEntry(def, in ctx, in result, in duplicateKey, fireTimestampChartLocal);
        if (appendLog)
        {
            LogEntries.Add(log);
            TrimLogEntriesOldestFirst();
        }

        if (!result.Fired)
            return;

        if (!_duplicateStore.TryRegister(in duplicateKey, out _))
            return;

        var ev = new AlertEvent
        {
            ConditionId = id,
            FireTimeChartLocal = fireTimestampChartLocal,
            SourceBarIndex = ctx.SourceBarIndex,
            SourceBarOpenTimeChartLocal = ctx.SourceBarOpenTimeChartLocal,
            SourceTimeframeToken = ctx.ChartTimeframeToken,
            Symbol = ctx.Symbol
        };
        _fireSink?.Enqueue(in ev);
    }

    void TrimLogEntriesOldestFirst()
    {
        var cap = MaxDebugLogEntries;
        if (cap <= 0)
            return;
        while (LogEntries.Count > cap)
            LogEntries.RemoveAt(0);
    }
}
