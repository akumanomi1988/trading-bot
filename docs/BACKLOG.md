# BotTrading — accepted scope, engineering backlog and continuation checkpoint

**Last engineering checkpoint:** 2026-10-28. **Repository:** `akumanomi1988/trading-bot`; **branch:** `dev`.
**Latest verified Windows CI:** `d6cde5ef` — 106/106 C# assertions, 14/14 TypeScript tests, React typecheck/build, Windows native publish and real WebView2 E2E (2 synthetic assets, EMA backtest, simulated BUY, captured PNG, denied cross-origin navigation) all PASS: https://github.com/akumanomi1988/trading-bot/actions/runs/40782127144.

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
- [ ] Extend Windows WebView2 E2E with permission denial, native dialogs, keyboard focus and restart persistence.
- [x] Actual external-origin navigation attempt in Windows WebView2 E2E verifies native cancellation and unchanged app origin; PASS in CI #40782127144.
- [x] End-to-end simulated market fixture (two independent 420-candle symbols), React compare view, UI-selected EMA backtest, UI simulated buy and C# paper-account reconciliation verified in native Windows CI #40781777398; no broker access or production data.
- [x] Native Windows WebView2 smoke: isolated workspace/profile, actual bundled React DOM, correct virtual origin, bridge bootstrap messages, PNG preview and clean exit verified on CI #40780136513. Expanded smoke includes strategy tab, EMA selection and paper portfolio; CI #40780519212 PASS.
- [ ] Verify live Stooq fetch and commercial licensing directly with the current provider; automated access appears restricted in 2026. A legally usable free commercial fallback still needs evaluation.
- [x] Add optional transient (never persisted) Stooq API-key query parameter, strict key validation, HTML-challenge and access-denial errors; deterministic HTTP tests. CI verification pending.
- [x] Deterministic HTTP contract tests for Stooq origin, redirects, rate limiting, oversized payloads, invalid symbols and malformed bodies; no provider calls in automated tests. Automated Windows CI PASS.
- [ ] Model market calendars and corporate actions; prevent split-induced false strategy performance and verify adjusted/raw normalization.
- [x] Persist validated historical source/provenance (user CSV vs Stooq, UNKNOWN adjustment, unknown currency/venue); surface uncertainty in data-quality UI without guessing ticker denomination. Automated CI verified; native manual acceptance still pending.
- [x] Let users explicitly declare instrument currency, venue and adjusted/raw status without altering candles; persist atomically and reject unknown or mixed currencies in the new two-asset portfolio backtest. No automated currency/venue verification or FX model exists; full currency lifecycle remains open.
- [ ] Strengthen workspace interruption and fault injection further.
- [x] Workspace schema v1 gate rejects unknown future schemas without rewriting data; legacy v1 experiments with missing strategy kind are normalized to SMA on read and preserved by the next atomic save. Automated CI PASS; native manual QA remains.
- [x] Native JSON backup/export and validated atomic restore, with pre-restore recovery copy and corruption tests; Windows UI acceptance remains pending.
- [ ] Perform reproducible 25 symbols × 100k candles memory, CPU and render profiling; optimize WebView2 full-series IPC/retained JS memory before acceptance.
- [x] Stream workspace JSON deserialize/serialize directly from/to files with flush-before-atomic-replace and 512 MiB postwrite cap, instead of materializing a second entire JSON string; 30k-candle regression roundtrip. CI verification pending.
- [x] Frontend bounded O(n) extrema-preserving chart rendering (maximum 1,200 SVG vertices), constant-memory shared min/max, original index/date X alignment and deterministic 100k-point fixtures, CI #40781565753 PASS; does not solve full-history IPC memory overhead.

