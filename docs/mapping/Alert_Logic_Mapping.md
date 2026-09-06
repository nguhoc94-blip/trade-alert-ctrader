# Alert Logic Mapping — Pine ↔ C#

> Tài liệu so sánh **luồng logic** và **code thật** giữa Pine Script gốc (file
> `NGUỒN DỰ ÁN BAN ĐẦU/PINE_CODE/pine code arlert new.pine` + `pine code alert debug.pine`)
> và bản port C# trong `src/TradeAlert.Core/Engines/AlertEvaluators/*.cs` +
> `src/TradeAlert.Core/Engines/ZoneCollector.cs` + `src/TradeAlert.Indicator/Loop6EvaluationContextFactory.cs`.
>
> **Phạm vi:** 13 alert conditions hiện hữu + 1 hàm hỗ trợ critical (`f_collect_active_zones`).
>
> **Quy ước cột:**
>
> 1. **Luồng logic Pine** — mô tả ngắn gọn các bước Pine thực hiện.
> 2. **Dẫn chứng code Pine** — số dòng + đoạn code thật.
> 3. **Luồng logic C#** — mô tả ngắn gọn các bước C# thực hiện (đã port).
> 4. **Dẫn chứng code C#** — file + line range + đoạn code thật.
> 5. **STT** — số thứ tự (1..n).
> 6. **% Pass logic** — mức độ khớp logic (100% = bit-for-bit; 95% = khớp về mặt hành vi nhưng có khác biệt do framework; <95% = còn gap).

---

## 1. Mục lục alert conditions

| Pine name              | C# `AlertConditionId`              | Loại                        | Trang mục |
|------------------------|------------------------------------|-----------------------------|-----------|
| `condBuyEventM5`       | `CondBuyEventM5`                   | EVENT (M5 bar close)        | §3.1      |
| `condSellEventM5`      | `CondSellEventM5`                  | EVENT (M5 bar close)        | §3.2      |
| `condBuyEventHLM15`    | `CondBuyEventHLM15`                | EVENT (M15 bar close, A)    | §3.3      |
| `condSellEventHLM15`   | `CondSellEventHLM15`               | EVENT (M15 bar close, A)    | §3.4      |
| `condBuyEventNGM15`    | `CondBuyEventNGM15`                | EVENT (M15 bar close, B/C)  | §3.5      |
| `condSellEventNGM15`   | `CondSellEventNGM15`               | EVENT (M15 bar close, B/C)  | §3.6      |
| `canBuyTouchM5`        | `CanBuyTouchM5`                    | TOUCH (mọi tick)            | §4.1      |
| `canSellTouchM5`       | `CanSellTouchM5`                   | TOUCH (mọi tick)            | §4.2      |
| `canBuyReal`           | `CanBuyReal`                       | REAL (mọi tick)             | §4.3      |
| `canSellReal`          | `CanSellReal`                      | REAL (mọi tick)             | §4.4      |
| `canBuyReal & m15Close`| `CanBuyRealAndM15CloseNow`         | REAL + M15 edge composite   | §4.5      |
| `canSellReal & m15Close`| `CanSellRealAndM15CloseNow`       | REAL + M15 edge composite   | §4.6      |
| `condPhaKhungLon`      | `CondPhaKhungLon`                  | PKL (mọi TF, mọi bar close) | §5.1      |

### 1.1. Bảng BUY ↔ SELL — cặp mirror (Pine ↔ C#)

| Hướng | Pine name | C# `AlertConditionId` | C# evaluator | Công thức fire (Pine) | Pivot / vùng | Loại compound |
|-------|-----------|----------------------|--------------|------------------------|--------------|---------------|
| **BUY** | `condBuyEventM5` | `CondBuyEventM5` | `EventFamilyEvaluators.EvaluateById` | `(buyHL_m5 > 0 \|\| buyNG_m5 > 0)` @ M5 close | LOW pivot (`typ==-1`) → HL/NG buy; Event C swap | **EVENT** |
| **SELL** | `condSellEventM5` | `CondSellEventM5` | `EventFamilyEvaluators.EvaluateById` | `(sellHL_m5 > 0 \|\| sellNG_m5 > 0)` @ M5 close | HIGH pivot (`typ==1`) → HL/NG sell; Event C swap | **EVENT** |
| **BUY** | `condBuyEventHLM15` | `CondBuyEventHLM15` | `EventFamilyEvaluators.EvaluateById` | `buyHL_m15 > 0` @ M15 close | Event A M15: `flagPrev!=2 && flagNow==2`, typ LOW | **EVENT** |
| **SELL** | `condSellEventHLM15` | `CondSellEventHLM15` | `EventFamilyEvaluators.EvaluateById` | `sellHL_m15 > 0` @ M15 close | Event A M15, typ HIGH | **EVENT** |
| **BUY** | `condBuyEventNGM15` | `CondBuyEventNGM15` | `EventFamilyEvaluators.EvaluateById` | `buyNG_m15 > 0` @ M15 close | Event B (MAIN_BROKEN) typ HIGH→buy; Event C typ LOW→buy | **EVENT** |
| **SELL** | `condSellEventNGM15` | `CondSellEventNGM15` | `EventFamilyEvaluators.EvaluateById` | `sellNG_m15 > 0` @ M15 close | Event B typ LOW→sell; Event C typ HIGH→sell | **EVENT** |
| **BUY** | `canBuyTouchM5` | `CanBuyTouchM5` | `TouchRealEvaluators.EvaluateTouchBuy` | `_tTouch == 1 \|\| _tTouch == 0` | Chạm green OB/key hoặc không zone chặn | **STATE** |
| **SELL** | `canSellTouchM5` | `CanSellTouchM5` | `TouchRealEvaluators.EvaluateTouchSell` | `_tTouch == -1 \|\| _tTouch == 0` | Chạm red OB/key hoặc không zone chặn | **STATE** |
| **BUY** | `canBuyReal` | `CanBuyReal` | `TouchRealEvaluators.EvaluateRealBuy` | `_tActive == 1 \|\| _tActive == 0` | `lastPushedSwingType==-1` → realBuy zone | **STATE** |
| **SELL** | `canSellReal` | `CanSellReal` | `TouchRealEvaluators.EvaluateRealSell` | `_tActive == -1 \|\| _tActive == 0` | `lastPushedSwingType==1` → realSell zone | **STATE** |
| **BUY** | `canBuyReal and m15CloseNow` | `CanBuyRealAndM15CloseNow` | `TouchRealEvaluators.EvaluateRealBuyAndM15Close` | `canBuyReal && m15CloseNow` | Như `canBuyReal` + cạnh M15 | **STATE** |
| **SELL** | `canSellReal and m15CloseNow` | `CanSellRealAndM15CloseNow` | `TouchRealEvaluators.EvaluateRealSellAndM15Close` | `canSellReal && m15CloseNow` | Như `canSellReal` + cạnh M15 | **STATE** |
| — | `condPhaKhungLon` | `CondPhaKhungLon` | `EventFamilyEvaluators.EvaluatePhaKhungLon` | Event A/B/C (không tách buy/sell) | Mọi pivot, mọi TF @ bar confirmed | **EVENT** |

