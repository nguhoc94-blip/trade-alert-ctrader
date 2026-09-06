# Loop 6 — Parity report schema (baseline only)

## Scope

- **In scope:** OHLC + bar index/time + normalized bar-state flags from **TradingView / DATA CHART** style feeds — **not** production broker ticks as parity truth.
- **Out of scope:** Claiming 100% match on production feed.

## Record fields (CSV header or JSON keys)

| Field | Required | Description |
|-------|----------|-------------|
| `sample_type` | yes | `baseline_chart_export` \| `synthetic_readiness` |
| `symbol` | yes | |
| `chart_timeframe` | yes | e.g. `M5`, `M15` |
| `source_bar_index` | yes | long |
| `source_bar_open_time_chart_local` | yes | ISO-8601, Unspecified policy |
| `open` | yes | double |
| `high` | yes | double |
| `low` | yes | double |
| `close` | yes | double |
| `bar_closed` | yes | bool |
| `is_realtime` | yes | bool |
| `evaluation_offset` | no | int? |
| `pine_event_summary` | no | text — **only if exported from Pine baseline** |
| `pine_state_summary` | no | text |
| `csharp_condition_id` | no | e.g. `CondBuyEventM5` |
| `csharp_evaluated` | no | bool |
| `csharp_fired` | no | bool |
| `csharp_reason_code` | no | string |
| `match_status` | yes | `match` \| `mismatch` \| `not_comparable` |
| `mismatch_reason` | no | text when mismatch |
| `known_difference_ref` | no | stable id from `Loop6_Known_Differences_Schema.md` |

## Rules (Engineering locks)

1. **No fake `match`:** If there is no Pine event/state baseline row for the same bar, `match_status` MUST be `not_comparable`, not `match`.
2. **Synthetic samples:** Must set `sample_type=synthetic_readiness` and typically `not_comparable`.
3. **Production observation** — never used alone to set `match`; use known-differences doc (`production_observation` group).

## Sample file

See `docs/parity/samples/parity_sample_minimal.json`.