### Priority 1 — originally planned product scope
- [ ] Additional portfolio-level risk controls, cross-asset execution and portfolio rebalancing.
- [x] Quick paper account supports multiple holdings only with user-verified common denomination, adjusted prices and synchronized latest date; account currency remains fixed after liquidation, reset required to switch, legacy unknown accounts remain single-symbol. Workspace rejects mixed-currency holding snapshots. CI verification pending.
- [x] Configurable 1–100% capital-allocation limit per SMA/EMA entry, enforced on the next-open execution budget and threaded through optimizer, chronological validation, cost sensitivity and saved-experiment comparison. Windows CI verified (latest #40781777398).
- [x] Configurable 0–1000 bps fees and slippage in strategy UI, optimization, chronological validation and experiment archives; exact cost hypotheses recorded and used for like-for-like comparison. Windows CI verified (latest #40781777398).
- [x] Comparable saved-experiment research: compare two SMA/EMA experiments only when source fingerprint, sample period, capital and transaction costs coincide; return, benchmark, drawdown and trade-count deltas in native/React UI with explicit restrictions. Fresh CI and manual acceptance pending.
- [x] EMA crossover integrated as a second signal family alongside SMA in backtesting, parameter optimization, out-of-sample and expanding walk-forward validation, cost stress, native bridge, frontend and persistent experiment snapshots. Automated CI PASS; broader native manual QA remains.
- [x] Version-1 saved SMA experiment snapshots with SHA-256 dataset fingerprint, stale-data detection, 100-record cap, export/restore compatibility and persistence tests; Automated CI PASS; native manual QA remains.
- [x] Descriptive daily equity risk metrics (annualized volatility, Sharpe, invested-session exposure, worst session) integrated into backtest DTO, Windows UI and deterministic core assertions; Automated CI verified; native manual acceptance still pending.
- [ ] Statistical robustness beyond 4 folds: perturbation maps, synthetic regimes and outlier handling.
- [x] Six-level transaction-cost sensitivity comparing historical SMA returns against fee-adjusted passive benchmark, with C#/WebView2/React and regression tests; Automated CI PASS; native manual QA remains.
- [x] Initial two-asset historical portfolio simulation: separate equal-capital SMA/EMA sleeves, bounded common interval, combined equity, risk and benchmark, matching verified currency and user-attested adjusted histories; regression tests. This is NOT cross-asset rebalancing or a full corporate-action-aware portfolio engine.
- [ ] Full multi-asset portfolio execution with rebalancing, cross-asset risk budgets, currencies/FX and corporate-action-aware cash/positions.
- [x] Historical replay trade-ledger reconciliation on persistent load: verify executed OHLC prices, fees, cash, shares and average cost; detect tampered or interrupted snapshots. Automated CI PASS; native manual QA remains.
- [x] Historical replay pending-order cancellation and inclusive optional good-through expiry, persistent terminal statuses, native bridge, UI controls and regression tests; Automated CI PASS; native manual QA remains.
- [ ] News: legal free RSS/API source onboarding with attribution and timestamp integrity, dedupe, historical timestamp alignment and optional strictly offline AI analysis.
- [ ] Automated browser visual regression and real WebView2 automated screenshots across Windows DPI/screen sizes.
- [ ] Signed Windows installer, trusted signing certificate, automatic in-place upgrade and recovery QA. These are not met by an unsigned portable build.
- [x] Versioned self-contained win-x64 portable ZIP, build SHA manifest, SHA-256 verification, GitHub artifact upload and extracted-package native WebView2 smoke; manual distribution guide. CI verification pending.

## Deterministic continuation instructions

1. Open `dev` HEAD and read this file, `docs/ACCEPTANCE.md`, `.github/workflows/ci.yml` and current Windows CI run evidence.
2. Use authorized GitHub connector when raw git push credentials are absent; never force-push over concurrent work or alter `main` without an accepted release gate.
3. Implement the largest coherent open subsystem, integrate application/host/React/storage/tests, and verify with CI; correct any failure before proceeding.
4. Use `./scripts/build.ps1` for developer Windows builds, `npm test` for frontend bridge/plot tests and `dotnet run --project tests/BotTrading.Tests/BotTrading.Tests.csproj -c Release` for native engine checks.
5. Keep a clean separation between implemented code, CI-proven code, real Windows UI acceptance and commercial production readiness. Do not report percentages without approved weighted acceptance evidence.

**Release policy:** no production auto-deploy and no real-money trading. Financial analysis and market data are exploratory; results do not constitute investment advice.