**Ghi chú mirror BUY/SELL:**

- **EVENT M5:** cùng `f_detect_events_raw(m15Mode=false)` — buy dùng `typ==-1`, sell dùng `typ==1`.
- **EVENT M15 HL:** chỉ nhánh Event A (`buyHL` / `sellHL`).
- **EVENT M15 NG:** nhánh Event B + Event C với **direction swap** (LOW→BUY, HIGH→SELL).
- **TOUCH:** cùng `_tTouch`; buy chấp nhận green (`1`) hoặc neutral (`0`); sell chấp nhận red (`-1`) hoặc neutral (`0`).
- **REAL:** cùng `_tActive` trên active realzone; buy khi swing LOW vừa push; sell khi swing HIGH vừa push.
- **REAL @ M15:** composite — không có cặp Pine riêng ngoài `and m15CloseNow`.

**Pine alertcondition title (canonical):**

| Pine var | alert title |
|----------|-------------|
| `condBuyEventM5` | BUY EVENT M5 |
| `condSellEventM5` | SELL EVENT M5 |
| `condBuyEventHLM15` | BUY EVENT HL M15 |
| `condSellEventHLM15` | SELL EVENT HL M15 |
| `condBuyEventNGM15` | BUY EVENT NG M15 |
| `condSellEventNGM15` | SELL EVENT NG M15 |
| `canBuyTouchM5` | CAN BUY TOUCH M5 |
| `canSellTouchM5` | CAN SELL TOUCH M5 |
| `canBuyReal` | CAN BUY REAL |
| `canSellReal` | CAN SELL REAL |
| `canBuyReal and m15CloseNow` | CAN BUY REAL @ M15 CLOSE |
| `canSellReal and m15CloseNow` | CAN SELL REAL @ M15 CLOSE |

Dẫn chứng Pine: `pine code arlert new.pine:5216-5335`.

### 1.2. Dispatch C# — file hàm theo nhóm

| Nhóm | Entry point | File |
|------|-------------|------|
| EVENT M5/M15 (6 cond buy/sell) | `AlertEngine.Evaluate` → `EventFamilyEvaluators.EvaluateById` | `EventFamilyEvaluators.cs:51-179` |
| TOUCH M5 buy/sell | `TouchRealEvaluators.EvaluateTouchBuy` / `EvaluateTouchSell` | `TouchRealEvaluators.cs:34-71` |
| REAL buy/sell | `TouchRealEvaluators.EvaluateRealBuy` / `EvaluateRealSell` | `TouchRealEvaluators.cs` (`ComputeRealFlags`) |
| REAL @ M15 buy/sell | `EvaluateRealBuyAndM15Close` / `EvaluateRealSellAndM15Close` | `TouchRealEvaluators.cs:199-264` |
| PKL | `EventFamilyEvaluators.EvaluatePhaKhungLon` | `EventFamilyEvaluators.cs:189-253` |
| Registry + timing class | `AlertEngine.BuildDefinitions` | `AlertEngine.cs:43-104` |
| Compound parse (Pine name → id) | `CompoundRuleParser.PineNameMap` | `CompoundRuleParser.cs:23-39` |

**STATE vs EVENT trong compound panel** (`CompoundRuleParser.IsStateCondition`):

- **STATE** (Touch/Real/M15 composite): `[OK]` khi điều kiện đang true **ngay lúc eval** (`IsCurrentlyActive`).
- **EVENT** (M5/M15 event, PKL): `[OK]` phụ thuộc **Compound sync mode** (§7.2).

---

## 2. Hàm hỗ trợ critical

### 2.1. `f_collect_active_zones` — Phân loại zone xanh / đỏ

