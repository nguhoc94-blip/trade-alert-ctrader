# Loop 4 — AlertEngine mapping (`lib_alerts new` + 12 `alertcondition`)

**Loop 6:** see consolidated index [Loop6_Mapping_Master.md](Loop6_Mapping_Master.md).

Trạng thái: `implemented` | `skeleton_guarded` | `carry_over` | `not_in_loop4` | `mapping_gap`.

## Pure helper `lib_alerts new`

| Pine export | C# | Status | Notes |
|-------------|-----|--------|--------|
| `f_check_already_triggered` | `AlertDedupHelpers.CheckAlreadyTriggered` | **implemented** | Không đủ để fire EVENT alerts thật (per Engineering lock). |
| `f_price_touch_level` / `f_price_touch_range` | (chưa) | **not_in_loop4** | Dùng khi port touch/zones. |
| `f_check_touch_zones` | (chưa) | **skeleton_guarded** | Phụ thuộc zone arrays. |
| `f_collect_active_zones` | (chưa) | **skeleton_guarded** | Phụ thuộc pivot `box` / OB — C# dùng `KeyBoxRef`/stores Loop 3. |

## 12 `alertcondition` (Pine L5322–5335)

| # | Alert name | Pine condition | C# target | Timing fire | Duplicate key fields (canonical) | Reason / log fields | Status |
|---|------------|----------------|-----------|-------------|----------------------------------|---------------------|--------|
| 1 | BUY EVENT M5 | `condBuyEventM5` | `AlertEngine.CondBuyEventM5` | M5 close + `f_detect_events_raw(false,false)` | `sym|tf|alert|pine|barT|barI|srcTf|…|ev` | `AlertLogEntry.*` + `ReasonCode` | **skeleton_guarded** |
| 2 | SELL EVENT M5 | `condSellEventM5` | `AlertEngine.CondSellEventM5` | same | same | same | **skeleton_guarded** |
| 3 | BUY EVENT HL M15 | `condBuyEventHLM15` | `AlertEngine.CondBuyEventHLM15` | M15 close + events | same | same | **skeleton_guarded** |
| 4 | SELL EVENT HL M15 | `condSellEventHLM15` | `AlertEngine.CondSellEventHLM15` | same | same | same | **skeleton_guarded** |
| 5 | BUY EVENT NG M15 | `condBuyEventNGM15` | `AlertEngine.CondBuyEventNGM15` | same | same | same | **skeleton_guarded** |
| 6 | SELL EVENT NG M15 | `condSellEventNGM15` | `AlertEngine.CondSellEventNGM15` | same | same | same | **skeleton_guarded** |
| 7 | CAN BUY TOUCH M5 | `canBuyTouchM5` | `AlertEngine.CanBuyTouchM5` | realtime bar 0 touch | + optional zone key | same | **skeleton_guarded** |
| 8 | CAN SELL TOUCH M5 | `canSellTouchM5` | `AlertEngine.CanSellTouchM5` | same | same | same | **skeleton_guarded** |
| 9 | CAN BUY REAL | `canBuyReal` | `AlertEngine.CanBuyReal` | realtime filter bar 0 | + filter epoch (future) | same | **skeleton_guarded** |
| 10 | CAN SELL REAL | `canSellReal` | `AlertEngine.CanSellReal` | same | same | same | **skeleton_guarded** |
| 11 | CAN BUY REAL @ M15 CLOSE | `canBuyReal  and m15CloseNow` | `AlertEngine.CanBuyRealAndM15CloseNow` | `ta.change(time("15"))` ∧ realtime | `m15edge` inject | `CARRY_OVER_MISSING_M15_EDGE` nếu thiếu inject | **carry_over** (edge) + **skeleton_guarded** (body) |
| 12 | CAN SELL REAL @ M15 CLOSE | `canSellReal and m15CloseNow` | `AlertEngine.CanSellRealAndM15CloseNow` | same | same | same | **carry_over** + **skeleton_guarded** |

## Infrastructure Loop 4 (implemented)

| Component | Status |
|-----------|--------|
| `AlertConditionId` (12) | **implemented** |
| `AlertDefinition` registry | **implemented** |
| `AlertEngine.Evaluate` + guards | **implemented** |
| `DuplicateKey.ToCanonicalString` | **implemented** |
| `AlertDuplicateMemoryStore` | **implemented** (no silent eviction; `ResetSession` / `RemoveOlderThanBarIndex`) |
| `AlertLogEntry` + `ToLogEntry` | **implemented** |
| `IAlertFireSink` | **implemented** (interface only) |

## Known `mapping_gap` (doc theo lock Engineering)

- ATR provider / series tại lag so với Pine `ta.atr` — **mapping_gap** (KeyLevel Loop 3 inject scalar).
- Pivot break machine, OB lifecycle transitions đầy đủ — **mapping_gap** vs Pine main.
- `MicroSwingDetect` true port — **mapping_gap** / carry Loop 3.
- M15 clock `ta.change(time("15"))` — **carry_over**; host flag `M15CloseEdgeInjected` only.
- Zone collection từ OB/key boxes — **mapping_gap** hình học vs Pine `array<box>`.
- Triggered array lifecycle vs in-memory duplicate store eviction policy — **mapping_gap**.
