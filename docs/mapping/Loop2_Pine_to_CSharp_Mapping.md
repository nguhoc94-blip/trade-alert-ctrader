# Loop 2 — Pine → C# mapping status

Project: DEMO_TRADE_PROJECT  
Pod: POD_MAIN  
Source of truth Pine (read-only): `TAI_LIEU_DU_AN/CP0/NGUỒN DỰ ÁN BAN ĐẦU/`  
Inventory: `TAI_LIEU_DU_AN/PHASE 2/phase2_loop1_artifacts_REV_001/phase2_loop1_REV_001/pine_inventory.md`

| Pine | Symbol | C# | Status |
|---|---|---|---|
| lib_utils | `f_fmt_H` | `UtilsEngine.FmtH` | implemented |
| lib_utils | `f_fmt_L` | `UtilsEngine.FmtL` | implemented |
| lib_utils | `f_add_label` | `UtilsEngine.AddLabel` → `IDrawingCommandSink` | implemented |
| lib_utils | `f_add_line` | `UtilsEngine.AddLine` | implemented |
| lib_utils | `f_del_label` | `UtilsEngine.DelLabel` | implemented |
| lib_utils | `f_del_line` | `UtilsEngine.DelLine` | implemented |
| lib_filters | `f_get_noise_tf` | `FilterEngine.GetNoiseTf` | implemented |
| lib_filters | `f_is_noisy_wick` | `FilterEngine.IsNoisyWick` | implemented |
| lib_filters | `f_is_real_pullback` | `FilterEngine.IsRealPullback` | implemented |
| lib_filters | `is_valid_pullback_low` | `FilterEngine.ValidPullbackLow` | implemented |
| lib_filters | `is_valid_pullback_high` | `FilterEngine.ValidPullbackHigh` | implemented |
| lib_filters | `f_failed_by_continuation_high` | `FilterEngine.FailedByContinuationHigh` | implemented |
| lib_filters | `f_failed_by_continuation_low` | `FilterEngine.FailedByContinuationLow` | implemented |
| lib_micro_swing | `f_isDojiSellForce` | `MicroSwingEngine.IsDojiSellForce` | implemented |
| lib_micro_swing | `f_isDojiBuyForce` | `MicroSwingEngine.IsDojiBuyForce` | implemented |
| lib_micro_swing | `f_micro_swing_detect` | `MicroSwingEngine.MicroSwingDetect` | skeleton_guarded (`NotImplementedException`) |
| core model | (n/a) | `BarSnapshot`, `ChartTimePolicy`, `BarRuntimeFlags`, `OhlcTuple`, `SeriesBuffer`, `TimeframeState`, `EngineContext` | implemented |
| adapter | (n/a) | `ICandleFeedAdapter`, `FakeCandleFeedAdapter` | implemented |
| main pine | các hàm local khác | `*Engine` tương ứng | not_in_loop2 |
| lib_keylevel_box | * | KeyLevelEngine | carry_over |
| lib_alerts new | * | AlertEngine | carry_over |

**REV_003 (2026-05-10) — chart time:** canonical = `BarSnapshot.OpenChartTimeLocal` (`DateTimeKind.Unspecified`, wall clock từ baseline **DATA CHART.zip** tại `/mnt/data/DATA CHART.zip`). Không dùng `ToUniversalTime()` theo máy. Offset baseline UTC+7 cố định trong `ChartTimePolicy`; UTC derived chỉ qua `OpenTimeUtcDerived` / `ChartLocalUnspecifiedToUtcInstant` (không key parity chính). `Local`/`Utc` raw vào snapshot → từ chối; từ UTC explicit → `UtcInstantToChartLocalWallClock`.

**Mapping gap:** `lib_filters` có comment tham chiếu `f_is_real_pullback_spec73` trong main Pine — không có trong file lib_filters hiện tại; parity structure gate theo spec Product nếu cần.

**dynamic_requests:** chưa port; Loop 2 chỉ pure math trên OHLC đã inject.