| Luồng logic Pine | Dẫn chứng code Pine | Luồng logic C# | Dẫn chứng code C# | STT | % Pass |
|---|---|---|---|---|---|
| Duyệt tất cả pivot `i`. **Skip** nếu `!pivHasKey[i]`, `na(kb)`, hoặc `!pivKeyExtending[i]`. | `pine code arlert new.pine:5276` (gọi hàm); `lib_alerts new.pine f_collect_active_zones` (~L75-105):<br>`if not array.get(pivHasKey, i) continue`<br>`box kb = array.get(pivKeyBox, i)`<br>`if na(kb) continue`<br>`if not array.get(pivKeyExtending, i) continue` | `for (i=0; i<Count; i++)` → bỏ qua nếu `!GetHasKey(i)`, `kb is null`, `!GetKeyExtending(i)`. | `ZoneCollector.cs:42-49`:<br>`if (!pivotKeyBoxes.GetHasKey(i)) continue;`<br>`var kb = pivotKeyBoxes.GetKeyBox(i);`<br>`if (kb is null) continue;`<br>`if (!pivotKeyBoxes.GetKeyExtending(i)) continue;` | 1 | 100% |
| Phân loại NON-broken: `typ==-1` (Low) → green, `typ==1` (High) → red, nếu `flag ∈ {1,3,4,7,5,50}` HOẶC `(flag==0 && mainRole==1)`. | `lib_alerts new.pine` ~L110-115:<br>`isGreenKey := (typ == -1) and (flag==1 or flag==3 or flag==4 or flag==7 or (flag==0 and mainRole==1) or flag==5 or flag==50)`<br>`isRedKey := (typ == 1) and (flag==1 or flag==3 or flag==4 or flag==7 or (flag==0 and mainRole==1) or flag==5 or flag==50)` | Cùng điều kiện boolean, lưu vào `isGreen` / `isRed`. | `ZoneCollector.cs:56-66`:<br>`bool nonBrokenMatch = flag == 1 \|\| flag == 3 \|\| flag == 4 \|\| flag == 7 \|\| (flag == 0 && mainRole == 1) \|\| flag == 5 \|\| flag == 50;`<br>`if (typ == -1 && nonBrokenMatch) isGreen = true;`<br>`else if (typ == 1 && nonBrokenMatch) isRed = true;` | 2 | 100% |
| Phân loại BROKEN bổ sung (separate IF — KHÔNG else-if): nếu `flag ∈ {-3, 2, -2, 6}` → broken HIGH (`typ==1`) thành GREEN (support sau khi vỡ); broken LOW (`typ==-1`) thành RED. | `lib_alerts new.pine` ~L116-120:<br>`if flag == -3 or flag == 2 or flag == -2 or flag == 6`<br>` if typ == 1`<br>` isGreenKey := true`<br>` else if typ == -1`<br>` isRedKey := true` | Cùng nhánh `if` riêng biệt — broken HIGH thành green, broken LOW thành red. | `ZoneCollector.cs:67-72`:<br>`if (flag == -3 \|\| flag == 2 \|\| flag == -2 \|\| flag == 6)`<br>`{`<br>` if (typ == 1) isGreen = true;`<br>` else if (typ == -1) isRed = true;`<br>`}` | 3 | 100% |
| OB zones: skip nếu `obState != 2` (chỉ ZIN), `!obExtending`, `na(ob)`. `obType==-1` (OB đỏ = support) → green; `obType==1` (OB xanh = resistance) → red. | `lib_alerts new.pine` ~L129-149:<br>`if obStates[i] != 2 continue`<br>`if not obExtending[i] continue`<br>`box ob = array.get(obBoxes, i)`<br>`if na(ob) continue`<br>`if obType == -1 → push green`<br>`if obType == 1 → push red` | Duyệt `obPool.Count`. Skip `r.State != 2`, `!r.Extending`, `r.Box is null`. Type -1 → green; Type 1 → red. | `ZoneCollector.cs:85-103`:<br>`var r = obPool.GetRecord(i);`<br>`if (r.State != 2) continue;`<br>`if (!r.Extending) continue;`<br>`if (r.Box is null) continue;`<br>`if (r.Type == -1) greenOb.Add(...); else if (r.Type == 1) redOb.Add(...);` | 4 | 100% |

### 2.2. `f_check_touch_zones(rangeLow, rangeHigh, zones…)` — Touch check

| Luồng logic Pine | Dẫn chứng code Pine | Luồng logic C# | Dẫn chứng code C# | STT | % Pass |
|---|---|---|---|---|---|
| `f_price_touch_range(rangeLow, rangeHigh, zoneLow, zoneHigh) = !(priceHigh < zoneLow \|\| priceLow > zoneHigh)`. Trả về 2 nếu chạm cả green+red; 1 nếu chỉ green; -1 nếu chỉ red; 0 nếu không. | `lib_alerts new.pine` ~L155-200 (theo doc Loop6_Alert_Inventory). | Single-price (`Collect`) hoặc range (`TouchReal`): Iterate green key → green OB → red key → red OB; trả về `2 / 1 / -1 / 0`. | `ZoneCollector.cs:114-128` (single price);<br>`TouchRealEvaluators.cs:287-307` (range): `if (!(rangeHigh < z.Low \|\| rangeLow > z.High))`. | 5 | 100% |

---

## 3. EVENT family (M5 / M15 bar close, A/B/C)

### 3.1. `condBuyEventM5`

| Luồng logic Pine | Dẫn chứng code Pine | Luồng logic C# | Dẫn chứng code C# | STT | % Pass |
|---|---|---|---|---|---|
| Chỉ chạy khi `currentTF=="5" && m5_just_closed`. Gọi `f_detect_events_raw(m15Mode=false, …)` → `buyHL_m5, buyNG_m5`. Cond = `buyHL_m5 > 0 \|\| buyNG_m5 > 0`. | `pine code arlert new.pine:5222-5229`:<br>`if currentTF == "5" and m5_just_closed`<br>` [buyHL_m5, sellHL_m5, buyNG_m5, sellNG_m5] = alerts.f_detect_events_raw(false, false, ...)`<br>` condBuyEventM5 := (buyHL_m5 > 0 or buyNG_m5 > 0)` | Gate `IsM5JustClosed`. Lặp `PivotEntries`, tính `buyHL`, `buyNG` theo công thức Event A (M5: `flagPrev∈{1,3} && flagNow==2`) + Event B (`flagPrev != -1 && flagNow == -1`) + Event C (`flagNow==2 && hasKeyBox && KeyStopBar==SourceBarIndex && extPrev=T && extNow=F`, swap dir). Fired = `buyHL > 0 \|\| buyNG > 0`. | `EventFamilyEvaluators.cs:68-152`:<br>`bool eventA = isM15 ? (flagPrev != 2 && flagNow == 2) : ((flagPrev == 1 \|\| flagPrev == 3) && flagNow == 2);`<br>`if (flagPrev != -1 && flagNow == -1) { ... buyNG++ }`<br>`if (flagNow == 2 && piv.HasKeyBox && piv.KeyStopBar == ctx.SourceBarIndex && piv.KeyExtendingPrev && !piv.KeyExtending) { typ==-1 → buyNG++ }`<br>`fired = buyHL > 0 \|\| buyNG > 0` | 6 | 100% |
| TF gate `m5_just_closed`: Pine = `m5_time != prev_m5_time`. Trên M5 chart, fires once mỗi bar close M5. | `pine code arlert new.pine:224`:<br>`bool m5_just_closed = not na(m5_time) and not na(prev_m5_time) and m5_time != prev_m5_time` | C# `IsM5JustClosed = isBarClosed && tfTok == "5"` (chỉ M5 chart). M1/M2 chart sẽ không trigger M5 event (theo design). | `Loop6EvaluationContextFactory.cs:24-28`:<br>`var isM5Tf = tfTok == "5";`<br>`var isM5JustClosed = isJustClosed && isM5Tf;` | 7 | 100% |
| Dedup theo `swingId + "_2"` cho Event A, `swingId + "_-1"` cho Event B (HashSet `triggeredA_Buy_M5`, `triggeredB_Buy_M5`, `triggeredC_Buy_M5`). | `pine code arlert new.pine:5226` (passes các array). `lib_alerts new.pine f_detect_events_raw` (m15Mode=false). | Same dedup keys, same HashSets. | `EventFamilyEvaluators.cs:78-83`, `EventDedupSets.cs` (TriggeredA_Buy_M5, …) | 8 | 100% |

