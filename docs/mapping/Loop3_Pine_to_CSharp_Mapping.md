# Loop 3 — Pine → C# mapping (KeyLevel / OB skeleton)

Scope: `lib_keylevel_box.pine`, pivot/OB stores và skeleton đồng bộ với GIAO TASK Phase 2 Loop 3.  
Trạng thái hợp lệ: `implemented` | `skeleton_guarded` | `carry_over` | `not_in_loop3` | `mapping_gap`.

## Pure lib_keylevel_box

| Pine | C# | Status | Notes |
|------|-----|--------|--------|
| `has_clear_body_raw` | `KeyLevelEngine.HasClearBodyRaw` | **implemented** | `useAtrRule=true` → bắt buộc `atrValueWhenRuleEnabled`; không gọi `ta.atr` trong Core. |
| `find_reference_candle_from_pivot` | `KeyLevelEngine.FindReferenceCandleFromPivot` | **implemented** | `barIndex` tường minh; fallback `best_lag := pivot_lag` như Pine khi không tìm thấy ứng viên. |
| `build_keylevel_box` | `KeyLevelEngine.BuildKeylevelBox` → `KeyBoxSpec` | **implemented** | `minTick` inject; ATR thickness qua `atrValue`; thời gian qua `ChartTimePolicy.EnsureChartLocalUnspecified`; optional `IDrawingCommandSink` với `EnqueueKeyBoxSpec` (DTO only). |
| `f_is_valid_ob_candle` | `KeyLevelEngine.IsValidObCandle` / `OBEngine.IsValidObCandle` | **implemented** | Delegate từ `OBEngine`. |

## Models / state

| Pine (concept) | C# | Status |
|----------------|-----|--------|
| `box` geometry + xloc time | `KeyBoxSpec`, `KeyBoxRef` | **implemented** |
| reference candle tuple | `ReferenceCandlePick` | **implemented** |
| pivot parallel columns (task list) | `PivotStateStore` | **skeleton_guarded** | Cột đủ theo task; logic transition/break machine chưa port. |
| OB pool parallel columns | `ObPoolStore` | **skeleton_guarded** | `obBoxes` → `KeyBoxRef?`; `obLabelDrawingKeys` → id lệnh vẽ, không handle Pine. |
| OB identity snapshot | `ObIdentitySnapshot` | **implemented** |
| pivot row view | `PivotSnapshot` | **implemented** |
| LTF ring contract | `LtfRingBufferSpec` | **skeleton_guarded** | Không `request.security_lower_tf`. |
| read façades | `IPivotReadOnlyView`, `ICleanOhlcSeries` | **implemented** |

## OBEngine / MicroSwing

| Pine | C# | Status |
|------|-----|--------|
| OB lifecycle (`f_add_OB`, draw, delete, …) | `OBEngine.ObLifecycleTransitionNotMapped` | **skeleton_guarded** | `NotImplementedException` có message `mapping_gap`. |
| `f_micro_swing_detect` | `MicroSwingEngine.MicroSwingDetect` | **carry_over** | Vẫn `NotImplementedException`. |
| guard + context | `MicroSwingEngine.TryMicroSwingDetect` + `MicroSwingGuardContext` | **implemented** | Không success im lặng; đủ context vẫn `DetectNotPortedLoop3`. |

## not_in_loop3 / mapping_gap

- `AlertEngine`, `TimeframeEngine`, `SignalAggregator`, binding indicator cTrader, `request.security*`, cBot: **not_in_loop3** (không thêm trong Loop 3).
- Pivot break machine đầy đủ như Pine main: **mapping_gap** / chờ loop sau khi có state transition đã map.
- `ta.atr` tại lag động trong `has_clear_body_raw` (Pine): **mapping_gap** so với runtime ATR series — Core chỉ nhận scalar/context inject tại điểm đánh giá (theo lock Engineering).
