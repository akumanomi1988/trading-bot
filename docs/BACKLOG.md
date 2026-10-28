# BotTrading — accepted scope, engineering backlog and continuation checkpoint

**Last engineering checkpoint:** 2026-10-28. **Repository:** `akumanomi1988/trading-bot`; **branch:** `dev`.
**Most recent verified Windows CI baseline before this change:** `afbf94bc` — successful React and .NET Windows CI run https://github.com/akumanomi1988/trading-bot/actions/runs/40577874854. Current EMA changes require a new successful CI run.

## Definition of status

- **TESTED_PARTIAL:** production code exists and automated CI tests pass, but at least one milestone acceptance gate remains unsatisfied.
- **TODO:** substantive work is missing or unverified.
- **COMPLETE:** all explicit functionality, automated tests, integration, Windows UI acceptance and relevant release gates for that milestone have passed.

**No full historical phase is COMPLETE.** The original specification exists mainly in conversation history and is not a previously versioned acceptance contract; therefore the weighted overall functional completion percentage cannot be computed reliably. Do not substitute lines of code, commits or CI-test counts for product-completion percentages. Release readiness is not certified. There is no production release.

## Historical scope baseline (weights are not earned completion)

| Phase | Weight | Engineering status | Outstanding acceptance gates |
| --- | ---: | --- | --- |
| Foundation and modular quantitative core | 15% | TESTED_PARTIAL | Windows app smoke, WebView2 permissions/navigation/focus, recovery and fault injection |
| Free historical data and reproducible backtesting | 20% | TESTED_PARTIAL | Live provider E2E, provenance, alternative free feed, calendars, splits/dividends, instrument currencies |
| Visual two-asset comparison and synchronized replay | 15% | TESTED_PARTIAL | Real WebView2 visual E2E, large-series behavior and accessibility |
| Extensible strategy and experiment lab | 15% | TESTED_PARTIAL | Additional strategies, saved/versioned experiments, parameterized costs and portfolio reports |
| Chronological robustness and pattern search | 15% | TESTED_PARTIAL | Sensitivity, walk-forward acceptance with varied regimes, survivorship and selection-bias analysis |
| Persistent simulated trading | 10% | TESTED_PARTIAL | Native end-to-end acceptance, OHLC execution assumptions, cancel/expire flows, instrument currency metadata |
| News, attributed sentiment and optional AI research | 10% | TESTED_PARTIAL | Legally permitted free-source ingest, deep context/history, optional cost-free AI research, native E2E |

## Verified engineering accomplishments

1. .NET 10 WinForms host + WebView2 virtual origin; React/TypeScript main UI; JSON postMessage command allowlist; no broker connection.
2. CSV OHLC import and optional Stooq download; 25 symbol and 100,000 candle limits, atomic workspace storage.
3. Two-asset historical comparison, scale-normalized charting, bounded pattern correlation and cutoff-safe queries.
4. SMA cross next-opening backtest, fees/slippage, passive buy/hold benchmark with identical costs, trade log, drawdown and parameter grid.
5. Chronological 70/30 holdout and four nonoverlapping expanding walk-forward tests, selected only with training data.
6. Quick simulated portfolio plus independent time-stepped paper replay; queued MARKET/LIMIT/STOP orders, next-bar/gap assumptions, rejection, history-rewrite protection and restart recovery.
7. Cutoff-safe historical data diagnostics (possible missing weekdays, zero volume, abrupt moves), explicitly warning that holidays and corporate actions are not adjusted.
8. Locally imported CSV news with publication offsets, source attribution, English/Spanish rule-based polarity and conservative no-same-day look-ahead policy. No premium feeds or generated investment predictions.
9. Automated Windows CI: React and C# assertions, typecheck, frontend build, native publish, 7-day unsigned artifact retention.

## Prioritized remaining engineering work

