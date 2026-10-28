# BotTrading Windows acceptance protocol

This protocol distinguishes automated Windows CI smoke checks from remaining native acceptance scenarios. Do not mark any as PASS until performed on the real Windows release build. Screenshot/video logs should be retained with exact date, build SHA, .NET/WebView2/runtime versions.

1. `scripts/build.ps1` on clean Windows 10/11: compiles frontend and .NET, all C# and UI math assertions pass, emits `artifacts/BotTrading/BotTrading.Desktop.exe`. Start EXE; one WinForms window hosts React; no separate local HTTP API starts.

2. With zero assets, show empty state. Enter `AAPL.US` and select Download. If Stooq is available, actual OHLC daily data and metadata appear; if unavailable, an actionable failure appears without corrupting stored data.

3. Use `Importar CSV` twice for distinct symbols and valid ISO-date OHLC CSVs. Verify two panels, date labels, distinct colors, normalized overlay, 90/180/365-day window controls and malformed file rejection. Cancel native picker; original catalog remains intact.

4. Replay to a mid-history date: both charts must end no later than the cutoff. Search for 30-day patterns between the selected A/B pair, then click a result to compare their relative session shapes; no candidate's `end` date may exceed cutoff and same-symbol overlapping windows must be absent.

5. Run SMA 10/30 backtest on 300+ samples; all algorithmic orders are dated after their closing-signal bar, equity begins at configured capital, no negative balance. Compare against independently calculated small dataset.

6. Run parameter grid, then chronological 70/30 holdout. Change held-out prices in a copy of the dataset: selected SMA periods must not change. Training and test date ranges must not overlap.

7. In Paper Trading buy and sell simulated whole shares with sufficient cash; reject insufficient balance, over-selling and invalid quantity. Restart EXE; position quantities, cash and fills must persist. Reset requires affirmative confirmation.

8. Attempt navigation in WebView2 to an external URL, `window.open` and browser permissions. Navigation/popups should be blocked; host objects inaccessible; no bridge method should read arbitrary paths or execute a command outside the allowlist.

9. Import bad dates, duplicate dates, negative volume, zero open, unsorted bars, files exceeding 16 MiB and unsupported tickers. All must fail with an understandable error without overwriting valid local workspace.

10. Run two EXEs: second instance must not open a second workstation; first must keep working. Test window closing, relaunch and corrupted workspace (must fail safely without overwriting corruption).

11. Test 1366×768 and 1920×1080 screens, keyboard-only tab order, focus visibility, high DPI, Windows scaling 125%/150% and Edge WebView2 update.

12. Test multi-million-candle workload **only after increasing explicit domain limits with streaming/downsampling**. The currently implemented cap is 25 instruments × 100k bars each; measure 100k × 25 before declaring performance readiness.

13. Historical paper replay: import an OHLC CSV with at least four dated candles for `REPLAY` (e.g. closes 100, 105, 110, 120 and identical opens). In the Paper tab, start a replay at the first date. Submit a BUY of one share: order is `PENDING`, cash remains at 10,000 and the mark stays on the first candle. Advance one session: order becomes `FILLED` on the **second** candle opening (with explicitly displayed slippage/fees), cash decreases and the cursor advances. Submit SELL and advance: shares return to zero and cost basis clears. Restart the application and verify replay date, fills, balance and status remain unchanged. Attempt to import altered OHLC data for an already viewed replay date: it must reject without mutating the catalog or saved state. Choose **Discard replay**, then import the corrected file successfully. Verify the quick paper account remains unchanged throughout. Also submit orders with insufficient cash/shares, verify `REJECTED`, and confirm no orders execute before the next candle. At the last candle, further advances must be disabled. No live market or broker connection should appear.

14. Benchmark parity: run the SMA strategy on a constant-price CSV with fees/slippage enabled. No crossover trade occurs, so the strategy return is zero while the passive buy-and-hold return is negative after purchase and liquidation costs; excess return equals the opposite of the benchmark return. Re-run with a 70/30 holdout and verify the benchmark purchases only at the test period's first opening, not the training period's opening.

