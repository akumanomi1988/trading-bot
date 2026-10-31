param(
    [string]$Version = '0.1.0'
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
    throw 'Expected semantic version in major.minor.patch format.'
}
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$source = Join-Path $root 'artifacts/BotTrading'
$destination = Join-Path $root 'artifacts/packages'
if (-not (Test-Path (Join-Path $source 'BotTrading.Desktop.exe')) -or
    -not (Test-Path (Join-Path $source 'web/index.html'))) {
    throw 'Published desktop executable and bundled React assets are required.'
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$commit = if ($env:GITHUB_SHA) { $env:GITHUB_SHA } else { 'local-unverified' }
$manifest = [ordered]@{
    application = 'BotTrading'
    version = $Version
    sourceCommit = $commit
    platform = 'win-x64'
    mode = 'simulation-only'
    distribution = 'portable-unsigned'
    runtime = 'self-contained-dotnet-10'
    webview2RuntimeRequired = $true
    limitations = @('No live broker orders', 'No licensed market data bundled',
        'Unsigned beta distribution, not a production-certified installer')
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $source 'release-manifest.json') -Encoding utf8
$name = "BotTrading-$Version-win-x64-portable-unsigned.zip"
$zip = Join-Path $destination $name
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $source '*') -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -Path $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $name" | Set-Content -Path "$zip.sha256" -Encoding ascii
Write-Host "Portable package: $zip"
Write-Host "SHA-256: $hash"
