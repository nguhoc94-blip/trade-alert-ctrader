# Loop 5 — Smoke runbook (build / test / scope)

## Prerequisites

- .NET 6 SDK on PATH.

## A. Build

From repository root, the solution lives under `code_cTrader/code cTrader`:

```powershell
cd "code_cTrader\code cTrader"
dotnet build
```

Expected: **0 errors** for `TradeAlert.Core`, `TradeAlert.Indicator`, `TradeAlert.Core.Tests`.

## B. Tests (Core + Loop 5 wiring)

```powershell
dotnet test
```

Expected: all tests **Passed** (includes `ChartTimeFromBarsTests`, `SeriesBufferSyncTests`, `AlertPipelineHostWiringTests`, `M15EdgeIndicatorIntegrationTests`, `TradeAlertIndicatorShellTests`).

## C. Attach / platform feed (when cTrader SDK present)

- Not exercised in CI: add indicator project with official SDK references, implement `Calculate` → call `TradeAlertIndicator` host methods.
- Without SDK: static review + unit tests above substitute for attach.

## D. Log / duplicate guard

- Inspect `AlertPipelineHost.LogEntries` after `EvaluateRecordAndMaybeFire` with **`appendLog: true`** (mặc định host production dùng `false` và chỉ batched khi Loop6 Debug throttle).
- Session reload: call `OnStopOrReload` (or `AlertPipelineHost.ResetSession` + series reset); duplicate store must not silently evict—explicit reset only.

## E. Scope check (negative)

From `code cTrader` root:

```powershell
Select-String -Path "src\TradeAlert.Indicator\*.cs" -Pattern "cBot|backtest|PlaceOrder|Positions|AutoTrade|ForceM15Edge" -SimpleMatch -CaseSensitive:$false
```

Expected: **no matches** in Indicator sources (production `ForceM15Edge` forbidden).

## F. Mapping doc

Confirm `docs/mapping/Loop5_Indicator_Integration.md` reflects implemented vs carry-over items.
