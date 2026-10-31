# BotTrading portable Windows beta — manual release protocol

Status: **unsigned beta / simulation-only**. Publishing an artifact does NOT certify financial market-data accuracy, commercial feed licensing, user acceptance, a signed installer, or automatic upgrades.

## Build

On a clean Windows 10/11 x64 development agent with .NET 10 SDK, Node.js 22 and access to NuGet/npm, run `./scripts/build.ps1`. This compiles the bundled React app, runs .NET tests, publishes a **self-contained** .NET Windows executable and creates `artifacts/packages/BotTrading-0.1.0-win-x64-portable-unsigned.zip` plus its SHA-256 text file. Microsoft Edge **WebView2 Evergreen Runtime is still required** on the target PC.

The GitHub Windows workflow performs two real native smoke tests: the normal publish and the versioned ZIP extracted to a clean directory. It checks the build manifest SHA, archive checksum, React assets and resulting WebView2 screenshot. Download artifacts directly from the successful run; workflow artifacts are temporary (7 days), not durable tagged GitHub Releases.

## Manual install or update

1. Ensure no BotTrading process is running; back up the workspace using **Exportar copia** and copy the ZIP to a safe location.
2. Check the archive SHA-256 against the sibling `.sha256` file using `Get-FileHash`. Never trust an unsigned binary from an unknown source.
3. Extract the entire ZIP into a new versioned folder outside `Program Files` (no administrator rights required). Run `BotTrading.Desktop.exe` from that folder, not from the ZIP itself.
4. Confirm the app loads and tests with **simulated** funds only. Local workspace and the WebView2 profile remain in the user's `%LOCALAPPDATA%/BotTrading` profile; installing a new version does not migrate or delete them.
5. If startup fails, close the app and return to the previous folder. Restore the exported workspace only if needed after inspecting the error; do not overwrite corrupt workspaces automatically.

The binary is **not code-signed**; Windows SmartScreen may display warnings. No certificate, installer, silent upgrade or automatic rollback has been provided. These need deliberate release approval and additional clean-machine/manual QA.

No real broker or paid market-data source is configured. Check the current terms of every imported data provider before use or redistribution.
