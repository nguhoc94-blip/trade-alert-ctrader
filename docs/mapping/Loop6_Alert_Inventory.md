# Loop 6 — Alert inventory (13 `alertcondition`)

Evidence: Pine `lib_alerts` / main script alertconditions (`pine code arlert new.pine`) + `pine code alert debug.pine`; C# `AlertEngine.BuildDefinitions`, `AlertConditionId`.

**P1 disposition** (`closed_by_artifact` | `known_difference_or_risk_accepted` | `blocker_do_not_send_QA`):

| # | Alert name | Pine source | Pine condition | C# target | Timing (class) | P1 disposition |
|---|------------|------------|----------------|-----------|----------------|----------------|
| 1 | BUY EVENT M5 | arlert new | `condBuyEventM5` | `AlertEngine.CondBuyEventM5` | `BarCloseM5Event` | **closed_by_artifact** — port complete; TF gate fixed (A.2) |
| 2 | SELL EVENT M5 | arlert new | `condSellEventM5` | `CondSellEventM5` | `BarCloseM5Event` | same |
| 3 | BUY EVENT HL M15 | arlert new | `condBuyEventHLM15` | `CondBuyEventHLM15` | `BarCloseM15Event` | **closed_by_artifact** |
| 4 | SELL EVENT HL M15 | arlert new | `condSellEventHLM15` | `CondSellEventHLM15` | `BarCloseM15Event` | same |
| 5 | BUY EVENT NG M15 | arlert new | `condBuyEventNGM15` | `CondBuyEventNGM15` | `BarCloseM15Event` | same |
| 6 | SELL EVENT NG M15 | arlert new | `condSellEventNGM15` | `CondSellEventNGM15` | `BarCloseM15Event` | same |
| 7 | CAN BUY TOUCH M5 | arlert new | `canBuyTouchM5` | `CanBuyTouchM5` | `RealtimeTouchBar0` | **closed_by_artifact** — zones collect via `ZoneCollector` |
| 8 | CAN SELL TOUCH M5 | arlert new | `canSellTouchM5` | `CanSellTouchM5` | `RealtimeTouchBar0` | same |
| 9 | CAN BUY REAL | arlert new | `canBuyReal` | `CanBuyReal` | `RealtimeFilterBar0` | **closed_by_artifact** — eff* expand fixed (A.1); fail-closed fixed (B.2) |
| 10 | CAN SELL REAL | arlert new | `canSellReal` | `CanSellReal` | `RealtimeFilterBar0` | same |
| 11 | CAN BUY REAL @ M15 CLOSE | arlert new | `canBuyReal and m15CloseNow` | `CanBuyRealAndM15CloseNow` | `RealtimeFilterBar0WithM15CloseEdge` | **closed_by_artifact** — M15 edge-only fix (A.4) |
| 12 | CAN SELL REAL @ M15 CLOSE | arlert new | `canSellReal and m15CloseNow` | `CanSellRealAndM15CloseNow` | `RealtimeFilterBar0WithM15CloseEdge` | same |
| 13 | Phá Khung Lớn | alert debug | `condPhaKhungLon` | `CondPhaKhungLon` | `BarCloseAnyTfPhaKhungLon` | **closed_by_artifact** — new; all TF confirmed bar; Event A/B/C |

**Reason / log:** `AlertLogEntry` + `ReasonCode` from `AlertEvaluationResult` (see `Loop6_AlertLog_Schema.md`).

**Engineering lock:** No production `ForceM15Edge`; host-injected edge only.

## Phá Khung Lớn detail

Pine source: `pine code alert debug.pine` lines ~5296–5332. Không có trong `arlert new.pine`.

| Event | Pine condition | C# |
|-------|---------------|-----|
| A | `flagNow==2` (BROKEN) | `FlagNow==2 && dedup.TriggeredA_PKL.Add(sid+"_2")` |
| B | `flagNow==-1` (MAIN BROKEN) | `FlagNow==-1 && dedup.TriggeredB_PKL.Add(sid+"_-1")` |
| C | `flagNow==2 && !extNow && keyStopBar==bar_index && kb!=na` | `FlagNow==2 && HasKeyBox && !KeyExtending && KeyStopBar==barIndex` — dedup `sid+"_C"+barIndex` |

Direction swap KHÔNG áp dụng (không tách buy/sell). Dedup sets `TriggeredA/B/C_PKL` trong `EventDedupSets`.
