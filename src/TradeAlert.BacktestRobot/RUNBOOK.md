# Loop6 Backtest Trading cBot — Strategy Tester Runbook

`Loop6BacktestTradingBot` is a **separate algo** from `Loop6MultiSymbolEmailScanner` (cTrader
requires one algo type per assembly, so it lives in its own project `TradeAlert.BacktestRobot`).
It reuses `PerSymbolSignalHost` to fire the **same** R1–R6 compound signals as the scanner, then
places **limit** orders with SL/TP derived from swing-B keylevel / OB geometry and KlEntryLot FTMO
sizing.

## 0. Build & deploy

```powershell
dotnet build "TradeAlert.sln" -c Release
```

The build copies `TradeAlert.BacktestRobot.algo` to
`%USERPROFILE%\Documents\cAlgo\Sources\Robots\`. In cTrader: **Automate → cBots** → the bot appears
as *Loop6BacktestTradingBot*. (The scanner still deploys separately as `src.algo`.)

## 1. Chart / instrument

- Open an **M15** chart of the symbol under test. Rule 1 reads the M15 active swing from the chart
  engine, so the bot logs a warning and may misbehave on non-M15 charts.
- Validation symbols: **XAUUSD, AUDCAD, EURGBP, GERMANY40**.

## 2. Dry-run first (parity check, no orders)

1. Add the bot to the chart / Strategy Tester with **`01. Backtest → Enable Trading = false`**,
   `Enable Log = true`, `Print compound fires = true`.
2. Keep all engine params (groups 05–09) at defaults — they mirror the scanner.
3. Run the Strategy Tester over the desired window (use **Warmup Bars ≈ 3000**).
4. In the **Log** tab, each fire prints either:
   - `FIRE R# DIR bar=… -> PLAN BUY entry(KL|OB)=… sl=… tp=… w=… rr=… lotFtmo=…`, or
   - `FIRE R# DIR bar=… -> SKIP (reason)` (gate/keylevel/sizing reasons).
5. **Compare** these `FIRE` lines against the scanner / email alerts for the same symbol+window —
   slot, direction and bar index must match (signal parity). Minor visual-indicator timing drift is
   expected and accepted (host uses realtime flags, matching the scanner not the visual backtest).

## 3. Sizing parameters per symbol (group 10)

| Symbol     | Asset type | Quote | Conv (USD per quote)            | Contract size |
|------------|-----------|-------|----------------------------------|---------------|
| XAUUSD     | XAUUSD    | USD   | 1 (auto)                         | 100           |
| AUDCAD     | Forex     | CAD   | set `Manual conv` ≈ 1/USDCAD, or leave 0 for auto lookup | 100000 |
| EURGBP     | Forex     | GBP   | set `Manual conv` ≈ GBPUSD, or 0 for auto | 100000 |
| GERMANY40  | Custom    | EUR   | set `Manual conv` ≈ EURUSD       | `Custom contract size` (e.g. 1) |

- `Manual conv USD per quote = 0` → the bot tries to read `{QUOTE}USD` / `USD{QUOTE}` live; if that
  symbol is unavailable in the tester, set the value manually, otherwise no lot is computed and the
  trade is skipped (logged as `no lot`).
- `FTMO Account Balance`, `Risk %` drive the FTMO lot branch; full volume is used (no half lot).

## 4. Live (EnableTrading = true) smoke test

1. Set **`Enable Trading = true`**. Start with **one** symbol (XAUUSD recommended).
2. Verify in **Positions / History / Pending Orders**:
   - Limit orders appear at the computed entry; SL/TP attached (`SL=…p TP=…p` in log).
   - **Exit**: a position/pending closes when swing B becomes broken/broken-done
     (`CLOSE … (B-broken)`).
   - **Flip**: an opposite valid fire closes the opposite exposure first (`FLIP-CLOSE/FLIP-CANCEL`)
     then opens the new side (requires `Allow flip = true`).
   - **Parallel**: with `Allow parallel = true`, multiple different setups coexist; the same
     `(R#, swing B bar)` is never entered twice (`dedup skip`).
   - **Session**: after the stop hour (default **3:00 VN → 20:00 UTC**) everything is flattened
     (`CLOSE-ALL (session-stop)`) and no new entries open until the start hour
     (default **6:00 VN → 23:00 UTC**).
3. Cross-check the produced lot against the KlEntryLot indicator for the same entry/SL — they should
   agree (both use the FTMO branch).
4. Repeat for AUDCAD, EURGBP, GERMANY40 with their sizing params.

## 5. Key parameter groups

- **01. Backtest** — EnableTrading (dry-run switch), WarmupBars, EnableLog.
- **02. Signal / 03. Compound Rules** — direction filter + R1–R6 presets, sync mode, event window.
- **05–09. Engine** — pivot / filter / OB / keylevel settings (defaults mirror the scanner).
- **10. Trade/Risk** — FTMO balance, Risk %, Reward:Risk (1.5), SL width mult (2), asset/quote/conv.
- **11. Exit/Session** — VN start/stop hours, flip, parallel, session toggle.
- **12. Spread** — 0 = broker spread (`Symbol.Spread`), else fixed pips override.

## Notes / limits (v1)

- Engine logic is untouched — only a read-only `PerSymbolSignalHost.State` accessor was added.
- Single symbol per run (Strategy Tester is one chart); M15 chart required.
- One TP (RR), full volume; no partial / trailing / second TP.
