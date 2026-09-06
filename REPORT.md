# BÁO CÁO TIẾN ĐỘ — LOOP6 cBot / News Filter Patch

**Ngày báo cáo:** 18/06/2026 12:21 (UTC+7)
**Project:** `TradeAlert.BacktestRobot` — LOOP6 Backtest cBot
**Trạng thái tổng thể:** ✅ HOÀN THÀNH — Build 0 lỗi · 279 tests passed · .algo đã deploy

---

## 1. MỤC TIÊU TỔNG QUÁT

Bổ sung **Automatic News Filter** cho LOOP6 cBot nhằm:
- **Chặn entry mới** trong vùng thời gian xung quanh tin USD CPI / NFP
- **Force-close** toàn bộ vị thế/pending orders của bot trước khi tin ra
- **Không thay đổi** bất kỳ logic entry / SL / TP / risk hiện tại
- **Không đụng** vào lệnh thủ công hoặc lệnh của robot khác
- **Backtest offline** hoàn toàn không cần internet (dùng historical CSV)

---

## 2. FILES MỚI ĐÃ TẠO

### 2.1 Core — `src/TradeAlert.BacktestRobot/Execution/News/`

| File | Mô tả |
|---|---|
| `NewsEventCategory.cs` | Enum: `Other`, `Cpi`, `Nfp` |
| `NewsEvent.cs` | Data model cho 1 sự kiện tin tức. Bao gồm override fields `BlockBeforeMinOverride`, `BlockAfterMinOverride`, `ForceCloseBeforeMinOverride` (nullable — dùng cho historical CSV) |
| `NewsBlockDecision.cs` | Kết quả quyết định block/force-close. Có `BuildSkipDetail()` để format CSV |
| `NewsFilterConfig.cs` | Config immutable (29 parameters). Parse từ bot params. Enum `NewsFailSafeMode`, `NewsBacktestMode` |
| `INewsCalendarProvider.cs` | Interface fetch news (không throw exception) |
| `TradingEconomicsNewsProvider.cs` | Implement HTTP provider. Guard rỗng key, JSON schema mismatch, HTTP error → trả về empty list + error string, không crash |
| `NewsCacheStore.cs` | Lưu/load JSON cache (`loop6_news_cache.json`). Kiểm tra stale |
| `NewsForceCloseTracker.cs` | Track event nào đã trigger force-close → tránh duplicate |
| `NewsFilterService.cs` | **Core orchestrator.** Fetch → filter CPI/NFP → quyết định block/force-close. Fail-safe modes. Test helpers (`InjectEventsForTest`, `SimulateStaleCache`, `SimulateApiError`) |
| **`HistoricalNewsCsvLoader.cs`** _(Patch mới)_ | Load CSV lịch sử. `EnsureSampleCsvExists()` tự tạo file nếu chưa có. 132 entries (66 NFP + 66 CPI) từ 2021–2026 |

### 2.2 Tests — `tests/TradeAlert.BacktestRobot.Tests/News/`

| File | Số tests | Nội dung |
|---|---|---|
| `NewsCategoryTests.cs` | 8 | Classifier CPI/NFP/Other |
| `AffectedSymbolTests.cs` | 10 | Logic symbol bị ảnh hưởng USD news |
| `NewsFilterServiceTests.cs` | 12 | Block entry theo time window |
| `ForceCloseTests.cs` | 8 | Force-close timing |
| `FailSafeTests.cs` | 8 | Fail-safe mode khi API lỗi / cache stale |
| `NewsCacheStoreTests.cs` | 6 | Save/load/stale JSON cache |
| **`HistoricalNewsCsvLoaderTests.cs`** _(Patch mới)_ | 12 | CSV parse, backtest simulation |

**Tổng tests:** 279 passed · 0 failed · 0 skipped

---

## 3. FILES ĐÃ SỬA ĐỔI

### 3.1 `Loop6BacktestTradingBot.cs`

