# Loop 6 — MTF / bar timing contract (all 12 conditions)

**Clock policy:** chart-local wall `DateTimeKind.Unspecified`; UTC → fixed offset (+7); `Local` reject (`ChartTimeFromBars`). No machine `ToLocalTime()` for parity keys.

Legend: **src TF** = timeframe of *condition evaluation context*; **bar source** = series driving the alert; **offset** = evaluation bar index vs chart (Pine `[0]` = forming, `[1]` last closed, etc., descriptive — align with `BarRuntimeFlags`).

| # | Condition | src TF | Bar source | Offset / bar role | `is_bar_closed` | `is_realtime` | `bar_close_timing` | M15 edge | Dependency | Status |
|---|-----------|--------|------------|-------------------|-----------------|---------------|-------------------|----------|------------|--------|
| 1–2 | EVENT M5 | M5 | Chart M5 | M5 bar close (event family) | true at fire | false at close eval | yes (M5 close) | n/a | `f_detect_events_raw` path | skeleton_guarded |
| 3–6 | EVENT M15 * | M15 | Chart M15 | M15 bar close | true at fire | false at close | yes (M15 close) | n/a | same / HL / NG variants | skeleton_guarded |
| 7–8 | TOUCH M5 | M5 | M5 | bar **0** touch eval | forming bar | **true** | no | n/a | zone geometry | skeleton_guarded |
| 9–10 | REAL | chart TF | bar **0** filter | filter on realtime | forming | **true** | no | n/a | realtime filter state | skeleton_guarded |
| 11–12 | REAL @ M15 CLOSE | chart TF | bar **0** + M15 clock | filter ∧ M15 close edge | mixed | **true** | M15 close **edge** | **host `M15EdgeHostSignal`** | inject per eval cycle | carry_over |

### Pine ↔ C# gaps (carry-over / mapping_gap)

- Pine `ta.change(time("15"))`: **not** simulated in Core — **mapping_gap**; host must set `M15CloseEdgeInjected` (Loop 5).
- `barstate.isconfirmed` / `islast`: mapped via `BarRuntimeFlags` where available; full Pine semantics **mapping_gap**.
- **HTF/LTF semantics:** `Loop6_HTF_LTF_Mapping.md` — wick = `security_lower_tf`; M5/M15/noise = `request.security` (khác nhau).
- `request.security_lower_tf` (wick LTF bundle): **mapping_gap** — `TimeframeMapping` + `ILtfBarBundle` ready; host feed pending.
- Fixed M5/M15 `request.security`: **carry_over** (alert host).

### Evidence

C#: `AlertTimingClass`, `AlertEngine` registry, `M15EdgeHostSignal`, `BarRuntimeFlags`.  
Doc: `Loop5_Indicator_Integration.md`, `Loop4_AlertEngine_Mapping.md`.
