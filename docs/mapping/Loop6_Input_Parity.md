# Loop6 — Pine inputs ↔ cTrader host parity

Nguồn Pine: `PINE_CODE/pine code arlert new.pine` (dòng 20–149).  
Host: `TradeAlertLoop6Host.cs` → `Loop6ParameterBridge` → `PineStateEngine` / `Loop6StylePalette`.

## Khớp 1:1 (tên nhóm, default, min/max chính)

| Nhóm | Pine | cTrader |
|------|------|---------|
| Core Settings | `lookbackBars` 200 | `Lookback bars for pivots` 200 |
| Visual Display | 8 bool/int + `obBoxOpacity` 70 | Giống (tiếng Việt giữ nguyên label TV) |
| Filters | 18 inputs (OB, wick, pullback, gap, A4 doji, …) | Giống; `Gap threshold` không còn `maxval=3` |
| Break Rules | `breakR3MaxK` 3 | `R3: max k …` 3 |
| OB Settings | 6 inputs | Giống |
| Keylevel Settings | 6 inputs | Giống (`ATR length` — Pine EN; TV có thể hiện bản dịch) |
| Style Options | border, opacity, 7 label colors, 3 box colors, label opacity | Đã thêm đủ trên host + bridge |

## Khác biệt UI (không sửa được từ code)

- cTrader: bool = dropdown **Có/Không**; TradingView = checkbox.
- cTrader: màu = color picker; default tên màu gần Pine, hex fallback trong `Loop6ColorUtil` khi màu = (0,0,0,0).

## Hành vi / wire

| Input | Ghi chú |
|-------|---------|
| `obAtrMultiplier` | Có trên UI; Pine khai báo, C# OB engine chưa dùng (parity UI). |
| `keylevelUseAtrRule` | Wire `FindReferenceCandle` / `RecreateKeyBox`. |
| `keylevelLookback` | Wire `PineStateEngine` (trước hardcode 2). |
| Label / key colors | `Loop6StylePalette` + `PivotLabelEngine` / `KeyLevelVisual`. |
| Key opacity 90/70 | `PineColors.DefaultKeyOpacity*` + host `Key opacity ACTIVE/BROKEN`. |

## Chỉ cTrader (`cTrader Host`)

Alerts, debug, extend right — không có trên Pine.