### 3.2. `condSellEventM5`

| Luồng logic Pine | Dẫn chứng code Pine | Luồng logic C# | Dẫn chứng code C# | STT | % Pass |
|---|---|---|---|---|---|
| Mirror của `condBuyEventM5` cho `typ==1` (High pivot) — sellHL từ flag→BROKEN High; sellNG từ flag→MAIN_BROKEN High + Event C swap (typ==1 → SELL). | `pine code arlert new.pine:5229`:<br>`condSellEventM5 := (sellHL_m5 > 0 or sellNG_m5 > 0)` | Same logic, branch `typ == 1 → sellHL++` (Event A), `typ == 1 → sellNG++` (Event B), Event C swap `typ == 1 → sellNG_C++`. | `EventFamilyEvaluators.cs:103-141`:<br>`if (typ == 1) { if (sellHLSet_A.Add(key)) sellHL++; }` … `if (typ == 1) { if (sellNGSet_C.Add(key)) sellNG++; }` | 9 | 100% |

### 3.3. `condBuyEventHLM15`

| Luồng logic Pine | Dẫn chứng code Pine | Luồng logic C# | Dẫn chứng code C# | STT | % Pass |
|---|---|---|---|---|---|
| Gate `currentTF=="15" && m15_just_closed`. Gọi `f_detect_events_raw(m15Mode=true, …)`. Cond = `buyHL_m15 > 0`. **Event A M15** dùng condition mở rộng: `flagPrev != 2 && flagNow == 2` (mọi prior state). | `pine code arlert new.pine:5230-5236`:<br>`if currentTF == "15" and m15_just_closed`<br>` [buyHL_m15, sellHL_m15, buyNG_m15, sellNG_m15] = alerts.f_detect_events_raw(true, false, ...)`<br>` condBuyEventHLM15 := buyHL_m15 > 0` | Gate `IsM15JustClosed`. Trong vòng lặp, branch m15: `eventA = (flagPrev != 2 && flagNow == 2)`. Fired = `buyHL > 0`. | `EventFamilyEvaluators.cs:63-67, 95-97, 147`:<br>`bool isM15 = id is CondBuyEventHLM15 or CondSellEventHLM15 or CondBuyEventNGM15 or CondSellEventNGM15;`<br>`bool eventA = isM15 ? (flagPrev != 2 && flagNow == 2) : ...;`<br>`CondBuyEventHLM15 => buyHL > 0` | 10 | 100% |

### 3.4. `condSellEventHLM15`

| Luồng logic Pine | Dẫn chứng code Pine | Luồng logic C# | Dẫn chứng code C# | STT | % Pass |
|---|---|---|---|---|---|
| Mirror, `typ==1`. Cond = `sellHL_m15 > 0`. | `pine code arlert new.pine:5237`:<br>`condSellEventHLM15 := sellHL_m15 > 0` | Same logic mirrored. | `EventFamilyEvaluators.cs:148`:<br>`CondSellEventHLM15 => sellHL > 0` | 11 | 100% |

### 3.5. `condBuyEventNGM15`

| Luồng logic Pine | Dẫn chứng code Pine | Luồng logic C# | Dẫn chứng code C# | STT | % Pass |
|---|---|---|---|---|---|
| Gate `currentTF=="15" && m15_just_closed`. NG = Event B (MAIN_BROKEN) + Event C (KEY_STOP). Direction swap Event C: `typ==-1 → BUY`. | `pine code arlert new.pine:5238`:<br>`condBuyEventNGM15 := buyNG_m15 > 0` | Same: Event B `flagPrev != -1 && flagNow == -1, typ==1→buyNG++`; Event C `typ==-1 → buyNG_C++`. | `EventFamilyEvaluators.cs:113-140`:<br>`if (flagPrev != -1 && flagNow == -1) { if (typ == 1) { ... buyNG++ } }`<br>`if (flagNow == 2 && piv.HasKeyBox && piv.KeyStopBar == ctx.SourceBarIndex && piv.KeyExtendingPrev && !piv.KeyExtending) { if (typ == -1) buyNG++ }` | 12 | 100% |

### 3.6. `condSellEventNGM15`

| Luồng logic Pine | Dẫn chứng code Pine | Luồng logic C# | Dẫn chứng code C# | STT | % Pass |
|---|---|---|---|---|---|
| Mirror: Event B `typ==-1 → sellNG`; Event C `typ==1 → sellNG_C`. | `pine code arlert new.pine:5239`:<br>`condSellEventNGM15 := sellNG_m15 > 0` | Same. | `EventFamilyEvaluators.cs:113-140, 150`:<br>`if (typ == -1) { if (sellNGSet_B.Add(key)) sellNG++; }` … `if (typ == 1) { if (sellNGSet_C.Add(key)) sellNG++; }`<br>`CondSellEventNGM15 => sellNG > 0` | 13 | 100% |

---

## 4. TOUCH / REAL family (mọi tick, không gate bar close)

### 4.1. `canBuyTouchM5`

| Luồng logic Pine | Dẫn chứng code Pine | Luồng logic C# | Dẫn chứng code C# | STT | % Pass |
|---|---|---|---|---|---|
| 1. Collect zones bằng `f_collect_active_zones(...)`. 2. `_tTouch = f_check_touch_zones(close[0], close[0], …)`. 3. `canBuyTouchM5 = _tTouch == 1 \|\| _tTouch == 0`. | `pine code arlert new.pine:5276-5278`:<br>`[gKL_t5, …] = alerts.f_collect_active_zones(pivHasKey, pivKeyBox, pivKeyExtending, pivType, pivFlag, pivMainRole, obBoxes, obStates, obExtending, obTypes)`<br>`int _tTouch = alerts.f_check_touch_zones(close[0], close[0], …)`<br>`bool canBuyTouchM5 = (_tTouch == 1 or _tTouch == 0)` | `BuildZoneState(barClose)` (gọi `ZoneCollector.Collect`) → compute `ZoneTouchResult` for `close==close==barClose` (single price). Evaluator: `fired = t == 1 \|\| t == 0`. | `Loop6EvaluationContextFactory.cs:19`:<br>`var zoneState = shell.State.BuildZoneState(barClose);`<br>`PineStateEngine.cs:837-838`:<br>`public ZoneState BuildZoneState(double barClose) => ZoneCollector.Collect(ObPool, Pivots, barClose);`<br>`TouchRealEvaluators.cs:43-44`:<br>`int t = ctx.ZoneState.ZoneTouchResult;`<br>`bool fired = t == 1 \|\| t == 0;` | 14 | 100% (đã fix 22/05/2026: bổ sung filter `pivHasKey`, `pivKeyExtending`, `mainRole`, classification BROKEN — trước đây skip flag∈{2,-1,6,-3,5}, gây tTouch=0 → bug "cả buy+sell touch fire đồng thời") |