| Thay đổi | Chi tiết |
|---|---|
| `[Robot]` AccessRights | `None` → `FullAccess` (cần cho file I/O + optional HTTP). Comment giải thích rõ lý do |
| `using` | Thêm `using TradeAlert.BacktestRobot.Execution.News;` |
| **Group "12b. News Filter"** | 29 parameters mới (xem mục 5) |
| Runtime fields | `_newsFilter`, `_newsForceCloseTracker` |
| `OnStartCore()` | Gọi `InitNewsFilter()` |
| `InitNewsFilter()` | Build config → tạo cache store, provider, service → load cache → refresh nếu được cấu hình |
| `OnTimer()` | Gọi `RefreshIfNeeded` + `CheckAndExecuteNewsForceClose` mỗi 60s (live) |
| **`OnTick()`** _(Patch mới)_ | Force-close check mỗi tick — fallback cho backtest vì OnTimer không chạy trong backtest |
| `OnBar()` | Gọi `RefreshIfNeeded` + `CheckAndExecuteNewsForceClose` + `IsBlockedByNews()` trước khi build entry |
| `CheckAndExecuteNewsForceClose()` | Dùng `NewsForceCloseTracker` tránh duplicate; gọi `CloseAndCancelForSymbol` _(xem mục 3.2)_ |
| `IsBlockedByNews()` | Log `NEWS_BLOCK_CPI` / `NEWS_BLOCK_NFP` vào CSV |

**Triple guard cho HTTP** trong `TryFetchFromApi`:
```
EnableNewsFilter=true AND NewsApiKey≠"" AND BacktestMode≠CacheOnly
```
→ Default backtest (CacheOnly + empty key) = **không bao giờ gọi internet**.

### 3.2 `TradeExecutionService.cs`

Thêm method:
```csharp
public (int PositionsClosed, int PendingsCancelled) CloseAndCancelForSymbol(string symbolName, string why)
```
- Verify `symbolName == _symbolName` trước khi làm gì (early return nếu sai symbol)
- Chỉ iterate `OurPositions()` / `OurPendingOrders()` — đã filter bởi `LabelPrefix + SymbolName`
- **Không bao giờ đụng lệnh thủ công hoặc robot khác**
- Trả về `(positionsClosed, pendingsCancelled)` để log chính xác

### 3.3 `Loop6SkipReasonCodes.cs`

Thêm constants:

| Constant | Giá trị |
|---|---|
| `NewsBlockCpi` | `"NEWS_BLOCK_CPI"` |
| `NewsBlockNfp` | `"NEWS_BLOCK_NFP"` |
| `NewsForceClose` | `"NEWS_FORCE_CLOSE"` |
| `NewsForceCloseCpi` | `"NEWS_FORCE_CLOSE_CPI"` |
| `NewsForceCloseNfp` | `"NEWS_FORCE_CLOSE_NFP"` |
| `NewsApiError` | `"NEWS_API_ERROR"` |
| `NewsCacheStale` | `"NEWS_CACHE_STALE"` |

### 3.4 `Loop6DetailedCsvLogger.cs`

- Thêm `_newsActionsPath` → file `loop6_news_actions.csv`
- Thêm `LogNewsAction(...)` để ghi refresh / block / force-close / error
- `MapCloseBucket`: `NEWS_FORCE_CLOSE*` → bucket `"NEWS_FORCE_CLOSE"`

### 3.5 `NewsEvent.cs` _(Patch mới)_

Thêm 3 override fields (nullable):
```csharp
public double? BlockBeforeMinOverride      { get; init; }
public double? BlockAfterMinOverride       { get; init; }
public double? ForceCloseBeforeMinOverride { get; init; }
```
`null` = dùng `NewsFilterConfig` default. Set bởi `HistoricalNewsCsvLoader` khi CSV có giá trị ≥ 0.

### 3.6 `NewsFilterService.cs` _(Patch mới — nhiều chỗ)_

| Thay đổi | Mô tả |
|---|---|
| `LoadCacheOnStart()` | Thử JSON cache trước → nếu không có, fallback sang historical CSV. `EnsureSampleCsvExists` tự tạo file nếu chưa có |
| `CheckEventsForBlock()` | Dùng `ev.BlockBeforeMinOverride ?? cfg.BlockBeforeHighImpactMin` và `ev.BlockAfterMinOverride` thay vì hardcode config |
| `CheckForceCloseRequired()` | Dùng `ev.ForceCloseBeforeMinOverride ?? cfg.ForceCloseBeforeNewsMin` |
| `TryFetchFromApi()` | Thêm triple guard: `EnableNewsFilter && ApiKey != "" && BacktestMode != CacheOnly` |

---

## 4. HISTORICAL NEWS CACHE

### File tự động tạo:
```
C:\Users\{user}\Documents\cAlgo\Data\Loop6News\loop6_historical_news_cache.csv
```

### Format CSV:
```
event_name,currency,event_time_utc,block_before_min,force_close_before_min,block_after_min,source
Nonfarm Payrolls,USD,2024-06-07T13:30:00Z,-1,-1,-1,historical_manual
CPI m/m,USD,2024-04-10T13:30:00Z,-1,-1,-1,historical_manual
```