15. Data integrity panel: on a Friday/Monday consecutive CSV the candidate missing-weekday counter must remain zero. Insert a missing Tuesday and a +40% close-to-close move and verify separate counters and event dates. Move the historical cutoff to Monday and verify that later gaps/jumps are not disclosed; the warning must explain market holidays and corporate actions are not adjusted.

16. News research: import a UTF-8 CSV with `publishedAt,symbol,headline,source`, explicit time-zone offsets, one instrument-specific headline dated Friday and one GLOBAL headline dated Monday. With replay cutoff set to Monday, only Friday is visible; Tuesday includes Monday. Confirm no same-day articles leak into daily historical sessions. Restart the workstation and verify imported attribution, order, sentiment labels and paper balances persist. Reject missing time-zone, invalid article, over-1-MiB input without overwriting existing news. No outbound news provider calls occur.

17. Conditional replay orders: buy limit under market remains pending across bars whose lows exceed the limit and fills only after a future low touches it; fill may not exceed buy limit after simulated slippage. Sell limit similarly may not fill below its limit. Buy stop above the market triggers on a future high; sell stop below the market triggers on a future low and respects adverse gap openings. Insufficient holdings/cash is reported as REJECTED at execution, not order entry. Restart the workstation with a pending limit and verify trigger, kind and cursor persist. Clearly disclose that daily OHLC does not establish intrabar event ordering.

18. Monthly contribution replay: import adjusted or internally consistent daily bars covering Jan–Mar 2000, select 100 u.m./month and January 2000. The playback advances one calendar month per second; each month deposits exactly 100, buys whole shares at its first available daily open with costs, carries leftover cash and marks equity at the last visible monthly close. Remove February bars: the month remains visible with a deposit and no purchase. Change March prices: January/February historical views remain identical. Start in a month before available market history: show an actionable validation failure, never invent earlier share prices. Explain missing dividends, splits, FX and taxes.

19. Strategy risk panel: run an SMA test on a 40-session flat-price CSV. The strategy has 0% volatility and 0% invested-session exposure, while Sharpe displays N/D, never Infinity or NaN. Run a second CSV that causes crossover fills: verify nonzero exposure, bounded worst-session return, volatility and Sharpe against an independent equity-curve calculation. Confirm the panel discloses a 252-session year and zero risk-free rate.

20. Replay order lifecycle: enter a pending BUY LIMIT order outside the next session range and cancel it; later price changes must never execute it. A second pending order with an inclusive good-through date must expire before the first bar after the date, even if its high/low reaches the trigger. An order whose trigger occurs on its good-through date may fill. Restart and verify statuses, dates and cash. Invalid good-through dates must be rejected.

21. Backup and restore: create two imported assets, a paper position, a pending replay order and news. Export a JSON backup using the native Save dialog. Change the workspace, then restore via native Open dialog after explicit confirmation. Verify all four subsystems return to their exported values and a pre-restore recovery copy exists at %LOCALAPPDATA%\\BotTrading\\workspace.pre-restore.v1.json. Try malformed JSON and an incomplete portfolio backup: both must fail without changing either the current memory or persisted state. Cancel both dialogs; no state should change.

22. Strategy cost sensitivity: load an active ticker, choose SMA fast/slow and run the six-scenario stress report. All cases use the same dataset and parameters; fees and slippage each increase through 0, 5, 10, 25, 50 and 100 bps. For flat prices, SMA returns remain zero without trades, but passive benchmark returns decline with costs. Changing the ticker or parameters clears stale scenarios.

23. Ledger integrity: execute multiple historical replay buys and a sell, close and reopen the application. Cash, holdings, average cost and fills must be unchanged. Tamper separately with a saved fill price, its fee, cash and position quantity in a copy of the workspace JSON; loading must reject every manipulated state without rewriting the invalid file.

24. Saved experiments: import an instrument with 60+ candles, set SMA 3/8, save a strategy experiment, restart and verify the stored return, parameters and fingerprint match and are marked current. Reimport the same ticker with different OHLC history; the stored experiment must remain but show changed/absent dataset. Delete the saved experiment and verify live series and paper balances are unchanged. Export/restore a backup containing experiments and confirm they survive.

