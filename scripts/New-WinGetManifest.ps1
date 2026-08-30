param(
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory = $true)][ValidatePattern('^\d{4}-\d{2}-\d{2}$')][string]$ReleaseDate,
    [string]$PackagePath,
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([String]::IsNullOrWhiteSpace($PackagePath)) { $PackagePath = Join-Path $root 'dist\NetCheckMonitor-Portable.zip' }
if ([String]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = Join-Path $root 'packaging\winget' }
$packageIdentifier = 'AHui3C.NetCheckMonitor'
$manifestVersion = '1.10.0'
$resolvedPackage = (Resolve-Path -LiteralPath $PackagePath).Path
$packageHash = (Get-FileHash -LiteralPath $resolvedPackage -Algorithm SHA256).Hash.ToUpperInvariant()
$outputDirectory = Join-Path $OutputRoot $Version
$releaseUrl = "https://github.com/ahui3c/NetCheckMonitor/releases/download/v$Version/NetCheckMonitor-Portable.zip"
$releasePage = "https://github.com/ahui3c/NetCheckMonitor/releases/tag/v$Version"

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($resolvedPackage)
try {
    $requiredFiles = @('NetCheckMonitor.exe', 'NetCheckUpdater.exe', 'update-manifest.json', '使用說明.txt', 'User_Guide_EN.txt')
    $archiveFiles = @($archive.Entries | ForEach-Object { $_.FullName.Replace('/', '\') })
    foreach ($requiredFile in $requiredFiles) {
        if ($archiveFiles -notcontains $requiredFile) { throw "Release archive is missing WinGet payload: $requiredFile" }
    }
}
finally { $archive.Dispose() }

New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$utf8NoBom = New-Object Text.UTF8Encoding($false)

$versionManifest = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.$manifestVersion.schema.json
PackageIdentifier: $packageIdentifier
PackageVersion: $Version
DefaultLocale: zh-TW
ManifestType: version
ManifestVersion: $manifestVersion
"@

$installerManifest = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.$manifestVersion.schema.json
PackageIdentifier: $packageIdentifier
PackageVersion: $Version
InstallerType: zip
NestedInstallerType: portable
Scope: user
UpgradeBehavior: install
RequireExplicitUpgrade: true
Commands:
- NetCheckMonitor
ReleaseDate: $ReleaseDate
Installers:
- Architecture: neutral
  NestedInstallerFiles:
  - RelativeFilePath: NetCheckMonitor.exe
    PortableCommandAlias: NetCheckMonitor
  InstallerUrl: $releaseUrl
  InstallerSha256: $packageHash
ManifestType: installer
ManifestVersion: $manifestVersion
"@

$zhLocaleManifest = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.$manifestVersion.schema.json
PackageIdentifier: $packageIdentifier
PackageVersion: $Version
PackageLocale: zh-TW
Publisher: 廖阿輝
PublisherUrl: https://ahui3c.com
PublisherSupportUrl: https://github.com/ahui3c/NetCheckMonitor/issues
PackageName: NetCheckMonitor
PackageUrl: https://github.com/ahui3c/NetCheckMonitor
License: MIT
LicenseUrl: https://github.com/ahui3c/NetCheckMonitor/blob/main/LICENSE
ShortDescription: 長時間監控 Windows 對外網路連線、斷線與延遲，並產生圖文報表。
Description: |-
  NetCheckMonitor 是免費、開源、無廣告的 Windows 網路監控工具。它會定時檢查對外連線，記錄斷線與延遲，產生 HTML、PDF 與 CSV 報表，並支援定時測速、Google Drive 備份及 Gmail 通知。
Tags:
- connectivity
- internet
- latency
- monitoring
- network
- outage
- report
ReleaseNotesUrl: $releasePage
ManifestType: defaultLocale
ManifestVersion: $manifestVersion
"@

$enLocaleManifest = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.locale.$manifestVersion.schema.json
PackageIdentifier: $packageIdentifier
PackageVersion: $Version
PackageLocale: en-US
Publisher: AHui3C
PublisherUrl: https://ahui3c.com
PublisherSupportUrl: https://github.com/ahui3c/NetCheckMonitor/issues
PackageName: NetCheckMonitor
PackageUrl: https://github.com/ahui3c/NetCheckMonitor
License: MIT
LicenseUrl: https://github.com/ahui3c/NetCheckMonitor/blob/main/LICENSE
ShortDescription: Monitor Windows Internet connectivity, outages, and latency over time and generate reports.
Description: |-
  NetCheckMonitor is a free, open-source, ad-free Windows utility that periodically checks Internet connectivity, records outages and latency, creates HTML, PDF, and CSV reports, and supports scheduled speed tests, Google Drive backups, and Gmail notifications.
Tags:
- connectivity
- internet
- latency
- monitoring
- network
- outage
- report
ReleaseNotesUrl: $releasePage
ManifestType: locale
ManifestVersion: $manifestVersion
"@

$manifests = [ordered]@{
    "$packageIdentifier.yaml" = $versionManifest
    "$packageIdentifier.installer.yaml" = $installerManifest
    "$packageIdentifier.locale.zh-TW.yaml" = $zhLocaleManifest
    "$packageIdentifier.locale.en-US.yaml" = $enLocaleManifest
}
foreach ($item in $manifests.GetEnumerator()) {
    [IO.File]::WriteAllText((Join-Path $outputDirectory $item.Key), ($item.Value.Trim() + "`n"), $utf8NoBom)
}

Write-Host "WinGet manifests: $outputDirectory"
Write-Host "Package: $resolvedPackage"
Write-Host "SHA256: $packageHash"
