using System.Globalization;
using cAlgo.API;
using TradeAlert.Core.Models;
using TradeAlert.Core.Models.Alerts;
using TradeAlert.Core.Models.KeyLevel;
using TradeAlert.Indicator;
using TradeAlert.Indicator.Host;

namespace TradeAlert.Robot.Host;

/// <summary>
/// Minimal cBot — shares <see cref="TradeAlertIndicator"/> + <see cref="AlertPipelineHost"/> with indicator.
/// Logs signal evaluations on each closed bar; no orders, AccessRights.None.
/// </summary>
[Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
public class TradeAlertSignalRobot : cAlgo.API.Robot
{
    TradeAlertIndicator? _shell;
    readonly HostLtfCollector _ltfCollector = new();
    int _barsProcessed;

    [Parameter("Enable Alerts", DefaultValue = true)]
    public bool EnableAlerts { get; set; }

    [Parameter("Message Prefix", DefaultValue = "[Loop6Bot]")]
    public string MessagePrefix { get; set; } = "[Loop6Bot]";

    protected override void OnStart()
    {
        _barsProcessed = 0;
        var tfTok = Loop6ChartTimeframeToken.FromBarsTimeFrame(Bars.TimeFrame);
        var hostCtx = new IndicatorHostContext
        {
            Symbol = Symbol.Name,
            ChartTimeframeToken = tfTok,
            HostChartIsRealtime = false,
        };

        IAlertFireSink sink = EnableAlerts
            ? new PrintOnlyAlertFireSink(
                s => Print(s),
                evt => $"{MessagePrefix} FIRED {evt.Symbol} {evt.ConditionId} bar={evt.SourceBarIndex}")
            : VoidAlertFireSink.Instance;

        _shell = new TradeAlertIndicator(hostCtx, sink);
        _shell.State.TickSize = Symbol.TickSize;
        _shell.State.IsM5Mode = tfTok == "5";
        _ltfCollector.Configure(Bars.TimeFrame, tfTok);
        _ltfCollector.TryRebind(MarketData);
        _shell.State.ObLtfMappingResolved = _ltfCollector.HasLtfMapping;

        _shell.Series.InitialBackfill(Bars.Count - 1, i =>
        {
            if (i < 0 || i >= Bars.Count) return null;
            var bar = ToBar(i);
            var flags = Loop6BarRuntimeMapper.ForNonRealtimePlatformBar(i, Bars.Count - 1);
            return (bar, flags);
        });

        WarmUpPineStateWithLtfHistory();

        Print($"{MessagePrefix} OnStart symbol={Symbol.Name} tf={tfTok} bars={Bars.Count} ltfReady={_ltfCollector.IsConfigured}");
    }

    /// <summary>Mirror indicator Calculate sweep — fill LTF buffer cho toàn bộ lịch sử (OB confirm cần buffer).</summary>
    void WarmUpPineStateWithLtfHistory()
    {
        if (_shell == null || Bars.Count == 0)
            return;

        var lastInclusive = Bars.Count - 1;
        for (var i = 0; i <= lastInclusive; i++)
        {
            var flags = Loop6BarRuntimeMapper.ForNonRealtimePlatformBar(i, lastInclusive);
            _shell.Series.OnCalculateBar(i, ToBar(i), flags);

            LtfBarBundle? ltfSnap = null;
            if (i >= 1)
                ltfSnap = _ltfCollector.CollectForClosedHtfBarWithRebind(MarketData, Bars, i - 1);

            _shell.State.OnBar(_shell.Series.Buffer, i, _shell.Render, ltfSnap);
        }
    }

    protected override void OnBar()
    {
        if (_shell == null || Bars.Count < 2)
            return;

        var evalIndex = Bars.Count - 1;
        var closedBarIndex = evalIndex - 1;
        if (closedBarIndex < 0)
            return;

        var closedFlags = Loop6BarRuntimeMapper.ForClosedChartBarBeforeLast();
        var closedBar = ToBar(closedBarIndex);
        _shell.Series.OnCalculateBar(closedBarIndex, closedBar, closedFlags);

        var formingFlags = Loop6BarRuntimeMapper.ForBacktestFormingLastBar(isNewBarViaOpenTime: true);
        _shell.Series.OnCalculateBar(evalIndex, ToBar(evalIndex), formingFlags);

        var ltfSnap = _ltfCollector.CollectForClosedHtfBarWithRebind(MarketData, Bars, closedBarIndex);

        _shell.State.OnBar(_shell.Series.Buffer, evalIndex, _shell.Render, ltfSnap);

        var tfTok = Loop6ChartTimeframeToken.FromBarsTimeFrame(Bars.TimeFrame);
        EvaluateClosedBar(closedBarIndex, tfTok, closedBar);
        _barsProcessed++;
    }

    protected override void OnStop()
    {
        Print($"{MessagePrefix} OnStop barsProcessed={_barsProcessed} logRows={_shell?.Alerts.LogEntries.Count ?? 0}");
        _shell?.OnStopOrReload();
    }

    void EvaluateClosedBar(int index, string tfTok, BarSnapshot bar)
    {
        var alerts = _shell!.Alerts;
        var ctx = Loop6EvaluationContextFactory.Build(
            _shell,
            index,
            tfTok,
            bar.OpenChartTimeLocal,
            bar.Close,
            bar.High,
            bar.Low,
            isBarClosed: true,
            isRealtime: false);

        foreach (var id in alerts.Engine.Registry.Keys)
        {
            var def = alerts.Engine.Registry[id];
            var dup = new DuplicateKey(
                ctx.Symbol, ctx.ChartTimeframeToken, def.Title, def.PineConditionSymbol,
                ctx.SourceBarOpenTimeChartLocal, ctx.SourceBarIndex, ctx.ChartTimeframeToken);
            alerts.EvaluateRecordAndMaybeFire(id, in ctx, bar.OpenChartTimeLocal, in dup, appendLog: true);
        }
    }

    BarSnapshot ToBar(int i) =>
        BarsToCoreAdapter.ToBarSnapshot(
            Bars.OpenTimes[i],
            Bars.OpenPrices[i],
            Bars.HighPrices[i],
            Bars.LowPrices[i],
            Bars.ClosePrices[i],
            (long)Bars.TickVolumes[i]);
}