### Nội dung mẫu:
- **66 entries NFP** — first Friday mỗi tháng, 13:30 UTC (08:30 ET), từ Jan/2021 → Jun/2026
- **66 entries CPI** — khoảng tuần 2 mỗi tháng, 13:30 UTC, từ Jan/2021 → Jun/2026
- `-1` = dùng config defaults tại runtime (`BlockBeforeHighImpactMin` / `ForceCloseBeforeNewsMin` / `BlockAfterHighImpactMin`)

### Logic ưu tiên khi load:
```
1. JSON cache (loop6_news_cache.json)   ← từ API refresh
       ↓ (nếu không có)
2. Historical CSV (loop6_historical_news_cache.csv)  ← auto-created, offline
       ↓ (nếu cả hai đều không có)
3. Fail-safe mode (ContinueWithWarning / BlockAllAffectedSymbols)
```

---

## 5. PARAMETERS MỚI (Group "12b. News Filter")

| Parameter | Default | Mô tả |
|---|---|---|
| `EnableNewsFilter` | `false` | Master switch |
| `NewsProvider` | `"TradingEconomics"` | Provider name |
| `NewsApiKey` | `""` | API key (để rỗng = không gọi HTTP) |
| `BlockEventKeywords` | `"NFP,Nonfarm,CPI,Consumer Price"` | Keywords để nhận diện |
| `BlockCurrencies` | `"USD"` | Currencies cần block |
| `BlockUsdNewsSymbols` | `"EURUSD,GBPUSD,XAUUSD,USDJPY,USDCAD,USDCHF,AUDUSD,NZDUSD"` | Symbols bị ảnh hưởng |
| `EnableConservativeCrossBlock` | `false` | Block thêm cross pairs (EURJPY...) |
| `ConservativeCrossBlockSymbols` | `"EURJPY,GBPJPY,AUDJPY,CADJPY"` | Cross pairs |
| `BlockBeforeHighImpactMin` | `60` | Chặn entry trước N phút |
| `BlockAfterHighImpactMin` | `30` | Chặn entry sau N phút |
| `EnableNewsForceClose` | `true` | Bật force-close |
| `ForceCloseBeforeNewsMin` | `15` | Force-close trước N phút |
| `CancelPendingBeforeNewsMin` | `15` | Cancel pending trước N phút |
| `NewsBacktestMode` | `"CacheOnly"` | `CacheOnly` / `ApiIfAvailable` / `ApiOnly` |
| `NewsFailSafeMode` | `"ContinueWithWarning"` | Hành động khi không có data |
| `NewsRefreshOnStart` | `true` | Gọi API khi bot start |
| `NewsRefreshOnTimer` | `true` | Gọi API định kỳ |
| `NewsRefreshIntervalHours` | `6` | Chu kỳ refresh (giờ) |
| `NewsLookaheadDays` | `7` | Nhìn trước bao nhiêu ngày |
| `NewsCacheMaxAgeHours` | `24` | Cache quá tuổi thì coi là stale |
| `FailSafeFridayBlockStartBangkok` | `"19:00"` | Bắt đầu block Friday safety (BKK) |
| `FailSafeFridayBlockEndBangkok` | `"22:00"` | Kết thúc block Friday safety (BKK) |

---

## 6. TIMING CỦA FORCE-CLOSE

| Path | Khi nào chạy | Mục đích |
|---|---|---|
| `OnTimer()` — 60s | **Live trading** | Precision cao, không phụ thuộc bar |
| `OnTick()` | **Backtest** (OnTimer không hoạt động trong backtest) | Fallback tick-level |
| `OnBar()` | Cả hai (last resort) | Đảm bảo cũng check khi bar đóng |

`NewsForceCloseTracker` đảm bảo mỗi event chỉ trigger force-close **một lần** dù cả 3 path đều kích hoạt.

---

## 7. SAFETY GUARANTEES

| Điều kiện | Đảm bảo |
|---|---|
| `EnableNewsFilter = false` | Bot hoạt động **y hệt** bản cũ, không block, không close |
| `NewsApiKey = ""` (default) | **Không gọi HTTP** dù `EnableNewsFilter = true` |
| `NewsBacktestMode = CacheOnly` (default) | **Không gọi HTTP** trong mọi trường hợp |
| Force-close | Chỉ đụng position/pending của **bot này** (filter LabelPrefix + SymbolName) |
| Lệnh thủ công | **Không bao giờ** bị đụng (không có LabelPrefix của bot) |
| Robot khác | **Không bao giờ** bị đụng (LabelPrefix khác) |
| API lỗi / parse lỗi | Trả về empty events + error string, **không crash** |
| Cache không có | Auto-fallback sang historical CSV → auto-tạo file nếu chưa có |

