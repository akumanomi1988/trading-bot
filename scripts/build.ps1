$ErrorActionPreference = 'Stop'
Push-Location "$PSScriptRoot/.."
try {
  Push-Location frontend
  try { if (Test-Path 'package-lock.json') { npm ci --no-audit --no-fund } else { npm install --no-audit --no-fund }; if ($LASTEXITCODE -ne 0) { throw 'npm dependency installation failed' }; npm run build; if ($LASTEXITCODE -ne 0) { throw 'frontend build failed' } }
  finally { Pop-Location }
  dotnet run --project tests/BotTrading.Tests/BotTrading.Tests.csproj -c Release
  if ($LASTEXITCODE -ne 0) { throw 'core tests failed' }
  dotnet publish src/BotTrading.Desktop/BotTrading.Desktop.csproj -c Release -r win-x64 --self-contained false -o artifacts/BotTrading
  if ($LASTEXITCODE -ne 0) { throw 'desktop publish failed' }
} finally { Pop-Location }
