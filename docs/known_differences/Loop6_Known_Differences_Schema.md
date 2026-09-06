# Loop 6 — Known differences & risk acceptance (stable IDs)

Taxonomy (**minimum 3 groups** per Engineering):

1. **`parity_baseline_gap`** — Missing or incomplete **structured Pine event/state export** or OHLC/bar-state baseline for an alert; parity rows must be `not_comparable`, not `match`.
2. **`production_observation`** — Feed latency, broker vs chart, live-only quirks; **never** used to conclude baseline parity.
3. **`runtime_not_verified`** — cTrader SDK attach, full true-fire, `TimeframeEngine`, ATR/Pivot/MicroSwing/LTF arrays not proven in Loop 6 runtime.

## Stable IDs (for `known_difference_ref` in parity rows)

| ID | Group | Summary | P1 status |
|----|-------|---------|-----------|
| KD-PBG-001 | parity_baseline_gap | No bundled structured Pine event export for bar-index join in this artifact | **known_difference_or_risk_accepted** — QA reviews schema/readiness only; **must not** conclude 100% alert parity |
| KD-PO-001 | production_observation | Production feed differs from DATA CHART / TV export (timing, gaps) | **known_difference_or_risk_accepted** |
| KD-RNV-001 | runtime_not_verified | cTrader SDK attach / `Calculate` integration not verified in Builder env | **known_difference_or_risk_accepted** |
| KD-RNV-002 | runtime_not_verified | True-fire EVENT/TOUCH/REAL requires deps not fully ported (zones, filters, events) | **known_difference_or_risk_accepted** |
| KD-RNV-003 | runtime_not_verified | `request.security` / LTF arrays — **mapping_gap**, not in Loop 6 scope | **known_difference_or_risk_accepted** |
| KD-RNV-004 | runtime_not_verified | M15 close edge — host inject only; no `ta.change(time("15"))` in Core | **closed_by_artifact** (documented in Loop5/6 MTF contract) |

**Risk acceptance (explicit):** If structured Pine event baseline is absent, **QA shall not** sign off “100% parity match” for alert/state columns; sign-off limited to **schema correctness**, **inventory completeness**, and **readiness** per checklist.

## Blocker rule

If Builder **cannot** produce a **schema-valid** sample JSON at all → **P0** `blocker_do_not_send_QA`. Loop 6 ships valid sample + `not_comparable` — **PASS packaging** per task 8–9.
