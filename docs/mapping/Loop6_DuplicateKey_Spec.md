# Loop 6 — DuplicateKey specification

**Type:** `TradeAlert.Core.Models.Alerts.DuplicateKey` (`DuplicateKey.cs`).

## Canonical string

Built by `ToCanonicalString()` — stable order, invariant culture timestamp:

```
sym={Symbol}|tf={ChartTimeframe}|alert={AlertName}|pine={PineConditionSymbol}|barT={yyyy-MM-ddTHH:mm:ss.fff}|barI={SourceBarIndex}|srcTf={SourceTimeframeToken}|piv={PivotId?}|ob={ObPoolIndex?}|struct={StructId?}|ev={EventFamilyKey?}
```

- `barT` uses **chart-local** open time (`DateTimeKind.Unspecified` policy via constructor).

## Field semantics (dedup / log)

| Field | Role |
|-------|------|
| Symbol | Instrument |
| ChartTimeframe | Chart TF token |
| AlertName | Human alert title |
| PineConditionSymbol | Pine predicate id string (e.g. `condBuyEventM5`) |
| SourceBarOpenTimeChartLocal | Bar open used for key |
| SourceBarIndex | Bar index |
| SourceTimeframeToken | Source series TF |
| PivotId / ObPoolIndex / StructId / EventFamilyKey | Optional disambiguators (touch / EVENT family) |

Registry describes human-readable dup template per condition (`AlertDefinition.DuplicateKeyFieldDescriptor`).

## Session lifecycle

- Store: `IAlertDuplicateStore` / `AlertDuplicateMemoryStore` — per session, explicit `ResetSession`; no silent eviction (Engineering lock).
- Indicator wiring: `AlertPipelineHost.ResetSession` on reload/stop.

## Pine `f_check_already_triggered` ↔ C#

- **Mapping/carry-over:** Pine uses triggered state / arrays across executions; C# uses **per-session** duplicate store + canonical string keys.
- **mapping_gap:** Long-lived Pine array eviction / max history vs in-memory session policy — document for QA; do not claim 1:1 lifecycle parity in Loop 6.

## Evidence

Source: `DuplicateKey.cs`, `AlertEngine.cs`, `AlertPipelineHost.cs`, `Loop4_AlertEngine_Mapping.md`.
