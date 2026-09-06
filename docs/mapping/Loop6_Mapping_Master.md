# Loop 6 — Mapping master bundle (Pine → C# status index)

**Baseline:** Loop 5 C# source + read-only Pine (`lib_alerts new`, main alert script).  
**Status vocabulary:** `implemented` | `skeleton_guarded` | `carry_over` | `not_in_scope` | `mapping_gap`

## Document map

| Loop | Document | Role |
|------|----------|------|
| 2 | `Loop2_Pine_to_CSharp_Mapping.md` | Early Pine → C# constructs |
| 3 | `Loop3_Pine_to_CSharp_Mapping.md` | Key level / box / swing mapping |
| 4 | `Loop4_AlertEngine_Mapping.md` | 12 `alertcondition`, AlertEngine, dedup |
| 5 | `Loop5_Indicator_Integration.md` | cTrader shell, bars adapter, M15 host edge |
| 6 | `Loop6_Alert_Inventory.md` | Consolidated alert table + P1 disposition |
| 6 | `Loop6_HTF_LTF_Mapping.md` | HTF chart vs LTF wick (`security_lower_tf`) vs noise/M5/M15 (`security`) |
| 6 | `Loop6_Swing_Rules_Mapping.md` | Swing pipeline Pine → C# |
| 6 | `Loop6_OB_Keylevel_Mapping.md` | OB pool, struct OB, key broken/fake, opacity |
| 6 | `Loop6_MTF_BarTiming_Contract.md` | Timing / barstate / offset per condition |
| 6 | `Loop6_DuplicateKey_Spec.md` | `DuplicateKey` ↔ Pine triggered helpers |
| 6 | `Loop6_AlertLog_Schema.md` | `AlertLogEntry` + QA extensions |
| 6 | `docs/parity/Loop6_Parity_Report_Schema.md` | Baseline parity CSV/JSON |
| 6 | `docs/known_differences/Loop6_Known_Differences_Schema.md` | 3-way taxonomy + stable IDs |

## Module / state rollup (summary)

| Pine area | C# target (primary) | Status |
|-----------|----------------------|--------|
| `lib_alerts new` helpers | `AlertDedupHelpers`, `AlertEngine` | mixed: helpers **implemented**, touch/zone **skeleton_guarded** / **mapping_gap** |
| 12 `alertcondition` | `AlertConditionId`, evaluators | **skeleton_guarded** (true-fire blocked without deps) |
| `ta.change(time("15"))` / M15 close | `M15EdgeHostSignal` + host inject | **carry_over** (no Pine simulation in Core) |
| `request.security` / LTF arrays | — | **mapping_gap** / **not_in_scope** Loop 6 |
| Bar state (`barstate.isconfirmed`, realtime) | `BarRuntimeFlags`, eval context | **implemented** (flags); full Pine parity **mapping_gap** |
| ATR / Pivot / OB / MicroSwing | KeyLevel / series (Loop 3) | **mapping_gap** vs Pine (see known-differences) |

## QA traceability

Indexed in `docs/qa/loop6_bundle_index.json` (path, purpose, dependency, AC, status, owner, blocker ref).
