# Loop6 — Gap logic Pine ↔ C# (Hồi thật / Phá thật / Microswing / OB)

Tham chiếu:
- Pine: `PINE_CODE/pine code arlert new.pine` (+ `lib_micro_swing.pine`)
- C#: `src/TradeAlert.Core/Engines/*.cs`

Trạng thái chung: **port đầy đủ rules, có vài gap về dữ liệu đầu vào (raw vs eff, wick-cleaned vs raw) và thông số config chưa wire**.

---

## 1) Hồi thật — `PullbackFilterEngine` (A1/A2/A3/A4)

Pine lines 413–901 → `PullbackFilterEngine.cs` + `PineStateEngine` `BuildKeyLevelContext` (lines 160–266).

### Đã khớp
- Step 1 push pending A (`PushStep1`).
- Step 1.5 microswing rescue (`MicroSwingEngine.Detect`) chen vào trước Step 2.
- Step 2 evaluate A1/A2/A3/A4 với eff* (gap merge), 1/3 và 2/3 body thresholds, B body monotonic check.
- Step 3 extreme-pick + alternation gate (clear cả 2 queue khi confirm).
- Step 4 expire `Bx`/`CDx` + FIFO cap.
- Bypass mode (`useNewPullbackFilter=false`) đúng Pine lines 522–535.

### Gap còn lại

| # | Mục | Pine | C# | Ảnh hưởng |
|---|-----|------|-----|-----------|
| **1.1** | A3 guard wick neutralize | Pine A3 doji-force kiểm `bIsWickBar_H/L = useWickNoiseFilter and not na(neutralizedHighPrev[1])` để **không** confirm nếu wick B đã bị neutralize (line 662–663 và 734–735) | C# `EvaluateHighCandidates` không gọi neutralized check trước A4 (chỉ test `IsDojiSellForce`) | **A4 ở C# vẫn confirm khi Pine sẽ block** → có thể tạo extra HIGH/LOW false positive trong vùng nhiễu. |
| **1.2** | Pine `f_has_clear_body_structure_at(1)` dùng RAW bar tại lag 1 cho A3 `cClear` | Pine line 411: `keylib.has_clear_body_raw(lag, …)` | C# truyền `effClearBody` (merged eff*) cho A3 `cClear`. | Khi gap merge active, C# coi nến XY là "clear" trong khi Pine kiểm chỉ Y → C# có thể accept A3 mà Pine reject. |
| **1.3** | A4 doji `B = lag 2` | Pine truyền raw `open[2]/high[2]/low[2]/close[2]` | C# truyền `ohlcAtLag(2)` raw — **đúng**. | Không gap. |

---

## 2) Phá thật — `PivotTransitionEngine` (R1 / R1' M5 / R2 / R3)

Pine `process_break` lines 3312–3607 → `PivotTransitionEngine.Tick`.

### Đã khớp
- Step A first-break latch: bColorMatch + bodyCrossB/gapOverB + bBody>0 → flag 3/7/50.
- Step B R1 (k=1, tier≥2/3 non-M5), R1' (k=1 M5 không yêu cầu tier), R2 (medium 1/3≤tier<2/3 stage tại k=1, confirm k=2), R3 (mọi tier, k∈[1..breakR3MaxK], cColorMatch + clear + eBeyondB).
- R3 streak die khi `closeViolate`.
- Rollback khi `closeViolate` hoặc `k ≥ windowMax = max(2, breakR3MaxK)`.
- ROOT D không cho break (`isRootD`).
- Confirm branches: MAIN→MAIN_BROKEN (-1) + cleanup ROOT B key + FIX 2.3 NoPromoteC; MAIN_FAKE→-3 + stop-extend keybox; ACTIVE→2 + ROOT B → firstDIdx=Waiting.

### Gap còn lại

| # | Mục | Pine | C# | Ảnh hưởng |
|---|-----|------|-----|-----------|
| **2.1** | `highUsed`/`lowUsed` | Pine line 354–355: `highUsed = cleanHigh1Raw`, `lowUsed = cleanLow1Raw` (wick-neutralized) | C# `PineStateEngine` line 199–204 truyền `ohlc1.High`/`ohlc1.Low` **raw**, không qua `WickNeutralizeEngine` | `pivBreakBHigh`/`BLow` lệch → **R3 `eBeyondB`** so sánh sai khi B là nến râu nhiễu (râu bị neutralize ở Pine nhưng giữ ở C#). Có thể confirm R3 sớm hơn Pine. |
| **2.2** | `cClear` ở STEP B (line 3431) | Pine `f_has_clear_body_structure_at(1) and not f_bar_structure_is_doji_at(1)` (RAW lag 1) | C# truyền `effClearBody && !effIsDoji` (MERGED eff*) | Khi gap merge, body XY > avg dù Y một mình là doji → C# pass R3 mà Pine reject. |
| **2.3** | `cClear` ở STEP A (line 3359) | Cùng: RAW `f_has_clear_body_structure_at(1)` | C# truyền `effClearBody`/`effIsDoji` merged | Tương tự 2.2 — gap merge có thể trigger first-break latch sớm. |
| **2.4** | M5 detection | Pine `timeframe.period == "5"` | C# `IsM5Mode = tfTok == "5"` từ chart timeframe — **đúng**. | Không gap. |

