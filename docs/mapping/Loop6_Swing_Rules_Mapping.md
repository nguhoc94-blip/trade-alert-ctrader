# Loop 6 — Swing rules mapping (Pine → C#)

**Pine baseline:** `pine code arlert new.pine`  
**HTF/LTF:** xem **`Loop6_HTF_LTF_Mapping.md`** (bắt buộc đọc trước phần wick)

## Bar pipeline (mỗi bar đóng trên **HTF chart**)

| Thứ tự | Pine | C# | Status |
|--------|------|-----|--------|
| 0 | Snapshot `pivFlag` prev | `PivotTransitionEngine.SnapshotPrevFlags` | implemented |
| 1 | `eff*` + gap merge XY | `PineStateEngine` | implemented |
| 2 | Wick neutralize → `cleanHigh1Raw` / `cleanLow1Raw` | `WickNeutralizeEngine` → `pivCandHigh/Low` | implemented (LTF bundle: partial) |
| 3 | `process_break` R1–R3 | `PivotTransitionEngine.Tick` | implemented |
| 4 | Pullback Step 1 push A | `PullbackFilterEngine.PushStep1` | implemented |
| 5 | Micro rescue Step 1.5 | `MicroSwingEngine.Detect` | implemented |
| 6 | Pullback Step 2–3 batch | `PullbackFilterEngine.EvaluateAndBatch` | implemented |
| 7 | MAIN: push M → batch | `pushQueue` (MICRO trước batch) | implemented |
| 8 | Keylevel + OB + HH/LL + D-commit | `KeyLevel*`, `OBEngine`, `MainPromotionEngine` | implemented |
| 9 | RealZone + extend/overlap/sync | `RealZoneEngine`, `KeyLevelDrawEngine` | implemented |

## Wick noise (Pine 258–341) — trên HTF, confirm bằng LTF con

- **HTF:** `high[1]`, `low[1]`, `open[1]`, `close[1]` (chart TF).
- **LTF:** `request.security_lower_tf(..., ltfTf, ...)` với `ltfTf = f_resolve_ltf_by_htf()` — **mảng** nến trong HTF `[1]`.
- **Không** dùng `request.security` cho bước này.
- Fallback: `atr14[1] * wickAtrNoiseMult` khi `hasLtfData=false`.

**C#:** `TimeframeMapping.ResolveLtfTokenByHtf` + `ILtfBarBundle` (host feed decomposition).

## Pullback / micro / break

(Xem bản trước — không đổi logic; tham chiếu `Loop6_HTF_LTF_Mapping.md` cho `cleanHigh`/`cleanLow` series.)

## Known gaps

| Item | Ghi chú |
|------|---------|
| Host feed `security_lower_tf` bundle | `partial` |
| `oNoise` prefetch (`request.security` noiseTF) | chưa wire — lane wick dùng preprocess |
| M5/M15 absolute alerts | host `carry_over` |
