param([Parameter(Mandatory = $true)][string]$Executable)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$testRoot = Join-Path $root ('.portable-settings-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null

try {
    $assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $Executable))
    $type = $assembly.GetType('NetCheck.PortableSettingsStore', $true)
    $flags = [Reflection.BindingFlags]'Static,NonPublic'
    $migrationArgs = [object[]]::new(1); $migrationArgs[0] = [string]$testRoot
    $migrationOk = [bool]$type.GetMethod('RunMigrationSelfTest', $flags).Invoke($null, $migrationArgs)
    $exeDirectory = Split-Path -Parent (Resolve-Path $Executable)
    $settingsPath = [string]$type.GetProperty('SettingsPath', $flags).GetValue($null, $null)
    $cloudPath = [string]$type.GetProperty('CloudPath', $flags).GetValue($null, $null)
    $sessionPath = [string]$type.GetProperty('SessionPath', $flags).GetValue($null, $null)
    $besideExecutable = [IO.Path]::GetDirectoryName($settingsPath) -eq $exeDirectory -and
        [IO.Path]::GetDirectoryName($cloudPath) -eq $exeDirectory -and
        [IO.Path]::GetDirectoryName($sessionPath) -eq $exeDirectory -and
        [IO.Path]::GetFileName($settingsPath) -eq 'NetCheckMonitor.settings.json'
    $oldPortable = $env:NETCHECK_PORTABLE_SETTINGS
    $oldLegacyMonitor = $env:NETCHECK_LEGACY_MONITOR_DIR
    $oldLegacyCloud = $env:NETCHECK_LEGACY_CLOUD_SETTINGS
    try {
        $env:NETCHECK_PORTABLE_SETTINGS = Join-Path $testRoot 'Unified\NetCheckMonitor.settings.json'
        $env:NETCHECK_LEGACY_MONITOR_DIR = Join-Path $testRoot 'NoLegacyMonitor'
        $env:NETCHECK_LEGACY_CLOUD_SETTINGS = Join-Path $testRoot 'NoLegacyCloud\settings.dat'
        $languageType = $assembly.GetType('NetCheck.LanguagePreferenceStore', $true)
        $languageArgs = [object[]]::new(1); $languageArgs[0] = [string]'en-US'
        $languageType.GetMethod('Save', [Reflection.BindingFlags]'Static,NonPublic').Invoke($null, $languageArgs)
        $uiType = $assembly.GetType('NetCheck.UiPreferenceStore', $true)
        $uiType.GetMethod('MarkCloseToTrayNoticeShown', [Reflection.BindingFlags]'Static,NonPublic').Invoke($null, @())
        $monitorType = $assembly.GetType('NetCheck.MonitorSettingsStore', $true)
        $monitorValueType = $assembly.GetType('NetCheck.MonitorTargetSettings', $true)
        $monitorValue = [Activator]::CreateInstance($monitorValueType)
        $monitorValue.AutoStartMonitoring = $true
        $monitorValue.CustomTargets = New-Object 'System.Collections.Generic.List[string]'
        $monitorArgs = [object[]]::new(1); $monitorArgs[0] = $monitorValue
        $monitorType.GetMethod('Save', [Reflection.BindingFlags]'Static,Public').Invoke($null, $monitorArgs)
        $scheduleArgs = [object[]]::new(1); $scheduleArgs[0] = [string]'21:30'
        $type.GetMethod('SaveCloudBackupSchedule', $flags).Invoke($null, $scheduleArgs)
        $unified = [IO.File]::ReadAllText($env:NETCHECK_PORTABLE_SETTINGS)
        $unifiedOk = $unified.Contains('"Language":"en-US"') -and $unified.Contains('"CloseToTrayNoticeShown":true') -and $unified.Contains('"AutoStartMonitoring":true') -and $unified.Contains('"CloudBackupSchedule":"21:30"')
    }
    finally {
        $env:NETCHECK_PORTABLE_SETTINGS = $oldPortable
        $env:NETCHECK_LEGACY_MONITOR_DIR = $oldLegacyMonitor
        $env:NETCHECK_LEGACY_CLOUD_SETTINGS = $oldLegacyCloud
    }
    $cloudType = $assembly.GetType('NetCheck.CloudBackupManager', $true)
    $cloudArgs = [object[]]::new(1); $cloudArgs[0] = [string](Join-Path $testRoot 'cloud.dat')
    $cloudOk = [bool]$cloudType.GetMethod('RunStorageSelfTest', [Reflection.BindingFlags]'Static,Public').Invoke($null, $cloudArgs)
    $sessionType = $assembly.GetType('NetCheck.SessionStateStore', $true)
    $sessionArgs = [object[]]::new(1); $sessionArgs[0] = [string](Join-Path $testRoot 'session.json')
    $sessionOk = [bool]$sessionType.GetMethod('RunStorageSelfTest', [Reflection.BindingFlags]'Static,Public').Invoke($null, $sessionArgs)
    if (-not ($migrationOk -and $besideExecutable -and $unifiedOk -and $cloudOk -and $sessionOk)) { throw 'Portable settings migration probe failed.' }
    Write-Output 'Portable settings paths and one-time legacy migration passed.'
}
finally {
    $resolvedRoot = [IO.Path]::GetFullPath($testRoot)
    if ([IO.Path]::GetDirectoryName($resolvedRoot) -ne [IO.Path]::GetFullPath($root)) { throw 'Unsafe portable settings test path.' }
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