---

## 3) Microswing rescue — `MicroSwingEngine.Detect`

Pine `lib_micro_swing.f_micro_swing_detect` (62–169) → `MicroSwingEngine.cs`.

### Đã khớp đầy đủ
- Trigger `nTyp == lastPushedSwingType && (aBar - nBar) >= 3`.
- Lvl = `box.get_bottom(kb)` (HIGH N) / `box.get_top(kb)` (LOW N), fallback `nPr` khi không có keybox.
- Scan từ off=1..maxOff lấy cực trị `cleanLowSeries`/`cleanHighSeries`.
- Side-effect clear pending queue trong `(nBar, aBar)` exclusive.
- Doji-Force helpers (`IsDojiSellForce`/`IsDojiBuyForce`) chính xác công thức.

### Gap còn lại
**Không có gap logic.** Chỉ khác:
- Pine có debug label vẽ tại M (`showDebug`); C# bỏ.
- C# trả `MicroSwingResult` cho caller emit pivot, không tự push.

---

## 4) OB — pivot OB (source=0) + structural (source=1) + LTF confirm

| Thành phần Pine | C# |
|------------------|-----|
| `f_add_OB` (1925–2057) initial scan | `OBEngine.ComputeInitialObState` + `ObPoolMaintenance.PushOb` |
| Visibility loop (4659–4769) per-bar | `OBEngine.Tick` + host loop trong `PineStateEngine` (lines 427–489) |
| `f_check_ob_ltf_confirm_full_range` (1790–1871) | `ObLtfConfirmEngine.CheckFullRange` |
| Confirm tại `checkBar` (4682–4746) | `ObLtfConfirmEngine.ConfirmCheckBar` + `ApplyOutcome` |
| `f_find_and_draw_OB` pivot (2074–2158) | `ObPivotFinderEngine.FindAndDraw` |
| `f_find_and_draw_OB_structural` (2184–2262) | `ObStructuralEngine.FindAndDrawStructural` |
| `f_trim_OBs` (2267–2396) | `OBEngine.ComputeTrimIndices` |
| `f_limit_overlapping_ob_boxes` | `ObOverlapEngine` |
| `f_cancel_pending_OBs_same_dir` (1893–1920) | **❌ chưa port** |

### Gap còn lại

| # | Mục | Pine | C# | Ảnh hưởng |
|---|-----|------|-----|-----------|
| **4.1** | `f_cancel_pending_OBs_same_dir` | Khi OB mới push vào state=0 (chờ fullPierce), Pine xóa toàn bộ OB state-0 cùng `typ` + `source` của pivot khác (line 2012–2013) | C# `ObPoolMaintenance.PushOb` không gọi cancel | **OB pending tích lũy** từ pivot cũ, không bị xóa khi pivot mới sinh OB → pool nhiều noise. |
| **4.2** | Structural OB `obScanBars` | Pine `f_add_structure_OB` → `f_add_OB` → dùng global `obScanBars` (mặc định 50) | C# `ObStructuralEngine.FindAndDrawStructural` truyền `obScanBars: barD - barC + 5` (theo khoảng CD) | Initial scan range hẹp hơn Pine → **OB structural có thể giữ state=0** quá lâu, không transition 2/3 ngay khi created. Visibility loop sau đó vẫn xử lý nhưng có 1-2 bar trễ. |
| **4.3** | `obAtrMultiplier` input | Pine khai báo (line 39) nhưng **không sử dụng ở chỗ nào trong file** | C# đã có param UI (`ObAtrMultiplier`) — không wire vào engine | Không gap — Pine cũng không dùng, hiện chỉ là placeholder cho Auto LTF/ATR fallback (chưa implement cả 2 phía). |
| **4.4** | Auto LTF/ATR fallback | Pine line 4787: chỉ chạy nhánh LTF (`useOBLtfMode=true and hasLtfData`); nếu không có LTF → comment ATR mode nhưng **không có implementation** | C# `RunObLtfConfirm` chỉ chạy LTF mode tương đương; ATR fallback chưa cần | Không gap. |
| **4.5** | Trim buffer ngưỡng | Pine `currentOBSize > maxOBs + 10` (buffer 10) | C# trim ngay khi `> maxOBs` (không buffer) | **C# trim aggressive hơn 10 OB** → có thể xóa OB hợp lệ thường xuyên hơn. |
| **4.6** | `f_check_key_*_B_break` ngưỡng | Pine line 4897: `strong = math.abs(close - open) >= atr14 * 0.2` (atr14 = ta.atr(14)) | C# `KeyLevelDrawEngine.KeyBreakAtrMult = 0.2` + dùng `ctx.AtrValue` (= `_atrWilder` len `KeylevelAtrLen` = 14 mặc định) | OK khi `KeylevelAtrLen = 14` (default). Nếu user đổi `KeylevelAtrLen` → C# dùng ATR khác Pine. **Nên hardcode `atr14` riêng** cho key-break check. |

