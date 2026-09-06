# Hướng dẫn Deploy và Setup Alert — TradeAlertLoop6Host

> Phiên bản: LOOP6 REV001 (MTF Compound Alert)
> Cập nhật: 2026-05-22

---

## Mục lục

1. [Yêu cầu hệ thống](#1-yêu-cầu-hệ-thống)
2. [Deploy indicator vào cTrader](#2-deploy-indicator-vào-ctrader)
3. [Thêm indicator lên chart](#3-thêm-indicator-lên-chart)
4. [Cấu hình alert đơn (single-TF)](#4-cấu-hình-alert-đơn-single-tf)
5. [Cấu hình Compound Alert (MTF)](#5-cấu-hình-compound-alert-mtf)
6. [Danh sách đầy đủ các alert condition](#6-danh-sách-đầy-đủ-các-alert-condition)
7. [Đọc và hiểu panel trên chart](#7-đọc-và-hiểu-panel-trên-chart)
8. [Đọc log](#8-đọc-log)
9. [Backtest compound rule](#9-backtest-compound-rule)
10. [Các tình huống hay gặp và cách xử lý](#10-các-tình-huống-hay-gặp-và-cách-xử-lý)

---

## 1. Yêu cầu hệ thống

| Yêu cầu | Chi tiết |
|---------|---------|
| cTrader | 4.x trở lên (khuyến nghị bản mới nhất) |
| .NET | 6.0 (đã được đóng gói trong file `.algo`) |
| Quyền truy cập | `AccessRights.None` — không cần quyền đặc biệt |
| Chart TF được hỗ trợ | M1, M2, M5, M15, M30, H1, H4, D1 |
| Compound TF thêm | M5, M15, H1, H4 (dữ liệu tải thêm từ broker) |

---

## 2. Deploy indicator vào cTrader

### Cách 1 — Build từ source (khuyến nghị)

1. Mở terminal tại thư mục project:
   ```
   code_cTrader_LOOP6_REV001_ update\
   ```

2. Chạy build:
   ```powershell
   dotnet build TradeAlert.sln
   ```

3. File `.algo` sẽ tự động copy vào:
   ```
   C:\Users\<tên_user>\Documents\cAlgo\Sources\Indicators\src.algo
   ```

4. Khởi động lại cTrader (hoặc dùng Ctrl+Shift+R để reload).

### Cách 2 — Copy file thủ công

1. Lấy file tại:
   ```
   src\TradeAlert.Indicator\bin\Debug\net6.0\src.algo
   ```

2. Copy vào:
   ```
   C:\Users\<tên_user>\Documents\cAlgo\Sources\Indicators\
   ```

3. Trong cTrader: menu **Automate** → **References** → **Refresh**.

---

## 3. Thêm indicator lên chart

1. Mở chart muốn theo dõi (ví dụ EURUSD M15).
2. Chuột phải lên chart → **Indicators**.
3. Search: `TradeAlertLoop6Host` → **Add**.
4. Cửa sổ Parameters mở ra — cấu hình theo hướng dẫn bên dưới.
5. Nhấn **OK**.

> **Lưu ý:** Khi thêm indicator vào chart H1 hay H4, indicator sẽ phân tích theo TF đó. Với compound alert, bạn không cần mở nhiều chart — chỉ cần 1 chart (thường M15) và cấu hình rule để reference H1/H4.

---

## 4. Cấu hình alert đơn (single-TF)

Đây là cơ chế alert gốc có sẵn từ trước (không cần compound). Indicator tự động fire alert cho tất cả 13 conditions trên TF của chart đang chạy.

### Các group parameter quan trọng

| Group | Mục đích |
|-------|---------|
| **Core Settings** | Lookback bars cho pivot detection |
| **Visual Display** | Hiển thị OB box, keylevel, swing labels |
| **Filters** | OB validation, ATR, pullback filter |
| **cTrader Host** | Bật/tắt alert, debug mode, log prefix |

### Bật alert

Trong group **cTrader Host**:

- `Enable Alerts` = **true** (bật engine compound)
- `Print single alert conditions` = **false** (mặc định — tắt spam `CanBuyTouchM5` mỗi tick; chỉ xem panel + log `COMPOUND R… FIRE`)
- `Message Prefix` = `[Loop6]` — prefix cho log compound
- `Debug Mode` = false (production) / true (khi cần debug chi tiết)
- `Max debug log rows` = 1000

Alert sẽ tự Print ra tab Log của cTrader khi condition fire.

---

## 5. Cấu hình Compound Alert (MTF)

Compound alert cho phép AND nhiều conditions từ nhiều khung thời gian khác nhau. Chỉ fire khi **tất cả** conditions trong 1 rule đều true đồng thời.

### Vị trí cấu hình

Group cuối cùng trong Parameters: **"Compound Alerts"**

```
Compound Rule 1    : [...]
Compound Rule 2    : [...]
Compound Rule 3    : [...]
Compound Rule 4    : [...]
EVENT valid window : [ 3 ]
Show compound panel: [✓]
Print compound fires: [✓]
```

### Cú pháp rule

```
TF:conditionName + TF:conditionName + TF:conditionName
```

**Quy tắc:**
- Separator: `+` hoặc `,`
- TF và condition name: không phân biệt HOA/thường
- Khoảng trắng được bỏ qua tự động
- Để trống = rule bị tắt (không tốn resource)

### TF aliases

| Nhập vào | Ý nghĩa |
|---------|---------|
| `M5` hoặc `5` | Minute 5 |
| `M15` hoặc `15` | Minute 15 |
| `H1` hoặc `60` | Hour 1 |
| `H4` hoặc `240` | Hour 4 |

### Ví dụ rules

**SELL setup đầy đủ 3 TF:**
```
H1:condSellEventHLM15 + M15:condSellEventHLM15 + M5:canSellReal
```
Rule này fire khi: H1 có SELL HL event **VÀ** M15 có SELL HL event **VÀ** M5 đang ở trạng thái canSellReal.

**BUY setup nhanh (M15 + M5):**
```
M15:condBuyEventNGM15 + M5:canBuyReal
```

**SELL với H4 context:**
```
H4:condPhaKhungLon + H1:condSellEventHLM15 + M5:canSellReal
```

**Touch entry M5:**
```
M5:canSellTouchM5 + M5:canSellReal
```

**Multi-event cùng TF:**
```
M15:condSellEventHLM15 + M15:condSellEventNGM15
```
*(Cả 2 event HL và NG của M15 phải cùng active trong window)*

---

## 5b. Preset R1–R6 (theo setup TradingView MTF)

6 rule dưới đây khớp **đúng thứ tự ảnh upload** (mỗi dòng = 1 condition, nối bằng **VÀ / AND**).

> **Giới hạn cTrader:** indicator có **6 ô** `Compound Rule 1..6` — **mặc định đã điền sẵn R1–R6** (xóa ô = tắt rule đó).

**Chart khuyến nghị:** M15 (symbol cần trade). Bật `Show compound panel`, `Print compound fires`, `EVENT valid window` = **3** (tăng lên 5–10 nếu EVENT hay miss window).

### R1 — SELL M5

| # | TradingView (title alert) | TF trên TV |
|---|---------------------------|------------|
| 1 | SELL EVENT M5 | **5m** |
| 2 | CAN SELL REAL | **5m** |
| 3 | CAN SELL REAL | **15m** |
| 4 | CAN SELL TOUCH M5 | **1h** |
| 5 | CAN SELL TOUCH M5 | **4h** |

**cTrader Compound Rule:**
```
M5:condSellEventM5 + M5:canSellReal + M15:canSellReal + H1:canSellTouchM5 + H4:canSellTouchM5
```

### R2 — BUY M5

| # | TradingView | TF |
|---|-------------|-----|
| 1 | BUY EVENT M5 | **5m** |
| 2 | CAN BUY REAL | **5m** |
| 3 | CAN BUY REAL | **15m** |
| 4 | CAN BUY TOUCH M5 | **1h** |
| 5 | CAN BUY TOUCH M5 | **4h** |

**cTrader:**
```
M5:condBuyEventM5 + M5:canBuyReal + M15:canBuyReal + H1:canBuyTouchM5 + H4:canBuyTouchM5
```

### R3 — BUY HL M15

| # | TradingView | TF |
|---|-------------|-----|
| 1 | BUY EVENT HL M15 | **15m** |
| 2 | CAN BUY REAL @ M15 CLOSE | **5m** |
| 3 | CAN BUY REAL | **15m** |
| 4 | CAN BUY TOUCH M5 | **1h** |
| 5 | CAN BUY TOUCH M5 | **4h** |

**cTrader:**
```
M15:condBuyEventHLM15 + M5:canBuyRealAndM15CloseNow + M15:canBuyReal + H1:canBuyTouchM5 + H4:canBuyTouchM5
```

### R4 — SELL HL M15

| # | TradingView | TF |
|---|-------------|-----|
| 1 | SELL EVENT HL M15 | **15m** |
| 2 | CAN SELL REAL @ M15 CLOSE | **5m** |
| 3 | CAN SELL REAL | **15m** |
| 4 | CAN SELL TOUCH M5 | **1h** |
| 5 | CAN SELL TOUCH M5 | **4h** |

**cTrader:**
```
M15:condSellEventHLM15 + M5:canSellRealAndM15CloseNow + M15:canSellReal + H1:canSellTouchM5 + H4:canSellTouchM5
```

### R5 — BUY NG M15

| # | TradingView | TF |
|---|-------------|-----|
| 1 | BUY EVENT NG M15 | **15m** |
| 2 | CAN BUY REAL @ M15 CLOSE | **5m** |
| 3 | CAN BUY REAL | **15m** |
| 4 | CAN BUY REAL | **1h** |
| 5 | CAN BUY TOUCH M5 | **4h** |

**cTrader:**
```
M15:condBuyEventNGM15 + M5:canBuyRealAndM15CloseNow + M15:canBuyReal + H1:canBuyReal + H4:canBuyTouchM5
```

### R6 — SELL NG M15

| # | TradingView | TF |
|---|-------------|-----|
| 1 | SELL EVENT NG M15 | **15m** |
| 2 | CAN SELL REAL @ M15 CLOSE | **5m** |
| 3 | CAN SELL REAL | **15m** |
| 4 | CAN SELL REAL | **1h** |
| 5 | CAN SELL TOUCH M5 | **4h** |

**cTrader:**
```
M15:condSellEventNGM15 + M5:canSellRealAndM15CloseNow + M15:canSellReal + H1:canSellReal + H4:canSellTouchM5
```

### TradingView — tạo alert từng rule

1. Mở chart **M5, M15, H1, H4** cùng symbol, gắn indicator Loop6 trên **cả 4 chart**.
2. **Create Alert** → chọn indicator → **Add condition** lần lượt 5 dòng theo bảng (đúng TF từng dòng).
3. Giữa các condition chọn **VÀ** (AND).
4. Trigger: **Once per bar close** (khuyến nghị cho EVENT) hoặc **Once per bar** cho rule có TOUCH/REAL state.
5. Đặt tên alert: `R1 SELL M5`, `R2 BUY M5`, …

### cTrader — paste nhanh (đã là default trong code)

Mở Parameters → **Compound Alerts** — 6 rule đã điền sẵn. Chỉ cần add indicator lên chart M15 (hoặc TF khác) và bật alert.

| Parameter | Preset |
|-----------|--------|
| Compound Rule 1 | R1 SELL M5 |
| Compound Rule 2 | R2 BUY M5 |
| Compound Rule 3 | R3 BUY HL M15 |
| Compound Rule 4 | R4 SELL HL M15 |
| Compound Rule 5 | R5 BUY NG M15 |
| Compound Rule 6 | R6 SELL NG M15 |

Source: `CompoundAlertPresets.cs` trong `TradeAlert.Core`.

---

## 6. Danh sách đầy đủ các alert condition

### EVENT conditions — chỉ fire tại thời điểm xác định

EVENT condition được coi là "còn hiệu lực" trong N bars kể từ lần fire gần nhất (N = `EVENT valid window`).

| Condition name (Pine) | Mô tả | TF phù hợp |
|----------------------|-------|------------|
| `condBuyEventM5` | BUY event A/B/C trên M5 close | **M5** |
| `condSellEventM5` | SELL event A/B/C trên M5 close | **M5** |
| `condBuyEventHLM15` | BUY event HL: Broken waiting D mới | M5, M15, H1+ |
| `condSellEventHLM15` | SELL event HL: Broken waiting D mới | M5, M15, H1+ |
| `condBuyEventNGM15` | BUY event NG: Main broken / Key stop mới | M5, M15, H1+ |
| `condSellEventNGM15` | SELL event NG: Main broken / Key stop mới | M5, M15, H1+ |
| `condPhaKhungLon` | Phá khung lớn (BROKEN / MAIN BROKEN / key stop) | Mọi TF |

### STATE conditions — đúng/sai theo trạng thái hiện tại

STATE condition không có cửa sổ thời gian — nó phản ánh trạng thái ngay lúc tick hiện tại.

| Condition name (Pine) | Mô tả | TF phù hợp |
|----------------------|-------|------------|
| `canBuyTouchM5` | Đang trong zone touch BUY trên M5 | **M5** |
| `canSellTouchM5` | Đang trong zone touch SELL trên M5 | **M5** |
| `canBuyReal` | Realtime filter cho phép BUY | Mọi TF |
| `canSellReal` | Realtime filter cho phép SELL | Mọi TF |
| `canBuyRealAndM15CloseNow` | canBuyReal VÀ đang tại M15 close edge | M5, M15 |
| `canSellRealAndM15CloseNow` | canSellReal VÀ đang tại M15 close edge | M5, M15 |

### Bảng cảnh báo TF × Condition

Nếu bạn kết hợp condition sai TF, indicator sẽ in warning vào Log nhưng không báo lỗi. Rule vẫn chạy nhưng condition đó sẽ không bao giờ true.

| Condition | Không nên dùng với TF |
|-----------|----------------------|
| `condBuyEventM5`, `condSellEventM5` | H1, H4 (bị gate bởi Pine TF=="5") |
| `canBuyTouchM5`, `canSellTouchM5` | H1, H4 |
| `canBuyRealAndM15CloseNow`, `canSellRealAndM15CloseNow` | TF không phải M5, M15 |

---

## 7. Đọc và hiểu panel trên chart

Panel xuất hiện tại góc **trên bên phải** của chart khi `Show compound panel` = true.

### Ví dụ panel

```
━ COMPOUND ALERTS ━
R1 R1 : FIRE
   [OK] bar#231 H1 condSellEventHLM15
   [OK] bar#892 M15 condSellEventHLM15
   [OK] (state) M5 canSellReal
R2 R2 : waiting
   [--] M15 condBuyEventHLM15
   [OK] (state) M5 canBuyReal
```

### Giải thích ký hiệu

| Ký hiệu | Ý nghĩa |
|---------|---------|
| `FIRE` | Rule đang active — tất cả conditions đều true |
| `waiting` | Ít nhất 1 condition chưa thỏa |
| `[OK] bar#N` | EVENT condition đã fire tại bar N, còn trong window |
| `[OK] (state)` | STATE condition đang true |
| `[--]` | Condition đang false |

### Màu panel

| Phần | Màu |
|------|-----|
| Tiêu đề `COMPOUND ALERTS` | Trắng |
| Kẻ ngang giữa các rule | Xám |
| Dòng đầu rule (`R1 … : waiting`) | **Vàng** |
| Dòng đầu rule (`R1 … : FIRE`) | **Đỏ** |
| Chi tiết condition `[OK]` / `(state)` true | **Xanh lá** |
| Chi tiết condition `[--]` false | Xám |
| Rule `(off)` | Xám mờ |

---

## 8. Đọc log

Khi `Print compound fires` = true và rule fire, Log tab hiển thị:

```
[Loop6] COMPOUND R1 [R1] FIRE bar=892 time=2026-05-22T14:00:00 close=1.08504
```

**Giải thích:**
- `R1` — số thứ tự rule (Rule 1..4)
- `[R1]` — tên rule (hiện tại default là R1, R2,... — sẽ custom được sau)
- `bar=892` — bar index trên chart đang chạy
- `time=...` — thời điểm bar mở
- `close=...` — giá close của bar đó

**Đặc tính dedup:** Mỗi chart bar chỉ in tối đa 1 dòng/rule, dù realtime tick bao nhiêu lần.

---

## 9. Backtest compound rule

1. Mở cTrader → **Backtesting** (Ctrl+T)
2. Chọn indicator `TradeAlertLoop6Host`
3. Điền compound rules vào parameters
4. Chạy backtest như thường

Trong quá trình backtest:
- HTF engines (H1, H4,...) được build song song với từng bar replay
- Compound rule fire bar-by-bar theo thứ tự thời gian
- Print log xuất hiện trong tab Log của backtest
- Panel cập nhật từng bar (có thể xem trong Visual Backtesting)

> **Lưu ý về độ sâu lịch sử:** `MarketData.GetBars(H1)` tải lượng bars H1 mặc định của broker. Nếu backtest đi quá xa lịch sử và H1 chưa có đủ dữ liệu, compound condition trên H1 sẽ không fire ở các bar đầu. Không ảnh hưởng logic — chỉ cần nhận thức điều này khi phân tích backtest.

---

## 10. Các tình huống hay gặp và cách xử lý

### Panel không hiện

- Kiểm tra `Show compound panel on chart` = true
- Kiểm tra `Compound Rule 1` (hoặc rule bất kỳ) đã nhập — panel chỉ hiện khi có ít nhất 1 rule hợp lệ

### Rule không fire dù nhìn chart thấy đúng điều kiện

1. Mở Log → tìm warning `WARN R1:` — nếu có, TF hoặc condition name bị sai
2. Tăng `EVENT valid window` lên (vd. từ 3 lên 10) — EVENT có thể đã fire nhưng qua window
3. Bật `Debug Mode` = true để xem chi tiết evaluation từng bar
4. Kiểm tra compound rule chỉ fire trên chart đang chạy — nếu chart là H1 mà rule dùng `M5:condBuyEventM5` thì M5 engine sẽ được load nhưng M5 event gate trong Pine chỉ nhận TF=="5"

### Warning "will never fire" trong Log

```
[Loop6] [Compound] WARN R1: condBuyEventM5 on TF '60' will never fire
```

Nghĩa là bạn đã viết `H1:condBuyEventM5`. Condition này theo Pine chỉ fire khi chart TF là M5. Sửa lại thành `M5:condBuyEventM5`.

### Indicator chậm khi mở chart

Lần đầu mở chart, HTF engines cần build state cho toàn bộ bars lịch sử. Quá trình này diễn ra âm thầm (không block UI). Với 5000 M15 bars + H1 engine: thường dưới 5 giây.

### Compound alert không fire trong backtest

- Kiểm tra `Print compound fires` = true
- Kiểm tra Log tab trong cửa sổ Backtest (không phải Log tab main)
- Đảm bảo backtest range có đủ dữ liệu để HTF engine warm up (ít nhất vài trăm bars HTF trước thời điểm muốn test)

### Sau khi đổi parameter không thấy thay đổi

cTrader recreate indicator khi Apply parameters — đây là hành vi bình thường. HTF engines sẽ build lại từ đầu. Đợi vài giây.

---

## Phụ lục: Cấu trúc file liên quan

```
src/
├── TradeAlert.Core/
│   └── Models/Alerts/
│       ├── AlertConditionId.cs         — 13 condition IDs
│       ├── CompoundAlertRule.cs        — Model rule + entry
│       └── CompoundRuleParser.cs       — Parse string, map Pine names
└── TradeAlert.Indicator/
    ├── MtfEngineManager.cs             — Engine manager, temporal cursor
    ├── TradeAlertLoop6Host.cs          — Indicator chính (cTrader entry point)
    └── Loop6EvaluationContextFactory.cs — Build context cho chart TF

docs/
└── HUONG_DAN_DEPLOY_VA_SETUP_ALERT.md — File này
```
