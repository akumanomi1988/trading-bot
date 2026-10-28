# BotTrading — Quant Workstation

An offline-first, Windows desktop quantitative research environment. WinForms owns the native process; WebView2 hosts React/TypeScript; the browser exchanges typed messages with the .NET engine. No Electron, Tauri, WPF, external web application, or local HTTP business API.

> **Delivery state (2026-10-26): engineering prototype, NOT production-ready.** This repository was initialized from an independently developed BotTrading prototype and is now hosted at `akumanomi1988/trading-bot`. The original requirements were collected in conversation; there was no prior source repository to reconcile. The Windows build, C# tests, WebView2 bridge and live provider must be verified on a suitable Windows toolchain before any release. No real orders can be sent.

## Architecture

```text
BotTrading.Desktop [Windows, net10.0-windows]
  ├── WinForms application shell, single instance, local WebView2 user-data folder
  ├── WebView2 virtual host: https://bottrading.local (bundled React assets)
  ├── Strictly enumerated JSON command bridge; native CSV picker
  └── BotTrading.Application [net10.0]
        ├── Local workspace JSON storage (atomic replace)
        ├── CSV import and optional HTTPS historical feed (Stooq)
        ├── Paper account (simulated fills, no broker)
        ├── Robustness holdout service
        └── BotTrading.Core [net10.0]
              ├── OHLC validation, SMA/EMA
              ├── Pearson correlation over scale-free returns
              └── SMA crossover backtest (next-bar execution, fees/slippage)

frontend [React 19 / Vite / TypeScript]
  └── WebView2 postMessage -> .NET dispatcher -> structured result
```

The shell maps bundled `frontend/dist` to a local virtual HTTPS hostname; business methods are invoked through `window.chrome.webview.postMessage`. Remote resources are not required by the built UI. Stooq is a **free external provider for historical CSV only**, subject to provider availability and restrictions; data is not guaranteed to be current or accurate. HTTP contract tests exercise a fixed HTTPS origin, disabled redirects, 429 responses, size limits and malformed CSV without contacting Stooq; live-provider acceptance remains separate.

## Functional paths present in source

- Import user-supplied CSV from a native OpenFileDialog; accepts `date,open,high,low,close[,volume]`, ISO dates, comma or semicolon delimiter; validates OHLC and duplicates.
- Optionally download Stooq daily historical bars (example ticker `AAPL.US`); HTTPS, fixed provider host, request timeout, size cap and symbol allowlist.
- Maintain up to 25 instruments locally (100,000 candles each); replace instrument history by reimport, preserving paper positions.
- Render two synchronized asset charts, normalized-return overlay and selectable historical cutoff.
- Search matching return sequences between the two selected instruments, or through the imported catalog, using Pearson correlation; exclude future samples and overlapping self-matches. Inspect an individual match with a normalized session-by-session shape comparison.
- Backtest long-only SMA/EMA crossover with next-opening fills, whole-share sizing, commission and slippage, equity series, closed trades, win rate and maximum drawdown. Compare strategy returns with a fee-adjusted buy-and-hold benchmark over the same interval. Report daily equity-curve annualized volatility, zero-risk-free Sharpe (undefined for a flat/insufficient series), time invested, worst daily session and observation count using a stated 252-trading-session convention.
- Compare six in-sample SMA transaction-cost scenarios (0/5/10/25/50/100 basis points each for fees and slippage) against fee-adjusted passive benchmarks, without predicting future returns.
- Compare saved SMA and EMA experiment snapshots only under the same underlying series hash, date range, capital and costs, using explicit percentage-point deltas; incomparable experiments are rejected.
- Save up to 100 versioned local SMA/EMA experiment summaries, bound to a SHA-256 fingerprint of the complete dataset and source metadata; retain historical results and flag dataset drift without silently revalidating them.
- Explore a bounded grid of SMA parameters in-sample; perform both a fixed 70/30 chronological holdout and expanding-window four-fold walk-forward validation without selecting parameters on test data.
- Place and persist local **simulated** buy/sell fills using the most recent imported close. Separately, a historical paper-replay session supports queued MARKET/LIMIT/STOP orders with daily OHLC execution assumptions, cancellation and optional inclusive good-through expiry, FILLED/REJECTED/CANCELLED/EXPIRED states, date-by-date marks and restart recovery; no execution API, no exchange connection and no brokerage credentials. Until instrument currency metadata and FX are available, paper positions are limited to one distinct symbol at a time; balances are expressed in abstract monetary units (u.m.).

