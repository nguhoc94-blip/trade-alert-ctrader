# Loop 6 — HTF / LTF mapping (theo Pine)

**Baseline:** `pine code arlert new.pine` (lines 93–100, 174–182, 343–345, 187–241)

## Thuật ngữ (không nhầm)

| Tên | Pine | Ý nghĩa |
|-----|------|---------|
| **HTF (chart)** | `timeframe.period` | Khung chart gắn indicator — swing, break, keylevel, `high[1]`/`low[1]` đều trên **series này** |
| **LTF wick** | `ltfTf = f_resolve_ltf_by_htf()` | Một bậc **thấp hơn** HTF, chỉ cho confirm wick |
| **Noise TF** | `noiseTF = filters.f_get_noise_tf()` | Khung thấp hơn theo ladder **khác** (prefetch noisy OHLC) |
| **M5 / M15 cố định** | `request.security(..., "5")` / `"15"` | Alert touch/event — **không** đổi theo chart TF |

## Hai API TradingView khác nhau

### 1) `request.security_lower_tf` — LTF **bên trong** HTF (wick)

```pine
[oLtf, hLtf, lLtf, cLtf] = request.security_lower_tf(
    syminfo.tickerid,
    ltfTf,  // f_resolve_ltf_by_htf()
    [open, high, low, close])
```

- Mỗi nến HTF tại `[k]` có **mảng** nến LTF cấu thành nó.
- Wick confirm đọc `oLtf[1]`, `cLtf[1]` — các nến LTF trong **HTF bar vừa đóng** `[1]`.
- Đây là “nến khung thấp hơn hình thành nên nến HTF” — **đúng** cho wick.

**C#:** `WickNeutralizeEngine.ILtfBarBundle` + `TimeframeMapping.ResolveLtfTokenByHtf(chartSeconds)`.

### 2) `request.security` — series khung khác **align** theo HTF

```pine
[oNoise, hNoise, lNoise, cNoise] = request.security(syminfo.tickerid, noiseTF, ohlc, ...)
[m5_open, ...] = request.security(syminfo.tickerid, "5", ...)
[m15_open, ...] = request.security(syminfo.tickerid, "15", ...)
```

- Mỗi bar HTF nhận **một** giá trị (hoặc bar cuối) từ TF `noiseTF` / `"5"` / `"15"`.
- **Không** trả về mảng nến con như `security_lower_tf`.
- Dùng cho noise prefetch, touch M5, edge M15 — **không** thay cho wick LTF.

## Bảng `f_resolve_ltf_by_htf()` (wick LTF)

| Chart HTF | `ltfTf` (LTF) |
|-----------|---------------|
| M5 (`5`) | M2 (`2`) |
| M15 (`15`) | M5 (`5`) |
| H1 (`60`) | M15 (`15`) |
| H4 (`240`) | H1 (`60`) |
| D | H4 (`240`) |
| W | D |

**C#:** `TimeframeMapping.ResolveLtfTokenByHtf(int htfSeconds)`.

## Bảng `f_get_noise_tf()` (noise — khác LTF wick)

| Chart HTF (giây) | `noiseTF` |
|------------------|-----------|
| ≥ 240 phút | `60` |
| ≥ 60 phút | `15` |
| ≥ 15 phút | `5` |
| ≥ 5 phút | `2` |
| còn lại | `1` |

Ví dụ **Daily**: LTF wick = H4 (`240`), noise TF = H1 (`60`) — **khác nhau**.

**C#:** `FilterEngine.GetNoiseTf` / `TimeframeMapping.GetNoiseTfToken`.

## Luồng wick trên HTF chart

```
HTF bar [1] (RAW high/low)
    → detect wick (avgRange, body*0.5)
    → confirm: LTF max/min body trong oLtf[1] HOẶC atr14[1]*mult
    → neutralize → cleanHigh1Raw / cleanLow1Raw → pivCandHigh/Low
```

Comment Pine line 46: noisy wick HTF cũ đã chuyển sang preprocess; `oNoise` vẫn prefetch nhưng lane chính là neutralize.

## C# hiện trạng

| Pine | C# | Status |
|------|-----|--------|
| HTF series | `SeriesBuffer` (chart TF host) | implemented |
| `f_resolve_ltf_by_htf` | `TimeframeMapping.ResolveLtfTokenByHtf` | implemented |
| `security_lower_tf` → bundle | `ILtfBarBundle` | partial — host chưa feed |
| Wick neutralize + ATR fallback | `WickNeutralizeEngine` | implemented |
| `f_get_noise_tf` | `FilterEngine.GetNoiseTf` | implemented (prefetch chưa wire) |
| M5/M15 absolute | host alert path | carry_over |

## Host checklist (cTrader)

1. `htfSeconds` = chart `TimeFrame` indicator.
2. `ltfSeconds` = `TimeframeMapping.ResolveLtfSecondsByHtf(htfSeconds)`.
3. Khi HTF bar đóng: thu **tất cả** bar LTF có `OpenTime` ∈ (htfOpen, htfClose] → `ILtfBarBundle`.
4. Không dùng `request.security`-style merge cho wick — phải là decomposition giống `security_lower_tf`.

## Liên kết

- `Loop6_Swing_Rules_Mapping.md` — pipeline swing
- `Loop6_MTF_BarTiming_Contract.md` — alert M5/M15 edge