25. SMA/EMA model selection: on the Strategy tab switch to EMA; execute a backtest, parameter optimization, 70/30 holdout, four-fold walk-forward and cost stress. Every result must retain the EMA label. Verify fills use EMA cross signals from the previous close and the next session open. Save and reload an EMA experiment. Existing v1 experiments lacking a strategyKind remain SMA. Switching strategies must clear stale displayed computations.

26. Saved experiment comparison: save SMA and EMA experiments for the same instrument and historical price dataset. Select them in the Experiment Archive, compare and verify that return, passive benchmark, drawdown and closed-trade deltas are computed as first minus second. Change the underlying data and save a new experiment; attempting to compare it with either older snapshot must fail, displaying the cause. Compare a snapshot to itself: it must be rejected.

27. Live market provider acceptance: with internet available, download a known supported Stooq ticker and verify dates, OHLC, provenance and persisted data. If rate-limited, the UI must show an actionable 429 message and preserve the previous workspace unchanged. Test a malformed ticker and disconnected network: no imported data may be overwritten. Automated HTTP tests intentionally use an injected offline handler and do not establish live service availability.

28. Native CI smoke: on Windows runner execute the published EXE with BOTTRADING_SMOKE_TEST=1. The runner must observe the exact https://bottrading.local origin, React's empty-workspace data toolbar and heading, successful app.info and assets.list native messages, a nonblank PNG preview, and a clean zero process exit. Without WebView2 runtime or an interactive window station the job must fail, not be skipped or silently pass. The smoke test is not a substitute for human visual DPI and native-dialog testing.

29. Workspace schema migration: open a legacy workspace with no schemaVersion or experiment strategyKind and verify its saved experiments are interpreted as SMA without modifying the source file during load. Saving afterward must write version 1 metadata. Mutate a copy with schemaVersion=2: loading must fail and leave bytes untouched; normal workspace and backups must remain recoverable.

30. Configurable transaction costs: set different commission and slippage (e.g. 25 and 75 bps), run SMA and EMA backtests, optimize and perform both OOS tests. Verify the resulting costs and saved experiments match the input, persist after restart, and cross-experiment comparisons reject different fee/slippage assumptions. Invalid negative or greater-than-1000 bps inputs must not execute. The six-case sensitivity grid remains independent of these inputs.

31. Capital risk cap: on the Strategy tab select SMA or EMA, set maximum capital per entry to 50%, run a strategy that generates at least one buy and compare with 100%. Entry cost including commission must be no more than half of the available account cash and quantity must not increase. Verify optimization, OOS, walk-forward and cost stress retain the cap. Save experiments with 50% and 100%: comparison must reject them as incomparable. Zero or more than 100% must fail. Historic experiment records lacking the cap must be treated as 100%.

32. Large-chart acceptance: import an instrument with 100,000 daily observations and navigate to its comparison, strategy and replay charts. Confirm line rendering stays responsive, the 1,200-vertex cap preserves first/last observations and extrema, unusual spikes remain visible, and relative calendar spacing is maintained. Measure peak memory and UI latency on Windows separately; the bounded SVG chart itself does not cap native-to-React history serialization.

33. Synthetic desktop E2E: execute the Windows CI smoke with two isolated synthetic 420-session instruments. The initial comparison must show the synthetic symbols. On the Strategy tab select EMA and run a genuine .NET backtest; the performance panel must contain an EMA result. On Paper Trading submit a BUY of one SIM.A simulated share via the UI, verify its displayed position and matching backend paper balance, and capture the PNG. No real broker or network feed is used. This does not replace full human acceptance of dialogs, DPI, persistence over multiple executions and real data.

34. Cross-origin navigation E2E: during the native WebView2 CI smoke, attempt to navigate to https://outside-origin.invalid. The native NavigationStarting handler must cancel it; WebView2 must stay at the allowed virtual HTTPS origin, retain the portfolio screen and not expose the bridge to any remote page. The smoke test should fail if navigation escapes. Popup and permission-denial cases remain separately pending.

Never use a live brokerage account, production secrets or real money for acceptance.