### 4.2. `canSellTouchM5`

| Luồng logic Pine | Dẫn chứng code Pine | Luồng logic C# | Dẫn chứng code C# | STT | % Pass |
|---|---|---|---|---|---|
| `canSellTouchM5 = _tTouch == -1 \|\| _tTouch == 0` (cùng nguồn `_tTouch`). | `pine code arlert new.pine:5279`:<br>`bool canSellTouchM5 = (_tTouch == -1 or _tTouch == 0)` | `fired = t == -1 \|\| t == 0`. | `TouchRealEvaluators.cs:70-71`:<br>`int t = ctx.ZoneState.ZoneTouchResult;`<br>`bool fired = t == -1 \|\| t == 0;` | 15 | 100% (sau fix ZoneCollector — xem STT 14) |

### 4.3. `canBuyReal`

| Luồng logic Pine | Dẫn chứng code Pine | Luồng logic C# | Dẫn chứng code C# | STT | % Pass |
|---|---|---|---|---|---|
| Khởi `canBuyReal=false`. Nếu `lastPushedSwingType == -1` (LOW pushed → looking BUY): `_activeBot = realBuyBot, _activeTop = realBuyTop`. Nếu `realBuyBot != na && low[0] < realBuyBot`: mở rộng `effBuyBreakBot = min(effBuyBreakBot, low[0])` → `_activeBot = min(effBuyBreakBot, realBuyBot)`. | `pine code arlert new.pine:5283-5294`:<br>`if lastPushedSwingType == -1`<br>` _activeBot := realBuyBot`<br>` _activeTop := realBuyTop`<br>` if not na(realBuyBot) and low[0] < realBuyBot`<br>` effBuyBreakBot := math.min(nz(effBuyBreakBot, low[0]), low[0])`<br>` if not na(effBuyBreakBot) and not na(realBuyBot) and not na(realBuyTop)`<br>` _activeBot := math.min(effBuyBreakBot, realBuyBot)`<br>` _activeTop := realBuyTop` | `ComputeRealFlags`: nếu `LastPushedSwingType == -1`, return early `(false,false)` nếu missing `RealBuyBot/Top`; nếu không, `activeTop = RealBuyTop`, `activeBot = EffBuyBreakBot.HasValue ? Math.Min(EffBuyBreakBot, RealBuyBot) : RealBuyBot`. `effBuyBreakBot` được expand intra-bar trong `RealZoneEngine.OnRealtimeBarPrice`. | `TouchRealEvaluators.cs:98-105`:<br>`if (rf.LastPushedSwingType == -1) { if (rf.RealBuyBot == null \|\| rf.RealBuyTop == null) return (false, false); activeTop = rf.RealBuyTop; activeBot = rf.EffBuyBreakBot.HasValue ? Math.Min(rf.EffBuyBreakBot.Value, rf.RealBuyBot.Value) : rf.RealBuyBot; }`<br>`RealZoneEngine.cs OnRealtimeBarPrice(barHigh, barLow)`: `if (RealBuyBot.HasValue && barLow < RealBuyBot.Value) EffBuyBreakBot = Math.Min(EffBuyBreakBot ?? barLow, barLow);` | 16 | 100% |
| `_tActive = f_check_touch_zones(_activeBot, _activeTop, ...)`. `canBuyReal = _tActive == 1 \|\| _tActive == 0`. | `pine code arlert new.pine:5303-5305`:<br>`int _tActive = alerts.f_check_touch_zones(_activeBot, _activeTop, ...)`<br>`canBuyReal := (_tActive == 1 or _tActive == 0)` | `tActive = CheckTouchZones(activeBot, activeTop, rf.Zones)`. `canBuy = tActive is 1 or 0`. Fail-closed: nếu `rf.Zones == null` → `tActive = -2` (block cả buy+sell). | `TouchRealEvaluators.cs:125-130`:<br>`int tActive = rf.Zones != null ? CheckTouchZones(activeBot.Value, activeTop.Value, rf.Zones) : -2;`<br>`bool canBuy = tActive is 1 or 0;` | 17 | 100% |

### 4.4. `canSellReal`

| Luồng logic Pine | Dẫn chứng code Pine | Luồng logic C# | Dẫn chứng code C# | STT | % Pass |
|---|---|---|---|---|---|
| Nếu `lastPushedSwingType == 1` (HIGH pushed → looking SELL): `_activeBot = realSellBot, _activeTop = realSellTop`. Expand `effSellBreakTop = max(effSellBreakTop, high[0])` khi `high[0] > realSellTop`. `_activeTop = max(effSellBreakTop, realSellTop)`. | `pine code arlert new.pine:5295-5302`:<br>`else if lastPushedSwingType == 1`<br>` _activeBot := realSellBot`<br>` _activeTop := realSellTop`<br>` if not na(realSellTop) and high[0] > realSellTop`<br>` effSellBreakTop := math.max(nz(effSellBreakTop, high[0]), high[0])`<br>` if not na(effSellBreakTop) and not na(realSellBot) and not na(realSellTop)`<br>` _activeBot := realSellBot`<br>` _activeTop := math.max(effSellBreakTop, realSellTop)` | Mirror: `activeBot = RealSellBot; activeTop = EffSellBreakTop.HasValue ? Math.Max(EffSellBreakTop, RealSellTop) : RealSellTop`. | `TouchRealEvaluators.cs:106-113`:<br>`else if (rf.LastPushedSwingType == 1) { if (rf.RealSellBot == null \|\| rf.RealSellTop == null) return (false, false); activeBot = rf.RealSellBot; activeTop = rf.EffSellBreakTop.HasValue ? Math.Max(rf.EffSellBreakTop.Value, rf.RealSellTop.Value) : rf.RealSellTop; }` | 18 | 100% |
| `canSellReal = _tActive == -1 \|\| _tActive == 0`. | `pine code arlert new.pine:5306`:<br>`canSellReal := (_tActive == -1 or _tActive == 0)` | `canSell = tActive is -1 or 0`. | `TouchRealEvaluators.cs:129`:<br>`bool canSell = tActive is -1 or 0;` | 19 | 100% |
| Nếu `lastPushedSwingType == 0` (init), không vào nhánh nào → `canBuyReal=false, canSellReal=false`. | Pine: nhánh `if/else if` không hit → giữ `false`. | C# `else { return (false, false); }`. | `TouchRealEvaluators.cs:114-117`:<br>`else { return (false, false); }` | 20 | 100% |

