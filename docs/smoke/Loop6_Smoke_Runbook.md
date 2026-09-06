# Loop 6 — Smoke runbook

## 1. Environment

- .NET 6 SDK (optional but recommended)
- Unpack `code_cTrader_LOOP6_REV001.zip` → work in inner `code cTrader/`

## 2. Core build & test

```powershell
cd "code cTrader"
dotnet build
dotnet test
```

Expect: build 0 errors; tests **71** passed (baseline Loop 5 + indicator tests).

## 3. Indicator / docs static smoke

- Confirm paths in `docs/qa/loop6_bundle_index.json` exist
- Parse JSON: `parity_sample_minimal.json`, `loop6_bundle_index.json`

```powershell
Get-Content "docs\parity\samples\parity_sample_minimal.json" | ConvertFrom-Json | Out-Null
Get-Content "docs\qa\loop6_bundle_index.json" | ConvertFrom-Json | Out-Null
```

## 4. Negative scope scan (`src` + `docs`)

1. **`src/`:** `Select-String` with patterns from manifest `negative_scope_grep.patterns` (Engineering task: no cBot/backtest/auto trade/order execution/risk-management **implementation**). Expect **0 matches** in `.cs` files.
2. **`docs/`:** allowable mentions only in **out-of-scope** / checklist / runbook context (tables stating «not implemented», QA bullets). If automated grep flags mapping tables, use human triage per `Builder_gui_Engineering_v011.md`.

Do **not** treat documentary “not in scope” lines as shipping guidance to enable trading automation.

## 5. Artifact verification

Compare manifest `checksum_sha256` / size / `entry_count` to actual zip.

## 6. QA handoff

Execute `docs/qa/Loop6_QA_Handoff_Checklist.md`.

## 7. cTrader SDK

If SDK missing: **skip** attach; mark **runtime_not_verified** (`KD-RNV-001`).
