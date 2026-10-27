# BotTrading Windows acceptance protocol

This protocol records tests that cannot be executed in the available Linux environment. Do not mark any as PASS until performed on the real Windows release build. Screenshot/video logs should be retained with exact date, build SHA, .NET/WebView2/runtime versions.

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

Never use a live brokerage account, production secrets or real money for acceptance.