### 4.5. `CanBuyRealAndM15CloseNow`

| Luồng logic Pine | Dẫn chứng code Pine | Luồng logic C# | Dẫn chứng code C# | STT | % Pass |
|---|---|---|---|---|---|
| `canBuyReal AND m15CloseNow`. `m15CloseNow = ta.change(time("15")) != 0 and barstate.isnew` — fires once tại open của M15 boundary. | `pine code arlert new.pine:5331, 5334`:<br>`bool m15CloseNow = ta.change(time("15")) != 0 and barstate.isnew`<br>`alertcondition(canBuyReal and m15CloseNow, title="CAN BUY REAL @ M15 CLOSE", message="CAN BUY REAL @ M15 CLOSE {{interval}}")` | Evaluator: gate `M15CloseEdgeInjected` trước; sau đó `canBuy = ComputeRealFlags(ctx).canBuy`. Host inject edge khi M15 boundary đóng (realtime: edge-only; backtest: per closed bar). | `TouchRealEvaluators.cs:199-231` (`EvaluateRealBuyAndM15Close`):<br>`if (!ctx.M15CloseEdgeInjected) return NotFired(CARRY_OVER_MISSING_M15_EDGE);`<br>`var (canBuy, _) = ComputeRealFlags(ctx);`<br>`return canBuy ? Fired : NotFired;`<br>`Loop6EvaluationContextFactory.cs:36-46` (edge gate logic) | 21 | 95% — TradingView `barstate.isnew` chỉ fires ở tick đầu của bar mới M15; cTrader thì host inject một lần khi M15 boundary mới open. Hành vi tương đương cho realtime; backtest fires per closed bar — khớp với chế độ backtest TradingView. |

### 4.6. `CanSellRealAndM15CloseNow`

| Luồng logic Pine | Dẫn chứng code Pine | Luồng logic C# | Dẫn chứng code C# | STT | % Pass |
|---|---|---|---|---|---|
| `canSellReal AND m15CloseNow`. | `pine code arlert new.pine:5335`:<br>`alertcondition(canSellReal and m15CloseNow, title="CAN SELL REAL @ M15 CLOSE", message="CAN SELL REAL @ M15 CLOSE {{interval}}")` | Mirror của 4.5. | `TouchRealEvaluators.cs:233-264` (`EvaluateRealSellAndM15Close`):<br>`var (_, canSell) = ComputeRealFlags(ctx);`<br>`return canSell ? Fired : NotFired;` | 22 | 95% — cùng caveat với STT 21. |

---

## 5. PHA KHUNG LON (PKL) — `condPhaKhungLon`

### 5.1. `condPhaKhungLon`

| Luồng logic Pine | Dẫn chứng code Pine | Luồng logic C# | Dẫn chứng code C# | STT | % Pass |
|---|---|---|---|---|---|
| Mọi TF, fires khi `isConfirmed`. Duyệt mọi pivot. **Event A**: `flagNow == 2` → dedup `swingId + "_2"` → fire. **Event B**: `flagNow == -1` → dedup `swingId + "_-1"`. **Event C**: `flagNow == 2 && !extNow && keyStopBar == bar_index && !na(kb)` → dedup `swingId + "_C" + keyStopBar`. Không tách buy/sell, không tách M5/M15. | `pine code alert debug.pine:5300-5326`:<br>`condPhaKhungLon := false`<br>`if isConfirmed`<br>` for i = 0 to sz_pkl - 1`<br>` if flagNow_pkl == 2`<br>` if not alerts.f_check_already_triggered(triggeredA_PKL, swingKey)`<br>` condPhaKhungLon := true; array.push(...)`<br>` if flagNow_pkl == -1 ...`<br>` if flagNow_pkl == 2 && !extNow_pkl && keyStopBar_pkl == bar_index && not na(kb_pkl) ...` | Gate `IsBarClosed`. Duyệt `PivotEntries`. Cùng 3 nhánh dedup, cùng key format. | `EventFamilyEvaluators.cs:189-253`:<br>`if (!ctx.IsBarClosed) return NotFired(PhaKhungLonNoBarClose);`<br>`if (flagNow == 2) { if (dedup.TriggeredA_PKL.Add(sid + "_2")) aHits++; }`<br>`if (flagNow == -1) { if (dedup.TriggeredB_PKL.Add(sid + "_-1")) bHits++; }`<br>`if (flagNow == 2 && piv.HasKeyBox && !piv.KeyExtending && piv.KeyStopBar == ctx.SourceBarIndex) { ... cHits++ }` | 23 | 100% |
| Lưu ý quan trọng: PKL Event C **KHÔNG** yêu cầu `extPrev=true` (khác với Event C trong `f_detect_events_raw` của EVENT M5/M15 — phải transition từ true→false). PKL chỉ check `!extNow && keyStopBar == bar_index`. Dedup theo `swingId + "_C" + keyStopBar` đủ để chống refire. | Pine line 5269 chỉ check `not extNow_pkl and not na(keyStopBar_pkl) and keyStopBar_pkl == bar_index and not na(kb_pkl)` (KHÔNG có `extPrev_pkl == true`). | Mirror: C# chỉ check `!piv.KeyExtending && piv.KeyStopBar == ctx.SourceBarIndex && piv.HasKeyBox` — không yêu cầu `KeyExtendingPrev`. | `EventFamilyEvaluators.cs:230-234`:<br>`if (flagNow == 2 && piv.HasKeyBox && !piv.KeyExtending && piv.KeyStopBar == ctx.SourceBarIndex) { var key = sid + "_C" + ctx.SourceBarIndex.ToString(); if (dedup.TriggeredC_PKL.Add(key)) cHits++; }` | 24 | 100% |

---

## 6. Kết luận parity

