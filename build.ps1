param([string]$OutputName = 'NetCheckMonitor.exe')

$outputName = [System.IO.Path]::GetFileName($OutputName)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$outDir = Join-Path $root 'NetCheck-Portable'
$iconPath = Join-Path $root 'assets\NetCheckMonitor.ico'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
if (-not (Test-Path -LiteralPath $iconPath)) { throw "Application icon not found: $iconPath" }

$refs = @(
    'System.dll',
    'System.Core.dll',
    'System.Drawing.dll',
    'System.Windows.Forms.dll',
    'System.Security.dll',
    'System.Web.Extensions.dll'
)

$sources = @(
    (Join-Path $root 'Localization.cs'),
    (Join-Path $root 'NetCheck.cs'),
    (Join-Path $root 'DataReport.cs'),
    (Join-Path $root 'CloudBackup.cs')
)
$provider = New-Object Microsoft.CSharp.CSharpCodeProvider
$parameters = New-Object System.CodeDom.Compiler.CompilerParameters
foreach ($reference in $refs) { [void]$parameters.ReferencedAssemblies.Add($reference) }
$parameters.GenerateExecutable = $true
$parameters.GenerateInMemory = $false
$parameters.IncludeDebugInformation = $false
$parameters.OutputAssembly = Join-Path $outDir $outputName
$parameters.CompilerOptions = "/target:winexe /optimize+ /win32icon:`"$iconPath`""
$results = $provider.CompileAssemblyFromFile($parameters, [string[]]$sources)
$provider.Dispose()
if ($results.Errors.HasErrors) {
    $messages = @($results.Errors | ForEach-Object { "$($_.FileName)($($_.Line),$($_.Column)): $($_.ErrorNumber): $($_.ErrorText)" })
    throw "C# build failed:`n$($messages -join "`n")"
}

Copy-Item -LiteralPath (Join-Path $root '使用說明.txt') -Destination $outDir -Force
Copy-Item -LiteralPath (Join-Path $root 'User_Guide_EN.txt') -Destination $outDir -Force
Write-Host "Build complete: $outDir\$outputName"
