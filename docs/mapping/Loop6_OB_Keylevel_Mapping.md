# Loop 6 — OB + Keylevel mapping (Pine → C#)

**Quan hệ key giữa FAKE / BROKEN / MAIN C / MAIN_OLD / MAIN BROKEN:** [Loop6_Keylevel_Swing_Relations.md](./Loop6_Keylevel_Swing_Relations.md)

## Pivot flags → key style (`f_sync_keybox_with_flag`)

| Flag | Tên | Key box | C# |
|------|-----|---------|-----|
| 1 | ACTIVE | H/L fill + solid | `EmitUpdateKeyBoxActive` |
| 0 | MAIN C | Vàng solid | `EmitUpdateKeyBoxMainC` |
| 2 | BROKEN | Broken invert + dashed | `EmitUpdateKeyBoxBroken` |
| 6 | BROKEN FAKE | Giống BROKEN | `EmitUpdateKeyBoxBroken` |
| 5 | MAIN FAKE | ACTIVE style | `EmitUpdateKeyBoxActive` |
| -3 | MAIN_FAKE_BROKEN | ACTIVE + **stop extend** (không đổi broken) | `EmitStopExtendKeyBox` |
| 4 | D_SWING | ACTIVE | `EmitUpdateKeyBoxActive` |
| -1,-2 | MAIN BROKEN / LOCK | Xóa key + cascade OB | `EmitDelKeyBox` + `ObPoolMaintenance` |

## Opacity (giảm mờ)

| Pine input | Cũ (transp) | Mới default C# |
|------------|-------------|----------------|
| `inpKeyOpacityActive` 90 | Rất mờ | **50** (`KeyOpacityActivePercent`) |
| `inpKeyOpacityBroken` 70 | Mờ | **35** |
| `obBoxOpacity` 70 | Mờ | **45** (`DefaultObBoxOpacityPercent`) |

`PineColors.WithTransparency(r,g,b, transp)` — `PineStateEngine.KeyOpacityActivePercent` có thể chỉnh từ host.

## OB pool

| Pine | C# | Source |
|------|-----|--------|
| `f_find_and_draw_OB` | `ObPivotFinderEngine` | 0 pivot |
| `f_find_and_draw_OB_structural` | `ObStructuralEngine` | 1 structure (sau MAIN C) |
| `f_add_OB` / initial scan | `ObPoolMaintenance.PushOb` + `OBEngine.ComputeInitialObState` |
| Visibility loop | `PineStateEngine` OB tick + `ObDrawEngine` |
| `build_ob_box` | `ObDrawEngine.BuildFromBar` + `EnqueueObBox` |
| ZIN extend | `ExtendObBox` mỗi bar |
| LOSE stop extend | `StopExtendObBox` — Pine `time[1]` (= `checkBar+1`); host `ToChartRightBarIndex` → cạnh phải tại bar mất zin |
| LOSE label hide | `hideOBLoseZin` chỉ ẩn label; LTF reject vẫn `DelObBox` |
| `f_delete_OBs_of_pivot` | `ObPoolMaintenance.DeleteObsOfPivot` |

## OB state

| State | Ý nghĩa |
|-------|---------|
| 0 | Pending |
| 2 | ZIN — vẽ box, extend |
| 3 | LOSE — stop extend hoặc xóa box |

## Lock swing (`f_lock_swings_before` + `update_label`)

| Pine | C# |
|------|-----|
| `lockSwingCount` (default **50**) | `PineStateEngine.LockSwingCount` / host parameter |
| `f_lock_swings_before(lockFromIdx)` | `MainPromotionEngine.LockSwingsBefore` sau D-commit |
| `lockFromIdx = max(dIdx - count, 0)` | Cùng logic trong `TryCommitD` |
| Xóa key + OB khi lock (kể cả FAKE key) | `EmitDelKeyBox` + `DeleteObsOfPivot` |
| `pivFlag = -2`, `pivFlagBeforeLock` | `SetFlag(-2)` + `SetFlagBeforeLock` (FAKE không đổi flag) |
| `update_label` mỗi bar | `PivotLabelEngine.RefreshAll` cuối `OnBar` |
| `showLockedSwings` / `showFakeSwings` / `showActiveSwings` | `PivotLabelRenderOptions` + host Visual Display |

**Lưu ý Pine:** label LOCKED dùng `flagForRendering` = flag trước lock; nếu đó là ACTIVE (1) thì vẫn cần `showActiveSwings=true` mới thấy chữ (sau khi bật `showLockedSwings`).

## OB LTF confirm

| Pine | C# |
|------|-----|
| `f_push_ltf_snapshot` | `LtfRingBufferStore.PushSnapshot` (cuối `PineStateEngine.OnBar`) |
| `f_get_ltf_from_buffer` | `LtfRingBufferStore.TryGetRange` |
| `f_check_ob_ltf_confirm_full_range` | `ObLtfConfirmEngine.CheckFullRange` |
| Confirm tại `checkBar` (0→2 ZIN) | `ObLtfConfirmEngine.ConfirmCheckBar` |
| `obFlagLTF` 0/2/3 | `ObPoolStore.FlagLtf` |
| LTF confirm kind (label debug) | `ObLtfConfirmKind` → `OB` / `OB no LTF data` / `OB has LTF but not real H=… L=… x=…` / `OB?` |
| `useOBConfirm` | `PineStateEngine.UseObConfirm` (default true) |
| LTF data host | `HostLtfCollector` + `TimeframeMapping.ResolveLtfSecondsByHtf` |

Luồng: HTF tick → OB lifecycle → nếu `state==2` và `flagLtf==0` → full-range LTF; nếu vừa 0→2 → confirm tại `bar_index-2`. Reject → `state=3`, `DelObBox` / `StopExtendObBox`. Không có LTF mapping hoặc không touch → auto confirm (`flagLtf=2`).

## OB overlap + labels

| Pine | C# |
|------|-----|
| `f_limit_overlapping_ob_boxes` | `ObOverlapEngine.LimitOverlappingObBoxes` |
| `label.new` + visibility | `ObLabelDrawEngine` (`EmitCreate`, `UpdateZinVisibility`) |
| `f_set_lose_label_text` | `ObLabelDrawEngine.SetLoseLabel` |
| `hideOBZin` / `hideOBLoseZin` | `HideObZin` / `HideObLoseZin` |

## Host drawing

`TradeAlertLoop6Host`: `EnqueueObBox`, `ExtendObBox`, `StopExtendObBox`, `DelObBox`, OB labels (`AddLabel`/`DelLabel`), LTF feed qua `HostLtfCollector`.
