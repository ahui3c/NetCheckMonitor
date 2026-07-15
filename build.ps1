param([string]$OutputName = 'NetCheckMonitor.exe')

$outputName = [System.IO.Path]::GetFileName($OutputName)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$outDir = Join-Path $root 'NetCheck-Portable'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$refs = @(
    'System.dll',
    'System.Core.dll',
    'System.Drawing.dll',
    'System.Windows.Forms.dll',
    'System.Security.dll',
    'System.Web.Extensions.dll'
)

Add-Type -Path @((Join-Path $root 'Localization.cs'), (Join-Path $root 'NetCheck.cs'), (Join-Path $root 'DataReport.cs'), (Join-Path $root 'CloudBackup.cs')) `
    -ReferencedAssemblies $refs `
    -OutputAssembly (Join-Path $outDir $outputName) `
    -OutputType WindowsApplication

Copy-Item -LiteralPath (Join-Path $root '使用說明.txt') -Destination $outDir -Force
Copy-Item -LiteralPath (Join-Path $root 'User_Guide_EN.txt') -Destination $outDir -Force
Write-Host "Build complete: $outDir\$outputName"
