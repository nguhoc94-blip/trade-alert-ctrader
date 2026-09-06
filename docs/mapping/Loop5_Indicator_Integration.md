# Loop 5 — Indicator integration (cTrader shell)

**Loop 6:** [Loop6_Mapping_Master.md](Loop6_Mapping_Master.md).

Chart-local time follows Loop 2/3 policy: `DateTimeKind.Unspecified` = chart-local wall clock; UTC instants convert via fixed `ChartTimePolicy` (+7); `Kind=Local` rejected in `ChartTimeFromBars`.

| Component / path | Role | Status |
|------------------|------|--------|
| `src/TradeAlert.Indicator/TradeAlertIndicator.cs` | Shell: wires `SeriesBufferSync`, `AlertPipelineHost`, `MinimalRenderSink`, `M15EdgeHostSignal`; session reset on reload | `implemented` |
| `ChartTimeFromBars` | `Bars.OpenTimes` → Core canonical open time | `implemented` |
| `BarsToCoreAdapter` | Map platform bar row → `BarSnapshot` + flags | `implemented` |
| `IndicatorHostContext` | Symbol / timeframe token for eval context | `implemented` |
| `SeriesBufferSync` | One-time `InitialBackfill`; `OnCalculateBar` single-index `Upsert` | `implemented` |
| `MinimalRenderSink` | `IDrawingCommandSink`; status / marker queue; `ClearCommands` on reload | `implemented` |
| `AlertPipelineHost` | `AlertEngine` evaluate → log always; duplicate store per session; optional `IAlertFireSink` | `implemented` |
| `ListAlertFireSink` | In-memory fire sink for tests / harness | `implemented` |
| `M15EdgeHostSignal` | Host-injected edge per evaluation; default false; no `ta.change` simulation | `implemented` |
| cTrader `Calculate` / SDK reference | Platform hook | `not_in_loop5` (skeleton host; no SDK ref in this workspace) |
| `TouchRealEvaluators` true-fire without full deps | Production parity | `skeleton_guarded` / `mapping_gap` where evaluators require more state |
| `TimeframeEngine` / `SignalAggregator` full | Pine-scale aggregation | `carry_over` / `not_in_loop5` |
| cBot / backtest / orders | — | `not_in_loop5` |

## Wiring contract (Bars → Core)

1. Normalize time with `ChartTimeFromBars.NormalizeBarOpenTime(openTime)`.
2. Build `BarSnapshot` / `BarRuntimeFlags` via `BarsToCoreAdapter` (or equivalent).
3. `SeriesBufferSync.InitialBackfill(highestIndex, tryGet)` once after session start.
4. On each bar close / tick path: `OnCalculateBar(index, bar, flags)` only.

## M15 edge

Host must call `M15EdgeHostSignal.SetHostM15CloseEdgeForCurrentEvaluation(bool)` at the start of each evaluation cycle. Default is false; no automatic M15-close simulation.
