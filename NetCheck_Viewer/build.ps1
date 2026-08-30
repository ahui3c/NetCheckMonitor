param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist\NetCheck_Viewer-Portable')
)

$ErrorActionPreference = 'Stop'
$viewerRoot = $PSScriptRoot
$workspaceRoot = Split-Path -Parent $viewerRoot
$iconPath = Join-Path $workspaceRoot 'assets\NetCheckMonitor.ico'
if (-not (Test-Path -LiteralPath $iconPath)) { throw "Application icon not found: $iconPath" }

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$outputExe = Join-Path $OutputDirectory 'NetCheck_Viewer.exe'
$provider = New-Object Microsoft.CSharp.CSharpCodeProvider
$parameters = New-Object System.CodeDom.Compiler.CompilerParameters
foreach ($reference in @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Windows.Forms.DataVisualization.dll', 'System.Web.Extensions.dll')) {
    [void]$parameters.ReferencedAssemblies.Add($reference)
}
$parameters.GenerateExecutable = $true
$parameters.GenerateInMemory = $false
$parameters.IncludeDebugInformation = $false
$parameters.OutputAssembly = $outputExe
$parameters.CompilerOptions = "/target:winexe /optimize+ /codepage:65001 /win32icon:`"$iconPath`""
try {
    $results = $provider.CompileAssemblyFromFile($parameters, [string[]]@(
        (Join-Path $viewerRoot 'AssemblyInfo.cs'),
        (Join-Path $viewerRoot 'BackupAnalyzer.cs'),
        (Join-Path $viewerRoot 'IncrementalScan.cs'),
        (Join-Path $viewerRoot 'AlertCenter.cs'),
        (Join-Path $viewerRoot 'TrendViews.cs'),
        (Join-Path $viewerRoot 'ViewerControlClient.cs'),
        (Join-Path $viewerRoot 'NetCheckViewer.cs')
    ))
}
finally { $provider.Dispose() }

if ($results.Errors.HasErrors) {
    $messages = @($results.Errors | ForEach-Object { "$($_.FileName)($($_.Line),$($_.Column)): $($_.ErrorNumber): $($_.ErrorText)" })
    throw "C# build failed:`n$($messages -join "`n")"
}

Copy-Item -LiteralPath (Join-Path $viewerRoot '使用說明.txt') -Destination $OutputDirectory -Force
$settingsArtifact = Join-Path $OutputDirectory 'NetCheck_Viewer.settings.json'
if (Test-Path -LiteralPath $settingsArtifact) { Remove-Item -LiteralPath $settingsArtifact -Force }
$zipPath = Join-Path (Split-Path -Parent $OutputDirectory) 'NetCheck_Viewer-Portable.zip'
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -Path (Join-Path $OutputDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "Build complete: $outputExe"
Write-Host "Portable package: $zipPath"
