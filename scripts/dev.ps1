$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot/.."
Push-Location "$root/frontend"
$vite = $null
try {
  if (Test-Path 'package-lock.json') { npm ci --no-audit --no-fund }
  else { npm install --no-audit --no-fund }
  if ($LASTEXITCODE -ne 0) { throw 'Frontend dependencies failed to install.' }
  $vite = Start-Process -FilePath 'npm.cmd' -ArgumentList @('run', 'dev') -WorkingDirectory (Get-Location).Path -PassThru
  $ready = $false
  for ($i = 0; $i -lt 60; $i++) {
    try {
      $response = Invoke-WebRequest -Uri 'http://127.0.0.1:5173/' -TimeoutSec 1
      if ($response.StatusCode -eq 200) { $ready = $true; break }
    } catch { }
    Start-Sleep -Milliseconds 500
  }
  if (-not $ready) { throw 'Vite development server did not become healthy.' }
  $env:BOTTRADING_DEV_URL = 'http://127.0.0.1:5173/'
  dotnet run --project "$root/src/BotTrading.Desktop/BotTrading.Desktop.csproj"
  if ($LASTEXITCODE -ne 0) { throw 'Desktop application exited with an error.' }
} finally {
  Remove-Item Env:\BOTTRADING_DEV_URL -ErrorAction SilentlyContinue
  if ($null -ne $vite -and -not $vite.HasExited) {
    taskkill.exe /PID $vite.Id /T /F | Out-Null
  }
  Pop-Location
}