---

## 8. KẾT QUẢ BUILD & TEST

```
Build:  0 errors · 6 warnings (pre-existing, không liên quan)
Tests:  279 passed · 0 failed · 0 skipped
Deploy: C:\Users\example\Documents\cAlgo\Sources\Robots\TradeAlert.BacktestRobot.algo
        Size: 262,640 bytes
```

### Breakdown tests News Filter (52 tests):

| Test class | Tests | Kết quả |
|---|---|---|
| `NewsCategoryTests` | 8 | ✅ PASS |
| `AffectedSymbolTests` | 10 | ✅ PASS |
| `NewsFilterServiceTests` | 12 | ✅ PASS |
| `ForceCloseTests` | 8 | ✅ PASS |
| `FailSafeTests` | 8 | ✅ PASS |
| `NewsCacheStoreTests` | 6 | ✅ PASS |
| `HistoricalNewsCsvLoaderTests` | 6 | ✅ PASS |
| `BacktestEntryBlockTests` | 6 | ✅ PASS |

---

## 9. HƯỚNG DẪN KIỂM TRA NHANH

### A. Kiểm tra historical CSV được tạo tự động
1. Chạy bot lần đầu với `EnableNewsFilter = true`
2. Kiểm tra file: `Documents\cAlgo\Data\Loop6News\loop6_historical_news_cache.csv`
3. Phải thấy 132+ rows (NFP + CPI từ 2021–2026)

### B. Kiểm tra backtest block entry
1. Backtest khung H1 hoặc M15
2. Chọn ngày bất kỳ có NFP (vd: 07/06/2024)
3. Set `EnableNewsFilter = true`, `EnableNewsForceClose = true`
4. Kỳ vọng: log `[NEWS] ENTRY BLOCKED (NEWS_BLOCK_NFP)` quanh 13:30 UTC ngày đó

### C. Kiểm tra force-close không đụng lệnh thủ công
1. Mở lệnh thủ công trên EURUSD
2. Để bot chạy sát giờ NFP
3. Kỳ vọng: lệnh thủ công **không bị đóng**

### D. Kiểm tra `EnableNewsFilter = false`
1. Set `EnableNewsFilter = false`
2. Backtest hoặc chạy live qua ngày NFP
3. Kỳ vọng: **không có bất kỳ thay đổi nào** so với bản bot cũ

---

## 10. FILES ĐƯỢC TẠO / SỬA — TỔNG HỢP

```
src/TradeAlert.BacktestRobot/
  Execution/News/
    NewsEventCategory.cs                ← MỚI
    NewsEvent.cs                        ← MỚI (+override fields)
    NewsBlockDecision.cs                ← MỚI
    NewsFilterConfig.cs                 ← MỚI
    INewsCalendarProvider.cs            ← MỚI
    TradingEconomicsNewsProvider.cs     ← MỚI
    NewsCacheStore.cs                   ← MỚI
    NewsFilterService.cs                ← MỚI (+historical CSV fallback, per-event overrides, triple guard)
    NewsForceCloseTracker.cs            ← MỚI
    HistoricalNewsCsvLoader.cs          ← MỚI (Patch)
  Execution/
    TradeExecutionService.cs            ← SỬA (thêm CloseAndCancelForSymbol)
  Execution/Analytics/
    Loop6SkipReasonCodes.cs             ← SỬA (thêm NEWS_* constants)
    Loop6DetailedCsvLogger.cs           ← SỬA (thêm LogNewsAction, news_actions.csv)
  Loop6BacktestTradingBot.cs            ← SỬA (29 params, OnTick, OnTimer, init, block, force-close)

tests/TradeAlert.BacktestRobot.Tests/News/
  NewsCategoryTests.cs                  ← MỚI
  AffectedSymbolTests.cs                ← MỚI
  NewsFilterServiceTests.cs             ← MỚI
  ForceCloseTests.cs                    ← MỚI
  FailSafeTests.cs                      ← MỚI
  NewsCacheStoreTests.cs                ← MỚI
  HistoricalNewsCsvLoaderTests.cs       ← MỚI (Patch)
```

---

*Report generated: 18/06/2026 12:21 UTC+7*