### Priority 0 — release blockers and data correctness
- [ ] Extend Windows WebView2 E2E beyond startup (permissions, disallowed navigation, dialogs, keyboard focus, replay and persistence).
- [x] CI opt-in native Windows WebView2 smoke: start the published WinForms executable with isolated local workspace/profile; verify real bundled React DOM, correct virtual origin, both .NET bridge bootstrap commands, screenshot and clean exit. Pending CI validation on runner.
- [ ] Verify real Stooq fetch against current provider availability and terms; investigate a legally usable free fallback only if the data contract is adequate.
- [x] Deterministic HTTP contract tests for Stooq origin, redirects, rate limiting, oversized payloads, invalid symbols and malformed bodies; no provider calls in automated tests. Pending Windows CI.
- [ ] Model market calendars and corporate actions; prevent split-induced false strategy performance and verify adjusted/raw normalization.
- [x] Persist validated historical source/provenance (user CSV vs Stooq, UNKNOWN adjustment, unknown currency/venue); surface uncertainty in data-quality UI without guessing ticker denomination. CI validation required.
- [ ] Track instrument currency and trading venue; reject cross-currency aggregates until FX modeling is available.
- [ ] Strengthen workspace interruption and fault injection further.
- [x] Workspace schema v1 gate rejects unknown future schemas without rewriting data; legacy v1 experiments with missing strategy kind are normalized to SMA on read and preserved by the next atomic save. CI validation required.
- [x] Native JSON backup/export and validated atomic restore, with pre-restore recovery copy and corruption tests; Windows UI acceptance remains pending.
- [ ] Perform reproducible 25 symbols × 100k candles memory, CPU and render profiling, with downsampling if required.

### Priority 1 — originally planned product scope
- [ ] Risk constraints and portfolio-level multi-run analysis.
- [x] Configurable 0–1000 bps fees and slippage in strategy UI, optimization, chronological validation and experiment archives; exact cost hypotheses recorded and used for like-for-like comparison. Pending latest CI.
- [x] Comparable saved-experiment research: compare two SMA/EMA experiments only when source fingerprint, sample period, capital and transaction costs coincide; return, benchmark, drawdown and trade-count deltas in native/React UI with explicit restrictions. Fresh CI and manual acceptance pending.
- [x] EMA crossover integrated as a second signal family alongside SMA in backtesting, parameter optimization, out-of-sample and expanding walk-forward validation, cost stress, native bridge, frontend and persistent experiment snapshots. Pending fresh CI and native UI acceptance.
- [x] Version-1 saved SMA experiment snapshots with SHA-256 dataset fingerprint, stale-data detection, 100-record cap, export/restore compatibility and persistence tests; CI validation required.
- [x] Descriptive daily equity risk metrics (annualized volatility, Sharpe, invested-session exposure, worst session) integrated into backtest DTO, Windows UI and deterministic core assertions; CI validation required before acceptance.
- [ ] Statistical robustness beyond 4 folds: perturbation maps, synthetic regimes and outlier handling.
- [x] Six-level transaction-cost sensitivity comparing historical SMA returns against fee-adjusted passive benchmark, with C#/WebView2/React and regression tests; CI validation required.
- [ ] Portfolio-level multi-asset backtesting.
- [x] Historical replay trade-ledger reconciliation on persistent load: verify executed OHLC prices, fees, cash, shares and average cost; detect tampered or interrupted snapshots. CI validation required before acceptance.
- [x] Historical replay pending-order cancellation and inclusive optional good-through expiry, persistent terminal statuses, native bridge, UI controls and regression tests; CI validation required before acceptance.
- [ ] News: legal free RSS/API source onboarding with attribution and timestamp integrity, dedupe, historical timestamp alignment and optional strictly offline AI analysis.
- [ ] Automated browser visual regression and real WebView2 automated screenshots across Windows DPI/screen sizes.
- [ ] Signed/versioned Windows installer and in-place upgrade/recovery exercises; production operational guide.

## Deterministic continuation instructions

1. Open `dev` HEAD and read this file, `docs/ACCEPTANCE.md`, `.github/workflows/ci.yml` and current Windows CI run evidence.
2. Use authorized GitHub connector when raw git push credentials are absent; never force-push over concurrent work or alter `main` without an accepted release gate.
3. Implement the largest coherent open subsystem, integrate application/host/React/storage/tests, and verify with CI; correct any failure before proceeding.
4. Use `./scripts/build.ps1` for developer Windows builds, `npm test` for frontend bridge/plot tests and `dotnet run --project tests/BotTrading.Tests/BotTrading.Tests.csproj -c Release` for native engine checks.
5. Keep a clean separation between implemented code, CI-proven code, real Windows UI acceptance and commercial production readiness. Do not report percentages without approved weighted acceptance evidence.

**Release policy:** no production auto-deploy and no real-money trading. Financial analysis and market data are exploratory; results do not constitute investment advice.
