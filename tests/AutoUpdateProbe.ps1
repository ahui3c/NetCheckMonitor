param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [Parameter(Mandatory = $true)][string]$Package
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('NetCheckAutoUpdate_' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
try {
    $assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $Executable))
    $service = $assembly.GetType('NetCheck.UpdateService', $true)
    $method = $service.GetMethod('ValidateDownloadedPackage', [Reflection.BindingFlags]'Static,NonPublic')
    if ($null -eq $method) { throw 'ValidateDownloadedPackage was not found.' }
    $writableMethod = $service.GetMethod('IsDirectoryWritable', [Reflection.BindingFlags]'Static,NonPublic')
    $startInfoMethod = $service.GetMethod('CreateUpdaterStartInfo', [Reflection.BindingFlags]'Static,NonPublic')
    if ($null -eq $writableMethod -or $null -eq $startInfoMethod) { throw 'UAC update helpers were not found.' }
    [string]$packagePath = (Resolve-Path -LiteralPath $Package).Path
    [string]$digest = 'sha256:' + [string](Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $validRoot = Join-Path $testRoot 'valid'
    New-Item -ItemType Directory -Path $validRoot | Out-Null
    $validated = $method.Invoke($null, [object[]]@([string]$packagePath, [string]$digest, [string]'v0.9.16', [string]'https://example.invalid/release', [string]$validRoot))
    $validPackage = $validated.Version -eq '0.9.16' -and
        (Test-Path -LiteralPath (Join-Path $validated.ExtractDirectory 'NetCheckMonitor.exe')) -and
        (Test-Path -LiteralPath (Join-Path $validated.ExtractDirectory 'NetCheckUpdater.exe'))

    $wrongDigestRejected = $false
    try {
        $badRoot = Join-Path $testRoot 'bad-digest'
        New-Item -ItemType Directory -Path $badRoot | Out-Null
        [string]$badDigest = 'sha256:' + (('0' * 64) -join '')
        [void]$method.Invoke($null, [object[]]@([string]$packagePath, [string]$badDigest, [string]'v0.9.16', [string]'', [string]$badRoot))
    } catch { $wrongDigestRejected = $null -ne $_.Exception.InnerException -and $_.Exception.InnerException.Message -like '*digest*' }

    $maliciousZip = Join-Path $testRoot 'malicious.zip'
    $stream = [IO.File]::Open($maliciousZip, [IO.FileMode]::CreateNew)
    try {
        $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            $entry = $archive.CreateEntry('../escaped.txt')
            $writer = [IO.StreamWriter]::new($entry.Open())
            try { $writer.Write('escape') } finally { $writer.Dispose() }
        } finally { $archive.Dispose() }
    } finally { $stream.Dispose() }
    [string]$maliciousDigest = 'sha256:' + [string](Get-FileHash -LiteralPath $maliciousZip -Algorithm SHA256).Hash.ToLowerInvariant()
    $zipSlipRejected = $false
    try {
        $zipRoot = Join-Path $testRoot 'zip-slip'
        New-Item -ItemType Directory -Path $zipRoot | Out-Null
        [void]$method.Invoke($null, [object[]]@([string]$maliciousZip, [string]$maliciousDigest, [string]'v0.9.16', [string]'', [string]$zipRoot))
    } catch { $zipSlipRejected = $null -ne $_.Exception.InnerException -and $_.Exception.InnerException.Message -like '*Unsafe path*' }
    $escapedFileAbsent = -not (Test-Path -LiteralPath (Join-Path $testRoot 'escaped.txt'))

    $writableDirectoryDetected = [bool]$writableMethod.Invoke($null, [object[]]@([string]$testRoot))
    $normalStartInfo = $startInfoMethod.Invoke($null, [object[]]@([string]'updater.exe', [string]'--test 1', [string]$testRoot, [bool]$false))
    $elevatedStartInfo = $startInfoMethod.Invoke($null, [object[]]@([string]'updater.exe', [string]'--test 1', [string]$testRoot, [bool]$true))
    $normalLaunchConfigured = -not $normalStartInfo.UseShellExecute -and [string]::IsNullOrEmpty($normalStartInfo.Verb)
    $uacLaunchConfigured = $elevatedStartInfo.UseShellExecute -and $elevatedStartInfo.Verb -eq 'runas'

    [PSCustomObject]@{
        ValidPackage = $validPackage
        WrongDigestRejected = $wrongDigestRejected
        ZipSlipRejected = $zipSlipRejected
        EscapedFileAbsent = $escapedFileAbsent
        WritableDirectoryDetected = $writableDirectoryDetected
        NormalLaunchConfigured = $normalLaunchConfigured
        UacLaunchConfigured = $uacLaunchConfigured
    }
    if (-not ($validPackage -and $wrongDigestRejected -and $zipSlipRejected -and $escapedFileAbsent -and $writableDirectoryDetected -and $normalLaunchConfigured -and $uacLaunchConfigured)) { throw 'Automatic update package validation failed.' }
} finally {
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