Each imported instrument persists source attribution (STOOQ or USER_CSV), adjustment status (UNKNOWN unless explicitly verified), and optional currency/venue metadata. Neither ticker suffixes nor provider URLs are treated as proof of denomination or corporate-action adjustment. Data integrity reports flag candidate missing weekdays, zero-volume bars and unusually large closing moves; alerts are heuristic and do not correct market holidays or corporate actions. The News workspace imports attributed timestamped CSV headlines for local English/Spanish lexical sentiment, with conservative historical cutoffs; this is not a predictive ML model.

**Known omissions:** advanced indicators, market calendars and split/dividend adjustment, multi-timeframe/tick feeds, portfolio-level backtesting, parameterized strategy DSL, Monte Carlo/bootstrapping, broker-grade paper order types/execution logic, alerts, external licensed news ingestion and optional local AI research, signed installer/updater, and Windows end-to-end acceptance. Read `docs/BACKLOG.md` before extending functionality.

## Windows development prerequisites

- Windows 10/11 x64, .NET 10 SDK, Microsoft Edge WebView2 Evergreen Runtime, Node.js 22 LTS or newer, npm.
- **No paid service is needed** for development, import, analytics or paper simulation.

```powershell
# From repo root; one PowerShell window launches Vite, then the native shell.
./scripts/dev.ps1

# In a second window, run the standalone core assertions.
dotnet run --project tests/BotTrading.Tests/BotTrading.Tests.csproj -c Release

# Produce a Windows published application under artifacts/BotTrading.
./scripts/build.ps1
```

To run from source without the dev server, build the React frontend first with `cd frontend; npm install; npm run build`, then run the desktop project. In the project folder `frontend`, `npm test` checks the framework-independent TypeScript math. The build script prefers `npm ci` when `package-lock.json` exists. The first GitHub bootstrap generates a versioned lockfile; subsequent builds must use pinned dependencies.

### CSV format

```csv
Date,Open,High,Low,Close,Volume
2026-01-05,102.00,106.00,101.00,104.00,90000
2026-01-06,104.00,107.00,102.00,103.50,65000
```

Prices must be positive; volume may be zero or omitted; each date must be unique. This is raw, unadjusted data. When provider columns contain corporate-action-adjusted prices, the engine currently does not normalize historical events; avoid making financial decisions based on mixed adjustments.

## Data safety and financial risk

1. Data is stored per Windows profile in `%LOCALAPPDATA%\BotTrading\workspace.v1.json`; the WebView2 profile lives in a sibling directory. The UI never receives filesystem write permissions.
2. Production WebView2 navigation is restricted to the mapped local virtual origin; pop-ups, permissions, host objects and DevTools are disabled. Source messages are checked against the allowed origin, size and command allowlist. The UI is subject to a bundled CSP.
3. The app makes **no connection to any broker and cannot place live trades**. Imported prices may be delayed, stale, unadjusted, erroneous or provider-dependent.
4. Backtest fills use next-bar opening prices following a completed prior closing signal; simulated fees/slippage are configured but do not model partial fills, impact, spread dynamics, delistings, borrow, taxes or liquidity constraints.
5. The 70/30 test uses only training observations to select its parameters; one split is insufficient to establish robustness. Paper fills use a historic close, NOT a live quote.
6. Workspace replacement is atomic where the underlying Windows filesystem supports `File.Replace`. The data toolbar can export a validated, portable JSON backup and restore it through native Windows file pickers; import validation runs before state mutation and a `workspace.pre-restore.v1.json` rollback copy is written locally first. Backups are not encrypted or cloud-synchronized: protect their contents and retain external copies.

## Verification status

- **Windows CI** (`dev`): source tests **53/53 C#** and **11/11 TypeScript** PASS at commit `5a59fe33`; React typecheck, Vite production build and native `dotnet publish` win-x64 PASS. [CI evidence](https://github.com/akumanomi1988/trading-bot/actions/runs/40467016898).
- Windows CI now attempts a real WinForms/WebView2 startup smoke run on the published EXE, checking bundled React and two host bridge responses and saving an on-screen PNG; passing it does not certify full interaction or visual/accessibility acceptance. The artifact is an **unsigned build**, not a validated installer. Artifacts are retained for seven days.
- Real WebView2 interactions, provider downloads, native dialog, visual and accessibility tests, clean-machine packaging and upgrade behavior: **NOT ACCEPTED**.
- Documentation and manual test protocols: `docs/BACKLOG.md` and `docs/ACCEPTANCE.md`. **No full phase is accepted as COMPLETE.**

## Development decisions

The product's original seven high-level phases were recovered from conversation history; implementation scope was not found as a versioned repository specification. Do not count source files as accepted features. Complete Windows compilation and functional acceptance before declaring any component production-ready. See `docs/BACKLOG.md` for verified vs pending milestones and deterministic continuation.