### 6.1. Bảng tổng kết

| # | Condition / hàm                  | % Pass | Ghi chú |
|---|----------------------------------|--------|---------|
| 1-5  | `f_collect_active_zones` (4 nhánh) + `f_check_touch_zones` | 100% | Fix 22/05/2026 — bổ sung filter `pivHasKey/pivKeyExtending/mainRole` + broken classification. |
| 6-13 | EVENT M5/M15 (6 conditions × A/B/C path) | 100% | Đã kiểm chứng dedup, TF gate, direction swap Event C. |
| 14-15 | TOUCH M5 buy/sell                | 100% | Phụ thuộc fix ZoneCollector — đã khớp. |
| 16-20 | REAL buy/sell + edge expansion  | 100% | `OnRealtimeBarPrice` đã cung cấp expansion intra-bar. |
| 21-22 | REAL @ M15 close composite       | 95%  | Realtime tương đương; backtest fires per closed bar (cùng hành vi TradingView replay). |
| 23-24 | PhaKhungLon (A/B/C, 3 dedup set) | 100% | Event C không yêu cầu transition `extPrev`, chỉ dedup theo `swingId+"_C"+keyStopBar`. |

### 6.2. Các fix đã thực hiện trong đợt rà soát này

1. **`ZoneCollector.Collect`** (file `src/TradeAlert.Core/Engines/ZoneCollector.cs`)
   - Bổ sung skip `!pivHasKey[i]` (Pine yêu cầu).
   - Bổ sung skip `!pivKeyExtending[i]` (Pine yêu cầu).
   - Phân loại NON-broken khớp Pine: dùng `flag ∈ {1,3,4,7,5,50}` HOẶC `(flag==0 && mainRole==1)`.
   - Bổ sung phân loại BROKEN (Pine line ~116-120): `flag ∈ {-3,2,-2,6}` — broken HIGH → green, broken LOW → red.
   - Skip OB khi `!Extending` (Pine yêu cầu).
2. **`IPivotKeyBoxView`** mở rộng: thêm `GetHasKey`, `GetKeyExtending`, `GetMainRole`.
3. **`PivotStateStore`** implement 3 method mới.

### 6.3. Tác động dự kiến

- Bug **`CanBuyTouchM5` + `CanSellTouchM5` cùng fires** (do `tTouch==0` vì ZoneCollector skip mọi zone broken): sẽ KHẮC PHỤC.
- Khi giá nằm trong vùng green key (cả non-broken extending hoặc broken HIGH extending): `tTouch=1` → `canBuyTouchM5=T, canSellTouchM5=F` (chỉ buy fires).
- Khi giá nằm trong vùng red key: `tTouch=-1` → `canBuyTouchM5=F, canSellTouchM5=T`.
- Khi không zone nào extending phủ giá: `tTouch=0` → cả hai fire (Pine cũng vậy — đây là hành vi đúng theo Pine khi không có zone chặn).

### 6.4. Caveat / hạn chế còn lại

- TouchM5 trên cTrader fire **mỗi tick** (do gọi `Calculate` mỗi tick). TradingView `alertcondition` chỉ fire mỗi bar khi cond chuyển true. → Khắc phục bằng layer dedup ở `AlertPipelineHost` (đã có).
- TF gate M5: cTrader chỉ trigger EVENT M5 khi chart timeframe là M5 thực — không trigger trên M1/M2 chart (matches Pine `currentTF == "5"`).
- Backtest cBot: `M15CloseEdgeInjected` cho M5 chart cần `barOpenTimeChartLocal.Minute % 15 == 0` — đã chính xác.

---

## 7. Compound alert — preset R1–R6 & sync TradingView

### 7.1. Preset BUY / SELL (Pine name trong rule string)

| Slot | Tên hiển thị | Hướng | Rule string (Pine names) |
|------|--------------|-------|--------------------------|
| R1 | SELL M5 | **SELL** | `M5:condSellEventM5 + M5:canSellReal + M15:canSellReal + H1:canSellTouchM5 + H4:canSellTouchM5` |
| R2 | BUY M5 | **BUY** | `M5:condBuyEventM5 + M5:canBuyReal + M15:canBuyReal + H1:canBuyTouchM5 + H4:canBuyTouchM5` |
| R3 | BUY HL M15 | **BUY** | `M15:condBuyEventHLM15 + M5:canBuyRealAndM15CloseNow + M15:canBuyReal + H1:canBuyTouchM5 + H4:canBuyTouchM5` |
| R4 | SELL HL M15 | **SELL** | `M15:condSellEventHLM15 + M5:canSellRealAndM15CloseNow + M15:canSellReal + H1:canSellTouchM5 + H4:canSellTouchM5` |
| R5 | BUY NG M15 | **BUY** | `M15:condBuyEventNGM15 + M5:canBuyRealAndM15CloseNow + M15:canBuyReal + H1:canBuyReal + H4:canBuyTouchM5` |
| R6 | SELL NG M15 | **SELL** | `M15:condSellEventNGM15 + M5:canSellRealAndM15CloseNow + M15:canSellReal + H1:canSellReal + H4:canSellTouchM5` |

Nguồn C#: `CompoundAlertPresets.cs`. Parse: `CompoundRuleParser.TryParse` — format `TF:pineConditionName+...`.

**Min TF cadence (TradingView sync):** R1/R2 → **M5**; R3–R6 → **M15** (do có `cond*Event*M15` hoặc `can*RealAndM15CloseNow` trên M5 với edge M15).

### 7.2. Compound sync mode — cùng thời điểm (TradingView multi-condition)

| Mode | Parameter C# | EVENT `[OK]` / FIRE | STATE (Touch/Real) |
|------|--------------|---------------------|---------------------|
| **TradingView** (default) | `CompoundSyncMode = TradingView` | Chỉ bar EVENT vừa fire: `LastFiredBarIndex == currentBarIndex` | True ngay lúc eval |
| **PineEventWindow** (legacy) | `CompoundSyncMode = PineEventWindow` | Còn `[OK]` trong `EVENT valid window` bars sau fire | True ngay lúc eval |

