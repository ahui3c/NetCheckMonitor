param(
    [string]$Executable = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\NetCheck_Viewer-Portable\NetCheck_Viewer.exe')
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Executable)) { throw "Viewer executable not found: $Executable" }
$resultPath = Join-Path $env:TEMP ('NetCheckViewerProbe-' + [guid]::NewGuid().ToString('N') + '.txt')
try {
    $process = Start-Process -FilePath $Executable -ArgumentList @('--self-test', $resultPath) -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) { throw "Viewer self-test failed with exit code $($process.ExitCode)." }
    $result = Get-Content -LiteralPath $resultPath -Raw
    if ($result.Trim() -ne 'NETCHECK_VIEWER_SELFTEST_OK') { throw "Unexpected self-test result: $result" }
    Write-Host 'NETCHECK_VIEWER_PROBE_OK'
}
finally {
    if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath -Force }
}
