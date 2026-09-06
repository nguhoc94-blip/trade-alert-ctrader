# Loop 6 — QA handoff checklist (Product / QA)

**Inbound:** Baseline from `/mnt/data/code_cTrader_LOOP5_REV001.zip`; Loop 6 artifact `code_cTrader_LOOP6_REV001.zip` + manifest.

## A. Artifact integrity

- [ ] `code_cTrader_LOOP6_REV001_manifest.json` parses JSON
- [ ] SHA-256, `zip_size_bytes`, `entry_count` match zip
- [ ] `required_presence` paths exist in unpacked tree
- [ ] `docs/qa/loop6_bundle_index.json` parses; every Loop 6 doc listed

## B. Documentation completeness

- [ ] `Loop6_Mapping_Master.md` — links Loop2–6
- [ ] `Loop6_Alert_Inventory.md` — 12 rows + P1 disposition wording (no vague “later”)
- [ ] `Loop6_MTF_BarTiming_Contract.md` — covers all 12 conditions
- [ ] `Loop6_DuplicateKey_Spec.md` — canonical + lifecycle + Pine carry-over
- [ ] `Loop6_AlertLog_Schema.md` — matches `AlertLogEntry`
- [ ] `Loop6_Parity_Report_Schema.md` + `parity_sample_minimal.json` — **no** `match_status: match` without Pine baseline
- [ ] `Loop6_Known_Differences_Schema.md` — 3 groups + stable IDs

## C. Scope & safety

- [ ] Negative scope grep clean (cBot, backtest, auto trade, order execution, risk) in `src/` + `docs/`
- [ ] No instruction to enable true-fire without deps
- [ ] Parity conclusions: baseline DATA CHART / TV only; production separated

## D. Build (optional if .NET available)

- [ ] `dotnet build` solution
- [ ] `dotnet test` — record pass/fail in Engineering inbox report

## E. Sign-off limit

Confirm understanding: **without structured Pine event export**, QA validates **readiness/schema**, **not** 100% behavioral parity (per `KD-PBG-001`).