---

## 5) Tóm tắt ưu tiên fix

| Mức | Mục | File C# cần sửa |
|-----|-----|-----------------|
| 🔴 Cao | **2.1** Wick-clean cho `highUsed/lowUsed` truyền vào break Tick | `PineStateEngine.cs` (~199): truyền `bar1Clean.CleanHigh/Low` |
| 🔴 Cao | **2.2/2.3** `cClear`/`cIsDoji` cho break engine dùng raw lag 1, không eff merged | Thêm 2 cờ raw cạnh effClear/effDoji, truyền cho `Transitions.Tick` |
| 🔴 Cao | **4.1** `f_cancel_pending_OBs_same_dir` | `ObPoolMaintenance.PushOb` thêm cancel khi state=0 |
| 🟡 Trung | **1.1** A3/A4 guard `neutralizedHighPrev[1]` | `PullbackFilterEngine.EvaluateHighCandidates` (+ Low) thêm param `bIsWickBar` |
| 🟡 Trung | **4.2** Structural OB `obScanBars` | `ObStructuralEngine` truyền `obScanBars` global thay vì `barD-barC+5` |
| 🟢 Thấp | **4.5** Trim buffer 10 | `OBEngine.ComputeTrimIndices` hoặc caller dùng `MaxObs + 10` |
| 🟢 Thấp | **1.2** A3 `cClear` dùng raw | Thêm `rawClearBody` truyền vào `EvaluateAndBatch` |
| 🟢 Thấp | **4.6** Key-break check tách ATR len 14 cố định | `KeyLevelDrawEngine.TryStopOnKeyBreak` dùng ATR riêng |

---

## 6) Alert-layer gaps (phát hiện khi map `pine code alert debug.pine`)

| # | Mục | Pine | C# (trước fix) | Trạng thái |
|---|-----|------|----------------|-----------|
| **A.1** | EFF break expand theo `low[0]/high[0]` mỗi bar | lines 5277-5289: `effBuyBreakBot := min(...)` mỗi tick khi `low<realBuyBot` | `RealZoneEngine.OnPivotBroken` chỉ expand khi có pivot break event | ✅ **FIXED** — `RealZoneEngine.OnRealtimeBarPrice(barHigh, barLow)` gọi từ `PineStateEngine.OnBar` |
| **A.2** | TF gate EVENT M5 chỉ chart `"5"` | `if currentTF == "5" and m5_just_closed` | `isM5Tf = tfTok is "5" or "1" or "2"` — M1/M2 cũng trigger | ✅ **FIXED** — `isM5Tf = tfTok == "5"` |
| **A.3** | `pivMainRole` trong `f_collect_active_zones` | Pine truyền `pivMainRole` vào collector | `ZoneCollector` không dùng `mainRole` | ⚠️ **PENDING** — cần lib source để xác nhận exact filter |
| **A.4** | `m15CloseNow` edge-only trên chart M15 | `ta.change(time("15")) != 0 and barstate.isnew` — edge 1 lần | `tfTok == "15"` → luôn true mỗi tick realtime | ✅ **FIXED** — `isBarClosed && tfTok == "15"` |
| **B.1** | Comment sai `keyStopBar == barIndex-1` | `keyStopBar_pkl == bar_index` | Comment viết `-1` nhưng code đúng | ✅ **FIXED** — comment đã sửa |
| **B.2** | Fail-open khi `Zones == null` | không fire nếu zone data không có | `tActive = 0` (canBuy=T, canSell=T) | ✅ **FIXED** — `tActive = -2` (block both) |
| **C.1** | `condPhaKhungLon` chưa được port | `pine code alert debug.pine` line 5332 | Không tồn tại trong C# | ✅ **FIXED** — `CondPhaKhungLon` + `BarCloseAnyTfPhaKhungLon` + `EventFamilyEvaluators.EvaluatePhaKhungLon` |
