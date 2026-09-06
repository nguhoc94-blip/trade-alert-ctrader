# Loop 6 — Alert / state log schema (`AlertLogEntry`)

## Implemented (C# — source of truth)

`TradeAlert.Core.Models.Alerts.AlertLogEntry`:

| Field | Type (concept) | Notes |
|-------|----------------|-------|
| FireTimestampChartLocal | DateTime | Chart-local |
| Symbol | string | |
| Timeframe | string | Chart TF |
| AlertName | string | |
| AlertType | string | |
| ConditionId | string | Enum stringified |
| SourceBarTimeChartLocal | DateTime | |
| SourceBarIndex | long | |
| IsCurrentBar | bool | |
| IsBarClosed | bool | |
| IsRealtime | bool | |
| IsBarCloseTiming | bool | |
| EvaluationOffset | int? | Pine `[n]` analogue |
| ReasonCode | string | Guard / not-fired |
| ReasonText | string | |
| StateSnapshotRef | optional ref | `AlertStateSnapshotRef?` |
| DuplicateKeyCanonical | string | `DuplicateKey.ToCanonicalString()` |
| PineConditionSymbol | string | |
| CSharpTarget | string | |

Alignment verified against `AlertEngine.ToLogEntry` (Loop 4/5).

## QA optional fields (documentation only — **not** in runtime type Loop 6)

Do **not** require these in code without Engineering approval; use parity / known-diff rows instead:

- `qa_reviewed_by`, `qa_ticket_id`
- `pine_line_ref` (file + line)
- `parity_row_id` (link to `Loop6_Parity_Report_Schema`)

If Product needs persistence, propose Loop 7+ DTO — **no** schema break in Loop 6.

## Reason codes

Populate from `AlertEvaluationResult` (`ReasonCode` / dependency flags). See tests `AlertPipelineHostWiringTests`, `M15EdgeIndicatorIntegrationTests` for guarded examples.
