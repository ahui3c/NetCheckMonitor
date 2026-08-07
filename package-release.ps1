param([string]$Destination = (Join-Path $PSScriptRoot 'dist\NetCheckMonitor-Portable.zip'))

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
& (Join-Path $root 'build.ps1') -OutputName 'NetCheckMonitor.exe'
$portable = Join-Path $root 'NetCheck-Portable'
$files = @(
    (Join-Path $portable 'NetCheckMonitor.exe'),
    (Join-Path $portable 'NetCheckUpdater.exe'),
    (Join-Path $portable 'update-manifest.json'),
    (Join-Path $portable '使用說明.txt'),
    (Join-Path $portable 'User_Guide_EN.txt')
)
foreach ($file in $files) { if (-not (Test-Path -LiteralPath $file)) { throw "Release file is missing: $file" } }
$destinationDirectory = Split-Path -Parent $Destination
if (-not [String]::IsNullOrWhiteSpace($destinationDirectory)) { New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null }
Compress-Archive -LiteralPath $files -DestinationPath $Destination -Force
$hash = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash
Write-Host "Release package: $Destination"
Write-Host "SHA256: $hash"
