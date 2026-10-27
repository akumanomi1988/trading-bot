# BotTrading execution backlog — 2026-10-26

## Evidence and status model

- `SOURCE_PRESENT_UNVERIFIED`: code exists in this local project, but no Windows .NET + React + WebView2 acceptance run has proven it.
- `TESTED_PARTIAL`: an independent subset of an explicit acceptance gate has been executed successfully.
- `TODO`: meaningful engineering or release work remains.
- `COMPLETE`: all milestone acceptance criteria are satisfied, with recorded test results.

No milestone is COMPLETE. Any global percentage would be speculative while the original BotTrading repo is unavailable and the integrated Windows build remains unverified. The **accepted production scope is 0% verified**, not a claim that no code exists. Reconcile requirements with the original spec/repo before reporting a weighted percentage as final.

## Seven historical phases / weighted scope baseline

Weights reflect estimated functional cost and acceptance burden, not source lines. Weights sum to 100.

| Phase | Weight | Source status | Gate for COMPLETE |
|---|---:|---|---|
| 1. Foundation / Quant Core | 15% | SOURCE_PRESENT_UNVERIFIED | Validated native build, structured analytics services, deterministic unit tests, workspace recovery, error handling |
| 2. Historical Data / Backtesting | 20% | SOURCE_PRESENT_UNVERIFIED | Multiple sources, corporate actions, reliable reproducible backtests, provenance, E2E and performance |
| 3. Visual Replay / Dual Asset Comparison | 15% | SOURCE_PRESENT_UNVERIFIED | WinForms/WebView2 + React E2E, date alignment, responsive charts, replay, persistent workspace |
| 4. Strategy Lab / Experiment Engine | 15% | SOURCE_PRESENT_UNVERIFIED | Extensible strategies, experiment records, parameter search, reproducibility, acceptance gates |
| 5. Robustness / Pattern Explorer | 15% | SOURCE_PRESENT_UNVERIFIED | Multi-asset matches, chronological out-of-sample, multi-fold walk-forward, sensitivity/stability, leakage tests |
| 6. Paper Trading | 10% | SOURCE_PRESENT_UNVERIFIED | Market-driven clock, order states/fills, session recovery, fees, P/L, rejection E2E; no real broker required |
| 7. News / Sentiment / Optional AI Research | 10% | TODO | Free legally usable ingestion, attribution, historical alignment, safe and budget-free optional AI workflow |

## Immediate critical engineering sequence

- [x] Locate connected GitHub repositories, confirm push permissions on the personal account; Remote `akumanomi1988/trading-bot` established; initial repository was empty.
- [x] Create isolated writable workspace and initialize C#/.NET, WinForms, WebView2 and React source structure.
- [x] Core OHLC and market series validation, SMA/EMA, next-bar execution, fees/slippage, drawdown, normalized multi-asset search.
- [x] Daily CSV import, restricted Stooq historical fetch, atomic local workspace persistence, simple paper account.
- [x] Native postMessage bridge, dual charts, historical cutoff, strategy and paper UI.
- [x] Fixed chronological train/test robustness holdout without choosing parameters on test observations.
- [x] Eleven actual TypeScript chart/IPC-client tests executed and passed (2026-10-26).
- [x] Windows .NET 10 CI builds the core and runs 31/31 console tests successfully at commit `993d51ab` (2026-10-27).
- [x] GitHub CI provisions locked npm dependencies, passes 11/11 UI tests, React typecheck and production Vite build at commit `993d51ab`.
- [ ] Execute real WebView2 smoke and native OpenFileDialog import test on Windows; verify CSP, navigation isolation, no remote HTTP API.
- [ ] Exercise actual Stooq data download when the provider is reachable; verify symbol rules, rate limits and error recovery. Add second free provider/fallback if legally available.
- [ ] Expand fault injection for malformed workspace JSON, atomic save rollback, reimport while analytics runs, large files and concurrent operations. Paper-fill integrity and snapshot consistency assertions are in development.
- [ ] Add calendar alignment & corporate action handling, plus missing-data diagnostics and data provenance.
- [ ] Extend strategy model and add parameter range configuration, experiment save/load, trade-level reports and costs by instrument.
- [x] Four-fold expanding-window walk-forward, non-overlapping test periods and future-leakage assertions, accepted on Windows CI `993d51ab`.
- [ ] Add perturbation/stability maps and stronger evidence against overfitting.
- [x] Implement historical time-driven paper replay: queued orders, next-open fills, rejection states, chronological marks, persistent cursor, and history-rewrite protection. Windows CI acceptance pending.
- [ ] Extend replay with conditional/limit orders, scheduled fills and advanced execution assumptions.
- [ ] News/sentiment/AI research using no-cost sources/tools only, with strict provenance, no proprietary scraping and explicit user control.
- [ ] End-to-end native UI tests (keyboard, focus, cancellation, disconnects, restarting, thousands of bars), responsive and accessibility pass.
- [ ] Performance profile: 25 instruments × up to 100k candles; optimize persistence/render limits where necessary.
- [ ] Signed/versioned Windows installer, upgrade migration plan, crash diagnostics, release dry run and user acceptance.
- [ ] Review the historical seven-phase specification with the now-established official repository; the new repository was initially empty.

## Deterministic continuation checkpoint

**Repository:** `https://github.com/akumanomi1988/trading-bot`; active development branch `dev`.

**Entry points:** `src/BotTrading.Desktop/WorkstationForm.cs` (IPC), `src/BotTrading.Application/Workstation.cs` (application facade), `src/BotTrading.Core/Backtester.cs` + `PatternFinder.cs` (quant engine), `frontend/src/App.tsx` (UI), `tests/BotTrading.Tests/Program.cs` (C# assertions).

**Do this first on Windows:** Run `./scripts/build.ps1` with .NET 10, Node 22+, npm and WebView2 installed. Inspect and fix genuine compiler/test failures. Then load a small CSV through the **UI**, compare two symbols, move replay and check future-data exclusion, run in/out-of-sample results, place paper buy/sell, restart, verify persistence. Do not publish or claim 100% until all gates are proven.

**Release policy:** develop/test locally, commit meaningful milestones, do not deploy on each iteration. Production release requires separate authorization and actual E2E evidence.