| Thành phần | File | Vai trò |
|------------|------|---------|
| `CompoundEvalSync.IsEvalSyncPoint` | `CompoundEvalSync.cs:75-100` | FIRE chỉ tại cadence TF nhỏ nhất trong rule |
| `CompoundEvalSync.IsEventActiveOnCurrentBar` | `CompoundEvalSync.cs:102-118` | TradingView = same bar; legacy = window |
| `MtfEngineManager.IsConditionActive` | `MtfEngineManager.cs:211-232` | Panel + AND evaluation |
| `TradeAlertLoop6Host.MayFireCompound` | `TradeAlertLoop6Host.cs` | Gate sync point trước khi FIRE |

**Ý nghĩa thực tế:** Với **TradingView**, `condBuyEventM5` và `canBuyReal` phải cùng true **tại cùng một điểm eval M5** — EVENT không “nhớ” 3 bar như legacy. Đây là lý do panel trước đây có thể thấy M5 EVENT `[OK]` trong khi M15 REAL chưa sync.

---

## 8. Lịch chạy alert — mỗi tick vs bar close (Pine ↔ C#)

### 8.1. Bảng timing tổng hợp

| Nhóm | Pine name / `AlertConditionId` | Pine chạy khi | C# `AlertTimingClass` | Realtime (mỗi tick bar[0]) | Bar close / cạnh nến mới |
|------|--------------------------------|---------------|------------------------|----------------------------|---------------------------|
| EVENT M5 | `condBuyEventM5`, `condSellEventM5` | `currentTF=="5" && m5_just_closed` | `BarCloseM5Event` | **Không** | **Có** — M5 chart, nến vừa đóng (backtest `isBarClosed`; realtime `isNew` → eval bar index−1) |
| EVENT M15 | `condBuyEventHLM15`, … NG M15 | `currentTF=="15" && m15_just_closed` | `BarCloseM15Event` | **Không** | **Có** — M15 chart hoặc HTF M15 engine khi M15 bar đóng |
| TOUCH M5 | `canBuyTouchM5`, `canSellTouchM5` | mọi lần script chạy, `close[0]` | `RealtimeTouchBar0` | **Có** | Có (refresh state lúc đóng nến backtest) |
| REAL | `canBuyReal`, `canSellReal` | mọi tick, `high[0]`/`low[0]` | `RealtimeFilterBar0` | **Có** | Có (backtest) |
| REAL @ M15 | `canBuyRealAndM15CloseNow`, … | `canBuyReal && m15CloseNow` | `RealtimeFilterBar0WithM15CloseEdge` | **Có** nhưng chỉ fire khi **M15 edge** (`isNew` @ boundary hoặc host inject) | Có @ M15 boundary bar close |
| PKL | `condPhaKhungLon` | `isConfirmed` mọi TF | `BarCloseAnyTfPhaKhungLon` | **Không** | **Có** |

### 8.2. Luồng host C# (sau fix 22/05/2026)

| Đường | Pass A — STATE (Touch/Real) | Pass B — bar close (EVENT/PKL) |
|-------|----------------------------|-------------------------------|
| **Single alert** (`EvaluateAllAlerts`) | Realtime: mỗi tick nến cuối (`MayEvaluateRealtimeAlerts`) | Backtest: mỗi nến đóng; Realtime: tick `isNew` → eval bar **index−1** |
| **Compound** (`MtfEngineManager.UpdateConditionCache`) | Realtime forming bar + HTF `UpdateRealtimePriceFromChart` | Backtest / HTF `AdvanceHtfEngines` + realtime `isNew` → bar index−1; **FIRE** gated bởi `CompoundEvalSync.IsEvalSyncPoint` (cadence min TF) |
| **Dedup spam Touch** | `DuplicateKey` theo bar — 1 lần/nến nếu bật `Print single alert conditions` | — |

### 8.3. File triển khai

| File | Vai trò |
|------|---------|
| `AlertBarTiming.cs` | Gate tập trung: `IsM5JustClosedGate`, `IsM15JustClosedGate`, `ComputeM15CloseEdge`, `ShouldEvaluateOnPass` |
| `Loop6EvaluationContextFactory.cs` | Build context chart TF |
| `MtfEngineManager.cs` | Tách pass STATE vs EVENT cho compound MTF |
| `TradeAlertLoop6Host.cs` | Single alert + compound + truyền `isNew` / M15 edge chart → HTF |

### 8.4. Ghi chú parity

- Pine `m5_just_closed` / `m15_just_closed` = **cạnh nến mới** (`barstate.isnew`), không phải mọi tick giữa nến → C# map qua `isNewBarOnRealtimeForming` + eval bar đóng.
- H1/H4 **không** được coi là M15 event close (đã bỏ `IsHigherThanM15` khỏi gate M15).
- `canBuyRealAndM15CloseNow` trên HTF M5 nhận M15 edge từ chart qua `chartM15CloseEdgeInjected`.

---

## 9. Tham chiếu file

| File | Mục đích |
|---|---|
| `NGUỒN DỰ ÁN BAN ĐẦU/PINE_CODE/pine code arlert new.pine:5210-5335` | Pine canonical alert layer (12 alertconditions). |
| `NGUỒN DỰ ÁN BAN ĐẦU/PINE_CODE/pine code alert debug.pine:5268-5332` | Pine debug layer + `condPhaKhungLon` (canonical PKL). |
| `src/TradeAlert.Core/Engines/AlertEvaluators/EventFamilyEvaluators.cs` | EVENT M5/M15 + PhaKhungLon port. |
| `src/TradeAlert.Core/Engines/AlertEvaluators/TouchRealEvaluators.cs` | TOUCH M5 + REAL + M15 composite port. |
| `src/TradeAlert.Core/Engines/ZoneCollector.cs` | `f_collect_active_zones` + `f_check_touch_zones` (single price) port. |
| `src/TradeAlert.Core/Engines/RealZoneEngine.cs` | `realBuyBot/Top, realSellBot/Top, effBuyBreakBot, effSellBreakTop`. |
| `AlertBarTiming.cs` | Lịch tick vs bar-close; M5/M15 edge gates. |
| `MtfEngineManager.cs` | Compound MTF — pass STATE mỗi tick, EVENT khi bar đóng. |
| `CompoundEvalSync.cs` / `CompoundEvalSyncMode.cs` | TradingView same-moment AND + min-TF cadence. |
| `CompoundAlertPresets.cs` | Preset R1–R6 buy/sell rule strings. |
| `src/TradeAlert.Core/Models/Alerts/EventDedupSets.cs` | Triggered* HashSets cho dedup theo swingId. |
| `src/TradeAlert.Core/Engines/AlertEngine.cs` | Registry 13 definitions + dispatch. |
