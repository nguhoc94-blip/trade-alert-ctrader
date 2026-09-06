# TradeAlert — cTrader Indicator & Backtest Robot

TradeAlert is a C#/.NET system for the [cTrader](https://ctrader.com/) platform (cAlgo Automate API). It detects trading signals based on **order blocks**, **key levels**, and **swing structure**, with multi-timeframe (MTF) compound alert support.

> **Portfolio note:** This repository is a curated excerpt for CV/portfolio review. Real backtest exports (`.xlsx`, `.zip`, logs) and live account data have been removed. Core signal logic is included to demonstrate engineering capability.

## Architecture

| Project | Role |
|---------|------|
| **TradeAlert.Core** | Shared models, engines, and alert pipeline (platform-agnostic) |
| **TradeAlert.Engines** | Additional engine utilities |
| **TradeAlert.Indicator** | Real-time chart indicator (`TradeAlertLoop6Host`) via cTrader Automate |
| **TradeAlert.BacktestRobot** | Backtest cBot with detailed execution rules (entry/exit, lot sizing, news filter) |
| **TradeAlert.Robot** | Multi-symbol scanner with optional email notifications |
| **KlEntryLotIndicator** | Standalone lot-sizing indicator (Bid spread / FTMO sizing) |

### Test coverage

- `TradeAlert.Core.Tests` — pivot/OB/key-level engines, alert pipeline, MTF timing
- `TradeAlert.BacktestRobot.Tests` — execution rules, swing gates, news filter, CSV analytics
- `KlEntryLot.Tests` — position sizing calculator

## Tech stack

- **Language:** C#
- **Runtime:** .NET 6
- **Platform API:** cTrader Automate (`cTrader.Automate` NuGet, cAlgo.API)
- **Build:** MSBuild / `dotnet` CLI

## Getting started

### Prerequisites

- [.NET 6 SDK](https://dotnet.microsoft.com/download/dotnet/6.0)
- Visual Studio 2022 or VS Code (optional)
- cTrader Desktop with Automate enabled (for deploying indicators/cBots)

### Build & test

```powershell
dotnet restore TradeAlert.sln
dotnet build TradeAlert.sln --configuration Release
dotnet test TradeAlert.sln
```

Expected: solution builds with 0 errors; unit tests pass (71+ tests at Loop 6 baseline).

### Deploy to cTrader

1. Build the indicator or cBot project, or open `TradeAlert.sln` in cTrader Automate.
2. For the indicator, add **TradeAlertLoop6Host** to a chart (M1–D1 supported).
3. For backtesting, attach **Loop6BacktestTradingBot** to an M15 chart.

See `docs/HUONG_DAN_DEPLOY_VA_SETUP_ALERT.md` for alert configuration and `docs/smoke/Loop6_Smoke_Runbook.md` for QA smoke steps.

## Documentation

- `docs/mapping/` — Pine Script → C# mapping specs (OB, key level, swing rules, MTF timing)
- `docs/parity/` — Parity report schema and sample fixtures
- `docs/qa/` — QA handoff checklist and bundle index
- `src/TradeAlert.BacktestRobot/RUNBOOK.md` — Backtest robot execution flow

## What is not included

- Historical backtest result files (`.xlsx`, `.zip`, `log.txt`)
- Live broker credentials, API keys, or account identifiers
- Pine Script source archives

Configure news API keys and email settings at runtime via cBot parameters — no secrets are stored in source.

## License

All Rights Reserved — portfolio viewing only. See [LICENSE](LICENSE).
