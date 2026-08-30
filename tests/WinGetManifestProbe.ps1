param([string]$ManifestDirectory, [string]$Package)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([String]::IsNullOrWhiteSpace($ManifestDirectory)) { $ManifestDirectory = Join-Path $root 'packaging\winget\0.9.15' }
if ([String]::IsNullOrWhiteSpace($Package)) { $Package = Join-Path $root 'dist\NetCheckMonitor-Portable.zip' }
$identifier = 'AHui3C.NetCheckMonitor'
$expectedVersion = Split-Path -Leaf $ManifestDirectory
$expectedFiles = @(
    "$identifier.yaml",
    "$identifier.installer.yaml",
    "$identifier.locale.zh-TW.yaml",
    "$identifier.locale.en-US.yaml"
)
foreach ($file in $expectedFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $ManifestDirectory $file))) { throw "Missing WinGet manifest: $file" }
}

$allText = @($expectedFiles | ForEach-Object { Get-Content -LiteralPath (Join-Path $ManifestDirectory $_) -Raw }) -join "`n"
if (($allText | Select-String -Pattern "PackageIdentifier: $([regex]::Escape($identifier))" -AllMatches).Matches.Count -ne 4) { throw 'Package identifier is inconsistent.' }
if (($allText | Select-String -Pattern "PackageVersion: $([regex]::Escape($expectedVersion))" -AllMatches).Matches.Count -ne 4) { throw 'Package version is inconsistent.' }

$installer = Get-Content -LiteralPath (Join-Path $ManifestDirectory "$identifier.installer.yaml") -Raw
$actualHash = (Get-FileHash -LiteralPath $Package -Algorithm SHA256).Hash.ToUpperInvariant()
$requiredInstallerValues = @(
    'InstallerType: zip',
    'NestedInstallerType: portable',
    'Scope: user',
    'UpgradeBehavior: install',
    'RequireExplicitUpgrade: true',
    'Architecture: neutral',
    'RelativeFilePath: NetCheckMonitor.exe',
    'PortableCommandAlias: NetCheckMonitor',
    "releases/download/v$expectedVersion/NetCheckMonitor-Portable.zip",
    "InstallerSha256: $actualHash"
)
foreach ($value in $requiredInstallerValues) {
    if (-not $installer.Contains($value)) { throw "Installer manifest is missing: $value" }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Package).Path)
try {
    $archiveFiles = @($archive.Entries | ForEach-Object { $_.FullName.Replace('/', '\') })
    foreach ($file in @('NetCheckMonitor.exe', 'NetCheckUpdater.exe', 'update-manifest.json', '使用說明.txt', 'User_Guide_EN.txt')) {
        if ($archiveFiles -notcontains $file) { throw "Package is missing: $file" }
    }
}
finally { $archive.Dispose() }

Write-Output "WinGet manifest probe passed: $identifier $expectedVersion ($actualHash)"
