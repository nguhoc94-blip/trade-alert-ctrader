# Hướng dẫn chạy Loop6BacktestTradingBot (cTrader)

> **Đã setup sẵn** (Release build + deploy). Bạn chỉ cần mở cTrader theo các bước dưới.  
> Sau này sửa code → chạy lại `CAI-DAT-BACKTEST.bat` hoặc `setup-backtest-robot.ps1`.

---

## Bước 1 — Mở cTrader

1. Mở **cTrader** (đăng nhập tài khoản / demo).
2. Vào **Automate** (tab cBot) → danh sách **cBots**.
3. Tìm robot: **`Loop6BacktestTradingBot`**  
   (file deploy: `%USERPROFILE%\Documents\cAlgo\Sources\Robots\TradeAlert.BacktestRobot.algo`)

Nếu không thấy: đóng/mở lại cTrader, hoặc chạy `CAI-DAT-BACKTEST.bat` rồi refresh.

---

## Bước 2 — Chart M15 (bắt buộc)

1. Mở chart symbol cần test, timeframe **M15** (ví dụ **XAUUSD M15**).
2. Kéo **Loop6BacktestTradingBot** vào chart **hoặc** mở **Strategy Tester** → chọn bot + symbol M15.

---

## Bước 3 — Lần đầu: dry-run (không vào lệnh)

Giữ mặc định hoặc chỉnh:

| Nhóm | Tham số | Giá trị lần đầu |
|------|---------|-----------------|
| **01. Backtest** | Enable Trading | **false** |
| **01. Backtest** | Enable Log | **true** |
| **02. Signal** | Print compound fires | **true** |
| **01. Backtest** | Warmup Bars | **3000** |

Chạy backtest → tab **Log** sẽ có dòng:

- `FIRE R# BUY/SELL bar=… -> PLAN …` (setup hợp lệ), hoặc  
- `FIRE R# … -> SKIP (…)` (bỏ qua, có lý do).

So với scanner/email: cùng symbol, cùng khoảng thời gian → **slot R#, chiều, bar phải khớp**.

---

## Bước 4 — Bật trade thật

Khi dry-run ổn:

| Nhóm | Tham số | Giá trị |
|------|---------|---------|
| **01. Backtest** | Enable Trading | **true** |
| **10. Trade/Risk** | FTMO Account Balance | ví dụ **100000** |
| **10. Trade/Risk** | Risk % | ví dụ **1** |
| **10. Trade/Risk** | Reward:Risk | **1.5** |
| **10. Trade/Risk** | SL width multiplier | **2** |

Kiểm tra **Positions / Pending / History**: limit, SL/TP, đóng khi B broken, flip, session cutoff.

---

## Tham số theo symbol (group 10)

| Symbol | Asset type | Quote currency | Manual conv USD/quote | Custom contract size |
|--------|------------|----------------|------------------------|----------------------|
| **XAUUSD** | XAUUSD | *(để trống = USD)* | **0** | 1 |
| **AUDCAD** | Forex | *(để trống = CAD)* | **0** hoặc ≈ 1/USDCAD | 100000 |
| **EURGBP** | Forex | *(để trống = GBP)* | **0** hoặc ≈ GBPUSD | 100000 |
| **GERMANY40** | Custom | **EUR** | ≈ EURUSD (vd 1.08) | **1** |

- **Manual conv = 0**: bot tự tìm `{QUOTE}USD` hoặc `USD{QUOTE}`. Strategy Tester không có symbol quy đổi → log `no lot`, cần nhập tay.
- **Spread**: group **12** — để **0** = spread broker mặc định.

---

## Session (group 11 — mặc định)

- Vào lệnh từ **6h sáng VN** (23:00 UTC).
- **3h sáng VN** (20:00 UTC): chặn lệnh mới + đóng hết lệnh đang mở.
- **Allow flip** / **Allow parallel**: true (mặc định).

---

## Robot khác trong cùng folder Robots

| File | Robot |
|------|--------|
| `TradeAlert.BacktestRobot.algo` | **Loop6BacktestTradingBot** (backtest trade) |
| `src.algo` | **Loop6MultiSymbolEmailScanner** (email alert, không trade) |

---

## Cập nhật sau khi sửa code

Double-click: **`CAI-DAT-BACKTEST.bat`**  
Hoặc PowerShell trong thư mục project:

```powershell
.\setup-backtest-robot.ps1
```

Chi tiết kỹ thuật: `src\TradeAlert.BacktestRobot\RUNBOOK.md`
